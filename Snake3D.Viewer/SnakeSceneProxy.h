////////////////////////////////////////////////////////////////
//┌─────────────────────────────┐
//│  C# (WPF)— SnakeGame        │  игровая логика, таймер, ввод, счёт
//├─────────────────────────────┤
//│  C++ / CLI — SnakeSceneProxy│  «мост» : управляемый класс - обёртка
//├─────────────────────────────┤
//│  OpenCASCADE(нативный C++)  │  геометрия, STEP - модели, рендер OpenGL
//└─────────────────────────────┘
////////////////////////////////////////////////////////////////////////////

//Ключевая идея : C# ничего не знает про OCCT, OCCT ничего не знает про WPF.
// Посредник — SnakeSceneProxy на C++ / CLI.Это управляемый(ref class) класс, 
// который владеет нативными OCCT - объектами и наружу отдаёт простой 
// API : AddSegment, MoveHead, FollowHead, RemoveCrystalAt и т.д.

//Деление ответственности :

//C# решает где находятся объекты и что происходит(логика игры, физика, коллизии, интерполяция).
//C++ / CLI + OCCT решает как это нарисовать(построение BRep - геометрии, матрицы трансформаций, камера, окно).
//Связка идёт через #pragma comment(lib, ...) — перечисление OCCT - 
// библиотек(TKernel, TKV3d, TKOpenGl, TKBool и т.д.) 
// и #pragma warning(disable: 4263 4264) — подавление предупреждений о том, что деструктор ~SnakeSceneProxy() 
// и финализатор !SnakeSceneProxy() имеют одинаковую сигнатуру
// (в C++ / CLI оба компилируются в Finalize, различаются только семантикой вызова).

#pragma once
#pragma warning(disable: 4263 4264)
#include "pch.h"
#include <Windows.h>
#include <Standard_Version.hxx>
#include <Aspect_DisplayConnection.hxx>
#include <WNT_Window.hxx>
#include <OpenGl_GraphicDriver.hxx>
#include <V3d_Viewer.hxx>
#include <V3d_View.hxx>
#include <AIS_InteractiveContext.hxx>
#include <AIS_Shape.hxx>
#include <TopoDS_Shape.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepBndLib.hxx>
#include <NCollection_Haft.h>
#include <gp_Ax2.hxx>
#include <gp_Vec.hxx>
#include <gp_Pnt.hxx>
#include <gp_XYZ.hxx>
#include <vector>
#include <cmath>
#include <vcclr.h>
#include <msclr/marshal.h>
#include <Graphic3d_Camera.hxx>
#include <STEPControl_Reader.hxx>
#include <IFSelect_ReturnStatus.hxx>
#include <BRepBuilderAPI_Transform.hxx>

#include <V3d_AmbientLight.hxx>
#include <V3d_DirectionalLight.hxx>

//Строим звезды
#include <BRep_Builder.hxx>
#include <TopoDS_Compound.hxx>
#include <random>


//Подсветка ячеек
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <map>

#include <msclr/marshal_cppstd.h>

#pragma comment(lib, "TKernel.lib")
#pragma comment(lib, "TKMath.lib")
#pragma comment(lib, "TKBRep.lib")
#pragma comment(lib, "TKXSBase.lib")
#pragma comment(lib, "TKService.lib")
#pragma comment(lib, "TKV3d.lib")
#pragma comment(lib, "TKOpenGl.lib")
#pragma comment(lib, "TKPrim.lib")
#pragma comment(lib, "TKBool.lib")
#pragma comment(lib, "TKTopAlgo.lib")
#pragma comment(lib, "TKBO.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "TKDESTEP.lib")
#pragma comment(lib, "TKShHealing.lib")


using namespace System;
using namespace System::Runtime::InteropServices;

namespace Snake3DViewer
{
	public ref class SnakeSceneProxy
	{
	private:
		//Управляемый класс нельзя хранить напрямую в полях нативные объекты — 
		//сборщик мусора может переместить объект в памяти, а нативный указатель 
		//«не узнает» об этом.Поэтому здесь используется NCollection_Haft — 
		//обёртка OCCT, которая хранит указатель на хендл в куче и не даёт 
		//GC двигать сам хендл :

		// OCCT-хендлы через NCollection_Haft
		NCollection_Haft<Handle(V3d_Viewer)> _viewer;
		NCollection_Haft<Handle(V3d_View)> _view;
		NCollection_Haft<Handle(AIS_InteractiveContext)> _aisContext;
		NCollection_Haft<Handle(OpenGl_GraphicDriver)> _graphicDriver;
		// Направленный свет «солнце» — сверху-позади-сбоку относительно корабля.
		// Направление обновляется в SetOrientation вместе с курсом змейки.
		NCollection_Haft<Handle(V3d_DirectionalLight)> _sunLight;
		
		// Нативные указатели на векторы 
		std::vector<Handle(AIS_Shape)>* _snakeSegments;
		std::vector<Handle(AIS_Shape)>* _walls;
		std::vector<Handle(AIS_Shape)>* _grid;
		std::vector<gp_Pnt>* _basePositions;
		std::vector<gp_Pnt>* _positions;
		std::vector<Handle(AIS_Shape)>* _crystals;
		std::vector<gp_Pnt>* _crystalBase;
		std::vector<Handle(AIS_Shape)>* _mines;
		std::vector<gp_Pnt>* _mineBase;
		std::vector<TopoDS_Shape*>* _models;            // 0=голова, 1=хвост, 2=кристалл, 4=мина
		std::vector<gp_Vec>* _segmentDirs;
		double _dirX, _dirY, _dirZ;
		double _upX, _upY, _upZ;

		//Для STEP
		TopoDS_Shape* _headModel;

		//Для камеры
		int _viewMode = 0; // 0 = вид следования (дефолт), 1 = вид из кабины

		//Анимация
		double _spinAngle = 0.0;
		static constexpr double SpinSpeed = 3.0;

		static const double WallThickness = 1.0;
		static const double WallTransparency = 0.75;
		static const double HighlightLift = 0.02;
		static const double HighlightTranspMax = 0.85;
		static const double HighlightTranspMin = 0.20;
		static const int ModelTail = 1;
		static const int ModelCrystal = 2;
		static const int ModelMine = 4;

		//Режим «без стен»: подсветка ячеек границы
		// Кэш подсвеченных ячеек: ключ → грань. Грань создаётся один раз,
		// дальше только Display/Erase — дешёвые операции.
		std::map<long long, Handle(AIS_Shape)>* _highlights;
		double _arenaHalf;  // половина арены (границы подсветки)
		double _cellSize;   // шаг сетки = размер ячейки

		// ── Звёздное небо: три групповых компаунда по яркости ──
		std::vector<Handle(AIS_Shape)>* _stars;

		//Для стен
		void AddWallBox(const gp_Pnt& p, double dx, double dy, double dz)
		{
			TopoDS_Shape w = BRepPrimAPI_MakeBox(p, dx, dy, dz).Shape();
			Handle(AIS_Shape) aisShape = new AIS_Shape(w);
			aisShape->SetDisplayMode(1);
			aisShape->SetColor(Quantity_Color(0.18, 0.18, 0.22, Quantity_TOC_RGB));
			aisShape->SetTransparency(0.75);

			// Скрыть граничные линии рёбер: по умолчанию AIS рисует контуры
			// граней серыми линиями — на светлом фоне они пробиваются.
			//aisShape->Attributes()->SetFaceBoundaryDraw(Standard_False);

			// вместо SetFaceBoundaryDraw(false):
			Handle(Prs3d_LineAspect) boundary = new Prs3d_LineAspect(
				Quantity_Color(0.18, 0.18, 0.22, Quantity_TOC_RGB), // цвет = цвет стены
				Aspect_TOL_SOLID,
				1.0);                                               // толщина 1 px
			aisShape->Attributes()->SetFaceBoundaryAspect(boundary);

			_walls->push_back(aisShape);
			_aisContext()->Display(aisShape, Standard_True);
		}

	public:
		//Доступ через скобки: _viewer() = new V3d_Viewer(...) — 
		//это разыменование хафта (получение ссылки на сам Handle).

		SnakeSceneProxy()
		{
			_viewer() = NULL;
			_view() = NULL;
			_aisContext() = NULL;
			_graphicDriver() = NULL;
			_snakeSegments = new std::vector<Handle(AIS_Shape)>();
			_walls = new std::vector<Handle(AIS_Shape)>();
			_grid = new std::vector<Handle(AIS_Shape)>();
			_highlights = new std::map<long long, Handle(AIS_Shape)>();
			_stars = new std::vector<Handle(AIS_Shape)>();
			_basePositions = new std::vector<gp_Pnt>();
			_positions = new std::vector<gp_Pnt>();
			_crystals = new std::vector<Handle(AIS_Shape)>();
			_crystalBase = new std::vector<gp_Pnt>();
			_mines = new std::vector<Handle(AIS_Shape)>();
			_mineBase = new std::vector<gp_Pnt>();
			_models = new std::vector<TopoDS_Shape*>();
			_segmentDirs = new std::vector<gp_Vec>();

			_dirX = 0.0;  _dirY = 1.0;  _dirZ = 0.0;
			_upX = 0.0;   _upY = 0.0;   _upZ = 1.0;

			_headModel = nullptr;
		}

		//Под капотом:

		//~SnakeSceneProxy() — это реализация IDisposable.Dispose().
		//Вызывается детерминированно(using или Dispose() из C#).
		// 
		//!SnakeSceneProxy() — настоящий финализатор, его вызовет GC в недетерминированный
		//момент, уже в потоке финализации, когда прокси доберут в очереди сборщика.
		// 
		//В финализаторе нельзя трогать managed - объекты(например, System::Console) — 
		//они могут быть уже собраны.Здесь оба метода делают только delete нативных указателей — это правильно.
		// 
		//Типичная ошибка новичка — забыть delete в финализаторе : тогда утечка нативной памяти 
		//при пропуске Dispose.Здесь паттерн соблюдён(кроме _walls, _snakeSegments и т.п., 
		//которые в финализаторе действительно освобождаются).
		// 
		//Нативные STL - контейнеры(std::vector<...>*) — обычные указатели в неуправляемой куче, 
		//их нужно удалять вручную, что и делается в обоих методах.

		~SnakeSceneProxy()
		{
			delete _snakeSegments;
			delete _walls;
			delete _grid;   _grid = nullptr;
			delete _highlights;   _highlights = nullptr;
			delete _stars;   _stars = nullptr;
			delete _basePositions; _basePositions = nullptr;
			delete _positions; _positions = nullptr;

			delete _headModel; _headModel = nullptr;

			delete _crystals; _crystals = nullptr;

			for (auto* m : *_models) delete m;
			delete _models;        _models = nullptr;
			delete _crystalBase;   _crystalBase = nullptr;
			delete _mines;         _mines = nullptr;
			delete _mineBase;      _mineBase = nullptr;
			delete _segmentDirs;   _segmentDirs = nullptr;
		}

		!SnakeSceneProxy()
		{
			delete _snakeSegments;
			delete _walls;
			delete _grid;   _grid = nullptr;
			delete _highlights;   _highlights = nullptr;
			delete _stars;   _stars = nullptr;
			delete _basePositions; _basePositions = nullptr;
			delete _positions; _positions = nullptr;

			delete _headModel; _headModel = nullptr;

			delete _crystals; _crystals = nullptr;

			for (auto* m : *_models) delete m;
			delete _models;        _models = nullptr;
			delete _crystalBase;   _crystalBase = nullptr;
			delete _mines;         _mines = nullptr;
			delete _mineBase;      _mineBase = nullptr;
			delete _segmentDirs;   _segmentDirs = nullptr;
		}

		TopoDS_Shape* GetModel(int kind)
		{
			if (kind < 0 || (int)_models->size() <= kind) return nullptr;
			return (*_models)[kind];
		}

		//Методы LoadStepModel(path, kind, targetSize) и LoadHeadModel(path, targetRadius) 
		//решают одну задачу : привести произвольную CAD - модель к единому «игровому» 
		//масштабу и ориентации.
		//kind: 1=хвост, 2=кристалл, 4=мина. targetSize — габарит модели в единицах 
		//арены.
		bool LoadStepModel(System::String^ path, int kind, double targetSize)
		{
			if (kind < 0 || kind > 4) return false;

			msclr::interop::marshal_context ctx;
			std::string nativePath = ctx.marshal_as<std::string>(path);

			//1. Чтение: STEPControl_Reader::ReadFile → TransferRoots() — STEP - 
			// файл это иерархия корневых сущностей; после переноса все собранные 
			// формы доступны через NbShapes(), OneShape() склеивает их в одну 
			// TopoDS_Shape.
			STEPControl_Reader reader;
			if (reader.ReadFile(nativePath.c_str()) != IFSelect_RetDone) return false;
			reader.TransferRoots();
			if (reader.NbShapes() < 1) return false;

			TopoDS_Shape raw = reader.OneShape();

			//2. Габариты: BRepBndLib::Add(shape, box) строит осеориентированный 
			// bounding box.Из него берём максимальный размер по осям — 
			// это «диагональ» модели.
			Bnd_Box box;
			BRepBndLib::Add(raw, box);
			Standard_Real xmin, ymin, zmin, xmax, ymax, zmax;
			box.Get(xmin, ymin, zmin, xmax, ymax, zmax);

			double dx = xmax - xmin, dy = ymax - ymin, dz = zmax - zmin;
			double maxDim = (std::max)(dx, (std::max)(dy, dz));
			if (maxDim < 1e-9) return false;

			//3. Масштабирование: scale = targetSize / maxDim — модель вписывается 
			// в заданный габарит арены.Например, кристалл получает габарит 3.0, 
			// мина — 1.5, голова — длину, равную 4 × радиус 
			// сегмента(как у старой капсулы, чтобы игровой радиус коллизии совпал 
			// с визуальным).
			double scale = targetSize / maxDim;

			//4. Центрирование: Транформацией gp_Trsf::SetTranslation(gp_Vec(c, gp_Pnt(0, 0, 0))) 
			// центр bbox переносится в начало координат.Это критично : дальше все 
			// вращения идут вокруг нуля, и модель крутится на месте, а не по орбите.
			// центр в начало координат — трансформы потом работают вокруг нуля
			gp_Pnt c((xmin + xmax) / 2.0, (ymin + ymax) / 2.0, (zmin + zmax) / 2.0);
			gp_Trsf toOrigin;
			toOrigin.SetTranslation(gp_Vec(c, gp_Pnt(0, 0, 0)));
			TopoDS_Shape shaped = BRepBuilderAPI_Transform(raw, toOrigin, Standard_True).Shape();

			gp_Trsf scaler;
			scaler.SetScale(gp_Pnt(0, 0, 0), scale);
			shaped = BRepBuilderAPI_Transform(shaped, scaler, Standard_True).Shape();

			while ((int)_models->size() <= kind) _models->push_back(nullptr);
			delete (*_models)[kind];
			(*_models)[kind] = new TopoDS_Shape(shaped);
			return true;
		}

		bool LoadHeadModel(System::String^ path, double targetRadius)
		{
			msclr::interop::marshal_context ctx;
			std::string nativePath = ctx.marshal_as<std::string>(path);

			STEPControl_Reader reader;
			if (reader.ReadFile(nativePath.c_str()) != IFSelect_RetDone)
				return false;

			reader.TransferRoots();
			if (reader.NbShapes() < 1)
				return false;

			TopoDS_Shape raw = reader.OneShape();

			//габариты
			Bnd_Box box;
			BRepBndLib::Add(raw, box);
			Standard_Real xmin, ymin, zmin, xmax, ymax, zmax;
			box.Get(xmin, ymin, zmin, xmax, ymax, zmax);

			double lenY = ymax - ymin;
			if (lenY < 1e-9)
				return false;

			// длина головы = 4 * radius, как у старой капсулы
			double scale = (targetRadius * 4.0) / lenY;

			// центр в начало координат
			gp_Pnt c((xmin + xmax) / 2.0, (ymin + ymax) / 2.0, (zmin + zmax) / 2.0);
			gp_Trsf toOrigin;
			toOrigin.SetTranslation(gp_Vec(c, gp_Pnt(0, 0, 0)));

			TopoDS_Shape centered = BRepBuilderAPI_Transform(raw, toOrigin, Standard_True).Shape();

			gp_Trsf scaler;
			scaler.SetScale(gp_Pnt(0, 0, 0), scale);
			TopoDS_Shape scaled = BRepBuilderAPI_Transform(centered, scaler, Standard_True).Shape();

			//Переориентация головы.SetRotation(gp_Ax1(O, gp_Dir(0, 0, 1)), -π / 2) — 
			// поворот вокруг Z, чтобы продольная ось модели совпала с осью полёта 
			// Y(в игре _forward = (0, 1, 0)).
			gp_Trsf reorient;
			reorient.SetRotation(gp_Ax1(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1)), -M_PI / 2.0);
			scaled = BRepBuilderAPI_Transform(scaled, reorient, Standard_True).Shape();

			delete _headModel;
			_headModel = new TopoDS_Shape(scaled);
			return true;
		}

		//Инициализация 3д сцены - построение графической иерархии.
		bool Initialize(System::IntPtr windowHandle)
		{
			try
			{
				Handle(Aspect_DisplayConnection) dispCon;
				_graphicDriver() = new OpenGl_GraphicDriver(dispCon);  //Рендер OpenGL

				_viewer() = new V3d_Viewer(_graphicDriver());          //Сцена
				//_viewer()->SetDefaultLights();                         //Свет
				//_viewer()->SetLightOn();
				// Свет: headlight + солнце + заполняющий
				_viewer()->SetDefaultLights();
				_viewer()->SetLightOn();

				// «Солнце»: сверху-позади относительно корабля — луч летит в −Y и −Z,
				// значит источник стоит со стороны +Y/+Z. Направление обновляет SetOrientation.
				Handle(V3d_DirectionalLight) sun = new V3d_DirectionalLight(gp_Dir(0.0, -1.0, -1.2));
				sun->SetColor(Quantity_Color(1.0, 0.95, 0.85, Quantity_TOC_RGB)); // тёплый
				sun->SetIntensity(1.0);
				_viewer()->AddLight(sun);
				_viewer()->SetLightOn(sun);

				//// Заполняющий: с обратной стороны (снизу-спереди), слабый и холодный —
				//// подсвечивает то, что солнце оставило в тени. Замена амбиенту.
				//Handle(V3d_DirectionalLight) fill = new V3d_DirectionalLight(gp_Dir(0.0, 1.0, 0.5));
				//fill->SetColor(Quantity_Color(0.35, 0.38, 0.45, Quantity_TOC_RGB));
				//fill->SetIntensity(0.4);
				//_viewer()->AddLight(fill);
				//_viewer()->SetLightOn(fill);

				_view() = _viewer()->CreateView();                     //Вьюпорт

				//Привязка к HWND
				//WNT_Window — обёртка над существующим HWND(панель WPF через WindowsFormsHost 
				//или WinForms Panel).OCCT не создаёт своё окно, а рисует прямо в 
				//клиентскую область чужого окна.wntWindow->Map() нужен, 
				//чтобы OCCT знал, что окно видимо.
				Handle(WNT_Window) wntWindow = new WNT_Window(reinterpret_cast<HWND>(windowHandle.ToPointer()));
				_view()->SetWindow(wntWindow);
				if (!wntWindow->IsMapped()) wntWindow->Map();

				//Менеджер отображения
				//AIS_InteractiveContext — центральный менеджер : хранит все 
				//отображаемые объекты, их цвета, режимы, выделение.
				//Именно через него идут все Display / Erase / Redisplay.
				_aisContext() = new AIS_InteractiveContext(_viewer());
				//_aisContext()->UpdateCurrentViewer();

				//_view()->MustBeResized();
				//_view()->Redraw();

				// Настройка камеры: перспективная проекция, угол обзора 45 градусов
				_view()->Camera()->SetProjectionType(Graphic3d_Camera::Projection_Perspective);
				_view()->Camera()->SetFOVy(45.0);

				// Начальный угол камеры (смотрим немного сверху и сбоку)
				_view()->SetProj(V3d_XposYposZpos);

				_view()->MustBeResized();
				_view()->Redraw();
			}
			catch (Standard_Failure& e)
			{
				System::Console::WriteLine("OCCT Error: {0}", gcnew System::String(e.GetMessageString()));
				return false;
			}
			return true;
		}

		//При изменении размера окна WPF сначала меняет размер самого 
		//HWND(через WinAPI SetWindowPos), затем вызывается _view()->MustBeResized() — 
		//OCCT пересчитывает вьюпорт и матрицу проекции.Без MustBeResized() картинка 
		//растянется или обрежется.
		// Метод изменения размера: использует WinAPI SetWindowPos
		void Resize(int width, int height)
		{
			if (!_view().IsNull() && !_view()->Window().IsNull())
			{
				// 1. Правильный DownCast: Aspect_Window -> WNT_Window
				Handle(WNT_Window) wntWindow = Handle(WNT_Window)::DownCast(_view()->Window());

				if (!wntWindow.IsNull())
				{
					// 2. Правильный каст: Aspect_Drawable (void*) -> HWND
					HWND hwnd = reinterpret_cast<HWND>(wntWindow->NativeHandle());

					if (hwnd != NULL)
					{
						SetWindowPos(hwnd, NULL, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

						_view()->MustBeResized();
						_view()->Redraw();
					}
				}
			}
		}

		void ClearScene()
		{
			RemoveAllSnake();
			ClearCrystals();
			ClearMines();

			// Стены и сетка — часть сцены: стираем адресно и чистим векторы.
			// EraseAll НЕ используем: он стёр бы и декорации, которые
			// в игровом цикле никто заново не рисует.
			for (auto& g : *_grid)
				if (!g.IsNull() && !_aisContext().IsNull())
					_aisContext()->Erase(g, Standard_False);
			_grid->clear();

			for (auto& w : *_walls)
				if (!w.IsNull() && !_aisContext().IsNull())
					_aisContext()->Erase(w, Standard_False);
			_walls->clear();

			if (!_aisContext().IsNull())
				_aisContext()->UpdateCurrentViewer();
		}

		void AddSegment(double x, double y, double z, double radius, int index)
		{
			//Фолбэки обязательны : aisShape не должен остаться пустым(Handle без объекта)
			//— иначе _aisContext()->Display(aisShape) упадёт.

			//Паттерн для фолбэков и моделей один и тот же : строится геометрия → 
			//оборачивается в AIS_Shape → SetDisplayMode(1) (заливка) → цвет → пуш 
			//в вектор → Display в контексте.
			gp_Pnt center(x, y, z);
			Handle(AIS_Shape) aisShape;
			gp_Pnt basePos = center;   // база по умолчанию — точка построения

			if (index == 0)
			{
				if (_headModel != nullptr)
				{
					// Голова — загруженная .step-модель
					aisShape = new AIS_Shape(*_headModel);
					aisShape->SetColor(Quantity_Color(0.42, 0.44, 0.48, Quantity_TOC_RGB));
					basePos = gp_Pnt(0, 0, 0);   // модель центрирована в нуле — вращение вокруг него
				}
				else
				{
					// Fallback — старая капсула
					gp_Ax2 ax2(gp_Pnt(center.X(), center.Y() - radius, center.Z()), gp_Dir(0, 1, 0));
					TopoDS_Shape cylinder = BRepPrimAPI_MakeCylinder(ax2, radius, radius * 2).Shape();
					TopoDS_Shape sphere1 = BRepPrimAPI_MakeSphere(gp_Pnt(center.X(), center.Y() + radius, center.Z()), radius).Shape();
					TopoDS_Shape sphere2 = BRepPrimAPI_MakeSphere(gp_Pnt(center.X(), center.Y() - radius, center.Z()), radius).Shape();

					BRepAlgoAPI_Fuse fuse1(cylinder, sphere1);
					fuse1.Build();
					if (!fuse1.IsDone())
					{
						BRepPrimAPI_MakeSphere fallback(center, radius);
						aisShape = new AIS_Shape(fallback.Shape());
						aisShape->SetColor(Quantity_Color(Quantity_NOC_RED));
					}
					else
					{
						BRepAlgoAPI_Fuse fuse2(fuse1.Shape(), sphere2);
						fuse2.Build();
						aisShape = new AIS_Shape(fuse2.IsDone() ? fuse2.Shape() : fuse1.Shape());
						aisShape->SetColor(Quantity_Color(0.75, 0.75, 0.75, Quantity_TOC_RGB));
					}
				}
			}
			else
			{
				TopoDS_Shape* tailModel = GetModel(1);
				if (tailModel != nullptr)
				{
					aisShape = new AIS_Shape(*tailModel);
					aisShape->SetColor(Quantity_Color(0.45, 0.95, 0.45, Quantity_TOC_RGB));
					aisShape->SetTransparency(0.25);
					basePos = gp_Pnt(0, 0, 0);

					// как у кристалла: сразу ставим в нужную точку
					gp_Trsf trans;
					trans.SetTranslation(gp_Vec(gp_Pnt(0, 0, 0), center));
					aisShape->SetLocalTransformation(trans);
				}
				else
				{
					// Фолбэк — синяя сфера (обязателен: aisShape не должен остаться пустым)
					BRepPrimAPI_MakeSphere sphere(center, radius);
					aisShape = new AIS_Shape(sphere.Shape());
					aisShape->SetColor(Quantity_Color(0.45, 0.95, 0.45, Quantity_TOC_RGB));
					aisShape->SetTransparency(0.25);
				}
			}

			aisShape->SetDisplayMode(1);
			_snakeSegments->push_back(aisShape);
			_basePositions->push_back(basePos);
			_positions->push_back(center);
			_segmentDirs->push_back(gp_Vec(0, 1, 0));
			_aisContext()->Display(aisShape, Standard_True);
		}

		void MoveSegment(int index, double x, double y, double z)
		{
			if (index < 0 || index >= static_cast<int>(_snakeSegments->size())) return;
			gp_Pnt newPos(x, y, z);
			(*_positions)[index] = newPos;

			// Направление сегмента — на предыдущий (головной) сегмент цепочки.
			// Сдвиг идёт от хвоста к голове, поэтому positions[index-1] уже актуален.
			if (index > 0)
			{
				gp_Vec toPrev((*_positions)[index - 1], newPos);
				if (toPrev.Magnitude() > 1e-6)
				{
					toPrev.Normalize();
					(*_segmentDirs)[index] = toPrev;
				}
			}

			gp_Trsf trsf;
			gp_Vec dir = (*_segmentDirs)[index];

			if (index == 0 || !GetModel(1))
			{
				// Голова или фолбэк-сферы: только перенос
				trsf.SetTranslation(gp_Vec((*_basePositions)[index], newPos));
			}
			else
			{
				// Хвост-модель: перенос + разворот вдоль цепочки + вращение
				gp_Dir fwd(dir);
				gp_Dir upRef(0, 0, 1);
				if (std::abs(fwd.Z()) > 0.999) upRef = gp_Dir(0, 1, 0);

				gp_Dir right = fwd.Crossed(upRef);
				gp_Dir up = gp_Dir(right.Crossed(fwd));

				// через один: чётные по часовой, нечётные — против
				double ang = _spinAngle * ((index % 2 == 0) ? 1.0 : -1.0);
				double c = cos(ang), s = sin(ang);
				gp_Dir r2(right.X() * c - up.X() * s,
					right.Y() * c - up.Y() * s,
					right.Z() * c - up.Z() * s);
				gp_Dir u2(up.X() * c + right.X() * s,
					up.Y() * c + right.Y() * s,
					up.Z() * c + right.Z() * s);

				trsf.SetValues(
					r2.X(), fwd.X(), u2.X(), newPos.X() - (*_basePositions)[index].X(),
					r2.Y(), fwd.Y(), u2.Y(), newPos.Y() - (*_basePositions)[index].Y(),
					r2.Z(), fwd.Z(), u2.Z(), newPos.Z() - (*_basePositions)[index].Z());
			}

			(*_snakeSegments)[index]->SetLocalTransformation(trsf);
			_aisContext()->Redisplay((*_snakeSegments)[index], Standard_False);
		}

		void MoveHead(int headIndex, double x, double y, double z)
		{
			if (headIndex < 0 || (size_t)headIndex >= _snakeSegments->size()) return;
			if ((*_snakeSegments)[headIndex].IsNull()) return;

			gp_Pnt newPos(x, y, z);
			(*_positions)[headIndex] = newPos;

			gp_Dir fwd(_dirX, _dirY, _dirZ);
			gp_Dir up(_upX, _upY, _upZ);

			gp_Dir right(fwd.Crossed(up));
			up = gp_Dir(right.Crossed(fwd));

			gp_Trsf trsf;
			// столбцы матрицы: локальный X -> right, Y -> fwd, Z -> up
			trsf.SetValues(
				right.X(), fwd.X(), up.X(), newPos.X() - (*_basePositions)[headIndex].X(),
				right.Y(), fwd.Y(), up.Y(), newPos.Y() - (*_basePositions)[headIndex].Y(),
				right.Z(), fwd.Z(), up.Z(), newPos.Z() - (*_basePositions)[headIndex].Z());

			(*_snakeSegments)[headIndex]->SetLocalTransformation(trsf);
			_aisContext()->Redisplay((*_snakeSegments)[headIndex], Standard_False);
		}

		/*void AddWall(TopoDS_Shape shape)
		{
			Handle(AIS_Shape) aisShape = new AIS_Shape(shape);
			aisShape->SetDisplayMode(1);
			aisShape->SetColor(Quantity_Color(Quantity_NOC_GREEN));
			_walls->push_back(aisShape);
			_aisContext()->Display(aisShape, Standard_True);
		}*/

		void CreateWalls(double arenaSize)
		{
			double half = arenaSize / 2.0;
			double thickness = 1.0;

			// Левая и правая (X)
			AddWallBox(gp_Pnt(-half - thickness / 2.0, -half, -half), thickness, arenaSize, arenaSize);
			AddWallBox(gp_Pnt(half - thickness / 2.0, -half, -half), thickness, arenaSize, arenaSize);

			// Передняя и задняя (Y)
			AddWallBox(gp_Pnt(-half, -half - thickness / 2.0, -half), arenaSize, thickness, arenaSize);
			AddWallBox(gp_Pnt(-half, half - thickness / 2.0, -half), arenaSize, thickness, arenaSize);

			// Пол и потолок (Z)
			AddWallBox(gp_Pnt(-half, -half, -half - thickness / 2.0), arenaSize, arenaSize, thickness);
			AddWallBox(gp_Pnt(-half, -half, half - thickness / 2.0), arenaSize, arenaSize, thickness);
		}

		// Процедурное звёздное небо: N случайных точек на сфере радиуса radius
		// вокруг центра арены. Звёзды — настоящая 3D-геометрия, поэтому небо
		// «поворачивается» вместе с камерой без какого-либо кода фона.
		//
		// Ключевой приём: звёзды НЕ создаются как 400 отдельных AIS_Shape —
		// они группируются по яркости и склеиваются в 3 компаунда
		// (TopoDS_Compound). Один Display на группу вместо одного на звезду.
		//void CreateStarSky() { CreateStarSky(800, 240.0); }
		void CreateStarSky(int count, double radius)
		{
			std::mt19937 rng(42); // фиксированное зерно: небо одинаково при каждом запуске
			std::normal_distribution<double>      gauss(0.0, 1.0);
			std::uniform_real_distribution<double> uni(0.0, 1.0);

			// Три группы яркости: тусклые (60%), средние (30%), яркие (10%)
			std::vector<TopoDS_Shape> groups[3];

			for (int k = 0; k < count; ++k)
			{
				// Равномерное направление на сфере: три гауссовых координаты,
				// нормировка. Тривиальный подход «угол = random*2π» даёт
				// сгущение к полюсам — здесь этого нет.
				double x = gauss(rng), y = gauss(rng), z = gauss(rng);
				double len = std::sqrt(x * x + y * y + z * z);
				if (len < 1e-9) continue;
				x /= len; y /= len; z /= len;

				// Лёгкий разброс радиуса (±10%) — глубина, не влияет на картинку
				double r = radius * (0.9 + 0.2 * uni(rng));
				gp_Pnt p(x * r, y * r, z * r);

				// Размер звезды: 0.2..1.2 — на расстоянии 120 это 1–6 пикселей
				double s = 0.2 + 1.0 * uni(rng);
				TopoDS_Shape star = BRepPrimAPI_MakeBox(
					gp_Pnt(p.X() - s / 2, p.Y() - s / 2, p.Z() - s / 2),
					gp_Pnt(p.X() + s / 2, p.Y() + s / 2, p.Z() + s / 2)).Shape();

				double b = uni(rng);
				groups[b < 0.6 ? 0 : (b < 0.9 ? 1 : 2)].push_back(star);
			}

			static const double cols[3][3] = {
				{ 0.55, 0.55, 0.65 },  // тусклые — холодный серый
				{ 0.80, 0.80, 0.90 },  // средние
				{ 1.00, 1.00, 1.00 }   // яркие — чистый белый
			};

			for (int g = 0; g < 3; ++g)
			{
				if (groups[g].empty()) continue;

				BRep_Builder bb;
				TopoDS_Compound comp;
				bb.MakeCompound(comp);
				for (const auto& s : groups[g]) bb.Add(comp, s);

				Handle(AIS_Shape) ais = new AIS_Shape(comp);
				ais->SetDisplayMode(1);
				ais->SetColor(Quantity_Color(cols[g][0], cols[g][1], cols[g][2], Quantity_TOC_RGB));
				_aisContext()->Display(ais, Standard_False);
				_stars->push_back(ais);
			}
			_aisContext()->UpdateCurrentViewer();
		}

		//void CreateArenaWalls(double size)
		//{
		//    double half = size / 2.0;
		//    double step = size / 2.0;

		//    // --- Задняя (Z = -half) и передняя (Z = +half) ---
		//    for (double s = -half; s <= half + 0.001; s += step)
		//    {
		//        AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, half, -half));
		//        AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(half, s, -half));
		//        AddArenaLine(gp_Pnt(s, -half, half), gp_Pnt(s, half, half));
		//        AddArenaLine(gp_Pnt(-half, s, half), gp_Pnt(half, s, half));
		//    }

		//    // --- Левая (X = -half) и правая (X = +half) ---
		//    for (double s = -half; s <= half + 0.001; s += step)
		//    {
		//        AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(-half, s, half));
		//        AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(-half, half, s));
		//        AddArenaLine(gp_Pnt(half, s, -half), gp_Pnt(half, s, half));
		//        AddArenaLine(gp_Pnt(half, -half, s), gp_Pnt(half, half, s));
		//    }

		//    // --- Пол (Y = -half) и потолок (Y = +half) ---
		//    for (double s = -half; s <= half + 0.001; s += step)
		//    {
		//        AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, -half, half));
		//        AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(half, -half, s));
		//        AddArenaLine(gp_Pnt(s, half, -half), gp_Pnt(s, half, half));
		//        AddArenaLine(gp_Pnt(-half, half, s), gp_Pnt(half, half, s));
		//    }
		//}

		void AddArenaLine(const gp_Pnt& p1, const gp_Pnt& p2)
		{
			BRepBuilderAPI_MakeEdge edge(p1, p2);
			Handle(AIS_Shape) aisEdge = new AIS_Shape(edge.Shape());
			aisEdge->SetDisplayMode(0);
			aisEdge->SetColor(Quantity_Color(Quantity_NOC_BLACK));
			_aisContext()->Display(aisEdge, Standard_True);
			_grid->push_back(aisEdge);
		}

		void ClearCrystals()
		{
			for (auto& c : *_crystals)
				if (!c.IsNull() && !_aisContext().IsNull())
					_aisContext()->Erase(c, Standard_False);
			_crystals->clear();
		}

		void AddCrystalAt(int index, double x, double y, double z, double radius)
		{
			gp_Pnt center(x, y, z);
			Handle(AIS_Shape) aisShape;
			TopoDS_Shape* model = GetModel(2);

			if (model != nullptr)
			{
				aisShape = new AIS_Shape(*model);
				aisShape->SetColor(Quantity_Color(0.40, 0.65, 1.00, Quantity_TOC_RGB)); // светло-синий
				aisShape->SetTransparency(0.25);
			}
			else
			{
				BRepPrimAPI_MakeSphere sphere(center, radius);
				aisShape = new AIS_Shape(sphere.Shape());
				aisShape->SetColor(Quantity_Color(Quantity_NOC_CYAN));
			}
			aisShape->SetDisplayMode(1);

			if (index >= (int)_crystals->size())    _crystals->resize(index + 1);
			if (index >= (int)_crystalBase->size()) _crystalBase->resize(index + 1, gp_Pnt(0, 0, 0));
			(*_crystals)[index] = aisShape;
			(*_crystalBase)[index] = center;

			if (model != nullptr)
			{
				gp_Trsf trans;
				trans.SetTranslation(gp_Vec(gp_Pnt(0, 0, 0), center));
				aisShape->SetLocalTransformation(trans);
			}

			_aisContext()->Display(aisShape, Standard_True);
		}

		// Ориентирует все кристаллы 
		void UpdateCrystalOrientations()
		{
			if (_crystals->empty()) return;
			if (_aisContext().IsNull()) return;

			gp_Dir fwd(_dirX, _dirY, _dirZ);
			gp_Dir up(_upX, _upY, _upZ);

			gp_Dir right(fwd.Crossed(up));
			up = gp_Dir(right.Crossed(fwd));

			// вращение вокруг вертикали (up): поворачиваем right и fwd, ось up 
			// не трогаем
			//Это формула Родригеса в упрощённом виде для угла вокруг фиксированной оси.
			//Один общий _spinAngle экономит состояние : не нужно хранить фазу
			//каждого кристалла.UpdateSpin(dt) инкрементирует угол и заворачивает 
			//его в 2π — чтобы double не рос бесконечно и не терял точность.
			double c = cos(_spinAngle), s = sin(_spinAngle);
			gp_Dir rightR(right.X() * c - fwd.X() * s,
				right.Y() * c - fwd.Y() * s,
				right.Z() * c - fwd.Z() * s);
			gp_Dir fwdR(fwd.X() * c + right.X() * s,
				fwd.Y() * c + right.Y() * s,
				fwd.Z() * c + right.Z() * s);

			//// вращение вокруг оси полёта — заметно даже для симметричной модели
			//double c = cos(_spinAngle), s = sin(_spinAngle);
			//gp_Dir upR(up.X() * c - right.X() * s,
			//    up.Y() * c - right.Y() * s,
			//    up.Z() * c - right.Z() * s);
			//gp_Dir rightR(right.X() * c + up.X() * s,
			//    right.Y() * c + up.Y() * s,
			//    right.Z() * c + up.Z() * s);

			for (size_t i = 0; i < _crystals->size(); ++i)
			{
				if ((*_crystals)[i].IsNull()) continue;

				gp_Pnt pos = (*_crystalBase)[i];

				//Метод SetValues принимает матрицу по строкам.
				//Первые три столбца — базис локальной системы координат
				//(куда смотрят локальные X, Y, Z объекта), 
				//четвёртый — вектор переноса.Это стандартная компоновка : 
				//строим ортонормированный базис(right, forward, up) и кладём его
				//в матрицу.
				gp_Trsf trsf;
				trsf.SetValues(
					rightR.X(), fwdR.X(), up.X(), pos.X(),
					rightR.Y(), fwdR.Y(), up.Y(), pos.Y(),
					rightR.Z(), fwdR.Z(), up.Z(), pos.Z());

				(*_crystals)[i]->SetLocalTransformation(trsf);
				_aisContext()->Redisplay((*_crystals)[i], Standard_False);
			}
		}

		void UpdateSpin(double dt)
		{
			_spinAngle += dt * SpinSpeed;
			// держим угол в пределах 2π, чтобы double не рос бесконечно
			if (_spinAngle > 2.0 * M_PI) _spinAngle -= 2.0 * M_PI;
		}

		void RemoveCrystalAt(int index)
		{
			if (index < 0 || index >= static_cast<int>(_crystals->size())) return;
			if (!(*_crystals)[index].IsNull() && !_aisContext().IsNull())
				_aisContext()->Erase((*_crystals)[index], Standard_False);
			_crystals->erase(_crystals->begin() + index);
			if (index < (int)_crystalBase->size())
				_crystalBase->erase(_crystalBase->begin() + index);
		}

		void RemoveMineAt(int index)
		{
			if (index < 0 || index >= static_cast<int>(_mines->size())) return;
			if (!(*_mines)[index].IsNull() && !_aisContext().IsNull())
				_aisContext()->Erase((*_mines)[index], Standard_False);
			_mines->erase(_mines->begin() + index);
			if (index < (int)_mineBase->size())
				_mineBase->erase(_mineBase->begin() + index);
		}

		//Мины
		void ClearMines()
		{
			for (auto& m : *_mines)
				if (!m.IsNull() && !_aisContext().IsNull())
					_aisContext()->Erase(m, Standard_False);
			_mines->clear();
			_mineBase->clear();
		}

		void AddMineAt(int index, double x, double y, double z, double radius)
		{
			gp_Pnt center(x, y, z);
			Handle(AIS_Shape) aisShape;
			TopoDS_Shape* model = GetModel(4);

			if (model != nullptr)
			{
				aisShape = new AIS_Shape(*model);
				aisShape->SetColor(Quantity_Color(Quantity_NOC_ORANGE));
			}
			else
			{
				BRepPrimAPI_MakeSphere sphere(center, radius);
				aisShape = new AIS_Shape(sphere.Shape());
				aisShape->SetColor(Quantity_Color(Quantity_NOC_RED));
			}
			aisShape->SetDisplayMode(1);

			if (index >= (int)_mines->size())    _mines->resize(index + 1);
			if (index >= (int)_mineBase->size()) _mineBase->resize(index + 1, gp_Pnt(0, 0, 0));
			(*_mines)[index] = aisShape;
			(*_mineBase)[index] = center;

			if (model != nullptr)
			{
				gp_Trsf trans;
				trans.SetTranslation(gp_Vec(gp_Pnt(0, 0, 0), center));
				aisShape->SetLocalTransformation(trans);
			}

			_aisContext()->Display(aisShape, Standard_True);
		}

		void AddGridLine(const gp_Pnt& p1, const gp_Pnt& p2, Quantity_NameOfColor baseColor)
		{
			BRepBuilderAPI_MakeEdge edge(p1, p2);
			Handle(AIS_Shape) aisEdge = new AIS_Shape(edge.Shape());

			// Режим 0 = только каркас (линии), это важно для сетки
			aisEdge->SetDisplayMode(0);

			// Создаем серый цвет вручную (R=105, G=105, B=105 примерно как DIMGRAY)
			aisEdge->SetColor(Quantity_Color(Quantity_NOC_BLUEVIOLET));

			_aisContext()->Display(aisEdge, Standard_True);
		}

		void CreateGrid(double arenaSize, double step)
		{
			if (_aisContext().IsNull() || _view().IsNull()) return;

			double half = arenaSize / 2.0;

			for (double s = -half; s <= half + 0.001; s += step)
			{
				// Z = ±half
				AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, half, -half));
				AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(half, s, -half));
				AddArenaLine(gp_Pnt(s, -half, half), gp_Pnt(s, half, half));
				AddArenaLine(gp_Pnt(-half, s, half), gp_Pnt(half, s, half));

				// X = ±half
				AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(-half, s, half));
				AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(-half, half, s));
				AddArenaLine(gp_Pnt(half, s, -half), gp_Pnt(half, s, half));
				AddArenaLine(gp_Pnt(half, -half, s), gp_Pnt(half, half, s));

				// Y = ±half (пол и потолок)
				AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, -half, half));
				AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(half, -half, s));
				AddArenaLine(gp_Pnt(s, half, -half), gp_Pnt(s, half, half));
				AddArenaLine(gp_Pnt(-half, half, s), gp_Pnt(half, half, s));
			}
		}

		// Запоминает геометрию арены для подсветки границы.
		// Шаг обязан совпадать с тем, что передан в CreateGrid —
		// иначе ячейки подсветки разъедутся с линиями сетки.
		void SetHighlightGrid(double arenaSize, double cellSize)
		{
			_arenaHalf = arenaSize / 2.0;   // единая формула со стенами и сеткой
			_cellSize = cellSize;
		}

		// Показать/спрятать стены (клавиша T). Сам TopoDS_Shape стен
		// не разрушается — только их видимость в контексте.
		void SetWallsVisible(bool visible)
		{
			if (_aisContext().IsNull()) return;
			for (auto& w : *_walls)
			{
				if (w.IsNull()) continue;
				if (visible) _aisContext()->Display(w, Standard_False);
				else         _aisContext()->Erase(w, Standard_False);
			}

			// Сетка повторяет судьбу стен: один флаг — обе декорации.    
			for (auto& g : *_grid)
			{
				if (g.IsNull()) continue;
				if (visible) _aisContext()->Display(g, Standard_False);
				else         _aisContext()->Erase(g, Standard_False);
			}

			_aisContext()->UpdateCurrentViewer();
		}

		// Гасит всю подсветку разом (рестарт, выход из режима)
		void ClearBoundaryHighlights()
		{
			if (_aisContext().IsNull()) return;
			for (auto& kv : *_highlights)
				if (!kv.second.IsNull())
					_aisContext()->Erase(kv.second, Standard_False);
			_highlights->clear();
			_aisContext()->UpdateCurrentViewer();
		}

		// Ключ ячейки: плоскость (axis*2+side) в старших битах,
		// индексы i, j — в младших. Уникален для каждой ячейки каждой грани.
		static long long CellKey(int axis, int side, int i, int j)
		{
			long long plane = (long long)axis * 2 + (side > 0 ? 1 : 0);
			return (plane << 32) | ((long long)(i & 0xFFFF) << 16) | (long long)(j & 0xFFFF);
		}

		// Включить/выключить подсветку одной ячейки границы
		void SetHighlightCell(int axis, int side, int i, int j, bool on)
		{
			if (_aisContext().IsNull()) return;
			long long key = CellKey(axis, side, i, j);
			auto it = _highlights->find(key);

			if (on)
			{
				if (it != _highlights->end())
				{
					// грань уже в кэше — просто вернуть на сцену
					if (!it->second.IsNull())
						_aisContext()->Display(it->second, Standard_False);
					return;
				}

				Handle(AIS_Shape) ais = MakeCellFace(axis, side, i, j);
				_aisContext()->Display(ais, Standard_False);
				(*_highlights)[key] = ais;
				_aisContext()->UpdateCurrentViewer();
			}
			else
			{
				if (it == _highlights->end()) return;
				if (!it->second.IsNull())
					_aisContext()->Erase(it->second, Standard_False);
				_highlights->erase(it);
				_aisContext()->UpdateCurrentViewer();
			}
		}

		// Уровень яркости ячейки: 0 — погасить, 1 — максимально плотная.
		// Прозрачность = обратная яркость: уровень 1 → 0.20 (плотное пятно),
		// уровень 0.3 → ~0.65 (еле заметная тень).
		// Запись в кэше при level <= 0 НЕ удаляется: грань понадобится
		// снова, когда пятно вернётся — пересоздавать BRep недёшево.
		void SetHighlightLevel(int axis, int side, int i, int j, double level)
		{
			if (_aisContext().IsNull()) return;
			if (level < 0.0) level = 0.0;
			if (level > 1.0) level = 1.0;

			long long key = CellKey(axis, side, i, j);
			auto it = _highlights->find(key);

			if (level <= 0.0)
			{
				if (it != _highlights->end() && !it->second.IsNull())
					_aisContext()->Erase(it->second, Standard_False);
				return;
			}

			if (it == _highlights->end())
			{
				(*_highlights)[key] = MakeCellFace(axis, side, i, j);
				it = _highlights->find(key);
			}

			Handle(AIS_Shape) ais = it->second;
			if (ais.IsNull()) return;

			ais->SetTransparency(0.85 - 0.65 * level);
			// Display после Erase: погашенная ячейка из кэша снова на сцене
			_aisContext()->Display(ais, Standard_False);
			//UpdateCurrentViewer здесь НЕТ — его вызывает CommitHighlights
		}

		// Одна перерисовка на весь пакет изменений уровня
		void CommitHighlights()
		{
			if (!_aisContext().IsNull())
				_aisContext()->UpdateCurrentViewer();
		}

		// Строит квадрат-грань одной ячейки на заданной плоскости границы
		Handle(AIS_Shape) MakeCellFace(int axis, int side, int i, int j)
		{
			// Сдвиг +0.02 наружу: ячейка не должна лежать точно на линии сетки
			// (координаты совпадают до бита) — иначе z-fighting, грань то видна,
			// то проваливается под линии при вращении камеры.
			double h = _arenaHalf + 0.02;
			double u1 = -_arenaHalf + i * _cellSize;
			double u2 = u1 + _cellSize;
			double v1 = -_arenaHalf + j * _cellSize;
			double v2 = v1 + _cellSize;

			gp_Pnt p[4];
			switch (axis)
			{
			case 0: // плоскость X = ±h; u — вдоль Y, v — вдоль Z
				p[0] = gp_Pnt(side * h, u1, v1);
				p[1] = gp_Pnt(side * h, u2, v1);
				p[2] = gp_Pnt(side * h, u2, v2);
				p[3] = gp_Pnt(side * h, u1, v2);
				break;
			case 1: // плоскость Y = ±h; u — вдоль X, v — вдоль Z
				p[0] = gp_Pnt(u1, side * h, v1);
				p[1] = gp_Pnt(u2, side * h, v1);
				p[2] = gp_Pnt(u2, side * h, v2);
				p[3] = gp_Pnt(u1, side * h, v2);
				break;
			default: // axis == 2: плоскость Z = ±h; u — вдоль X, v — вдоль Y
				p[0] = gp_Pnt(u1, v1, side * h);
				p[1] = gp_Pnt(u2, v1, side * h);
				p[2] = gp_Pnt(u2, v2, side * h);
				p[3] = gp_Pnt(u1, v2, side * h);
				break;
			}

			// Замкнутый полигон из 4 точек → провод (Wire) → плоская грань (Face).
			// Mode 1 (заливка) работает только с гранями — набор рёбер не подойдёт.
			BRepBuilderAPI_MakePolygon poly(p[0], p[1], p[2], p[3], Standard_True);
			TopoDS_Shape face = BRepBuilderAPI_MakeFace(poly.Wire(), Standard_True).Shape();

			Handle(AIS_Shape) ais = new AIS_Shape(face);
			ais->SetDisplayMode(1);
			ais->SetColor(Quantity_Color(0.13, 0.55, 0.30, Quantity_TOC_RGB)); // изумрудный — тёмнее оранжевого раза в три // оранжевый
			ais->SetTransparency(0.2);                                         // сцена просвечивает
			return ais;
		}

		/*bool CheckCollision(const TopoDS_Shape& s1, const TopoDS_Shape& s2)
		{
			BRepExtrema_DistShapeShape dist(s1, s2);
			return dist.Value() <= 0.001;
		}

		bool IsHeadColliding()
		{
			if (_snakeSegments->empty()) return false;
			TopoDS_Shape headShape = (*_snakeSegments)[0]->Shape();
			for (const auto& wall : *_walls)
				if (CheckCollision(headShape, wall->Shape())) return true;
			for (size_t i = 2; i < _snakeSegments->size(); ++i)
				if (CheckCollision(headShape, (*_snakeSegments)[i]->Shape())) return true;
			return false;
		}*/


		// Служебные методы отладки: вызываются только из UI-отладки
		/*void Zoom(int x1, int y1, int x2, int y2)
		{
			if (!_view().IsNull()) _view()->Zoom(x1, y1, x2, y2);
		}*/

		// Колёсный зум: factor > 1 — приблизить, factor < 1 — отдалить.
		// Работаем напрямую со Scale камеры: старый Zoom(x1,y1,x2,y2)
		// использует только длину вектора и не различает направление колеса.
		void ZoomBy(double factor)
		{
			if (_view().IsNull()) return;
			Handle(Graphic3d_Camera) cam = _view()->Camera();
			cam->SetScale(cam->Scale() / factor);
			_view()->Redraw();
		}

		void Pan(int x, int y)
		{
			if (!_view().IsNull()) _view()->Pan(x, y);
		}

		void Rotate(int x, int y)
		{
			if (!_view().IsNull()) _view()->Rotation(x, y);
		}

		void StartRotate(int x, int y)
		{
			if (!_view().IsNull()) _view()->StartRotation(x, y);
		}

		void Redraw()
		{
			if (!_view().IsNull()) _view()->Redraw();
		}

		void FitScene()
		{
			if (!_view().IsNull())
			{
				_view()->FitAll();
				_view()->ZFitAll();
				_view()->Redraw();
			}
		}

		void SetBackgroundColor(int r, int g, int b)
		{
			if (!_view().IsNull())
				_view()->SetBackgroundColor(Quantity_TOC_RGB, (float)r / 255, (float)g / 255, (float)b / 255);
		}

		//void RemoveSegment(int index)
		//{
		//	// 1. Проверка границ
		//	if (index < 0 || index >= static_cast<int>(_snakeSegments->size()))
		//		return;

		//	// 2. Получаем указатель на объект AIS_Shape
		//	Handle(AIS_Shape) shapeToRemove = (*_snakeSegments)[index];

		//	// 3. Удаляем объект из контекста отображения (это убирает его с экрана)
		//	if (!_aisContext().IsNull())
		//	{
		//		_aisContext()->Erase(shapeToRemove, Standard_False);
		//	}

		//	// 4. Удаляем ссылку из нашего локального вектора
		//	// erase сдвинет все последующие элементы влево на 1 позицию
		//	_snakeSegments->erase(_snakeSegments->begin() + index);
		//	_basePositions->erase(_basePositions->begin() + index);
		//	_positions->erase(_positions->begin() + index);

		//	//_segmentDirs параллелен _positions по индексам.
		//	// Без этой строки после первого удаления направления
		//	// поедут: dir[i] начнёт указывать на чужой сегмент,
		//	// и модели хвоста развернутся не туда.
		//	_segmentDirs->erase(_segmentDirs->begin() + index);

		//	// 5. ВАЖНО: В OCCT индексы в векторе должны совпадать с логикой C#
		//	// Так как мы используем erase, элементы после удаленного автоматически сдвинулись.
		//	// Нам НЕ нужно вызывать MoveSegment для обновления индексов, 
		//	// потому что в C# мы будем обращаться к ним по новым индексам.
		//	// Однако, если в C# вы храните старые индексы, логика сломается.
		//	// При правильной реализации в C# (сдвиг списка перед удалением) это сработает идеально.
		//}

		//void RemoveFood()
		//{
		//    // Удаляем еду из контекста, если она существует
		//    if (!_foodShape().IsNull() && !_aisContext().IsNull())
		//    {
		//        _aisContext()->Erase(_foodShape(), Standard_False);
		//        _foodShape() = Handle(AIS_Shape)(); // Обнуляем хендл
		//    }
		//}

		void RemoveAllSnake()
		{
			for (auto& seg : *_snakeSegments)
			{
				if (!_aisContext().IsNull())
					_aisContext()->Erase(seg, Standard_False);
			}
			_snakeSegments->clear();
			_basePositions->clear();
			_positions->clear();
			_segmentDirs->clear();
			if (!_aisContext().IsNull())
				_aisContext()->UpdateCurrentViewer();
		}

		void SetOrientation(double fx, double fy, double fz,
			double ux, double uy, double uz)
		{
			double dlen = sqrt(fx * fx + fy * fy + fz * fz);
			if (dlen > 0.0) { _dirX = fx / dlen; _dirY = fy / dlen; _dirZ = fz / dlen; }
			double ulen = sqrt(ux * ux + uy * uy + uz * uz);
			if (ulen > 0.0) { _upX = ux / ulen; _upY = uy / ulen; _upZ = uz / ulen; }

			// Свет сверху-позади-сбоку относительно корабля: источник на
			// S = P − f·A + u·B + r·C, луч идёт из S в P → направление
			// dir = f·A − u·B − r·C. Коэффициенты — константы прокси ниже.
			if (!_sunLight().IsNull())
			{
				gp_Vec f(_dirX, _dirY, _dirZ);
				gp_Vec u(_upX, _upY, _upZ);
				gp_Vec r = f.Crossed(u);

				static const double LightBack = 1.0;   // насколько позади
				static const double LightUp = 1.2;   // насколько выше (главный вклад)
				static const double LightSide = 0.6;   // насколько сбоку

				gp_Vec dir = f * LightBack - u * LightUp - r * LightSide;
				_sunLight()->SetDirection(gp_Dir(dir.X(), dir.Y(), dir.Z()));
			}
		}

		void ToggleViewMode()
		{
			_viewMode = (_viewMode == 0) ? 1 : 0;
		}

		void SetViewMode(int mode)
		{
			if (mode == 0 || mode == 1) _viewMode = mode;
		}

		bool IsCockpitView() { return _viewMode == 1; }

		void FollowHead(int headIndex)
		{
			if (headIndex < 0 || _snakeSegments == nullptr) return;
			if ((size_t)headIndex >= _snakeSegments->size()) return;
			if (_view().IsNull()) return;

			Handle(AIS_Shape) headShape = (*_snakeSegments)[headIndex];
			if (headShape.IsNull()) return;

			double cx = (*_positions)[headIndex].X();
			double cy = (*_positions)[headIndex].Y();
			double cz = (*_positions)[headIndex].Z();

			double camBack, camUp, lookAhead, fov, lerpSpeed;

			if (_viewMode == 1)
			{
				// Кабина
				camBack = 0.9;    // чуть позади центра головы — нос виден внизу экрана
				camUp = 0.5;    // немного выше оси, видно "капот"
				lookAhead = 30.0;   // далеко вперёд — хороший обзор арены
				fov = 75.0;   // широкое поле зрения
				lerpSpeed = 1.0;    // быстрый лерп, кабина не отстаёт от поворотов
			}
			else
			{
				// Дефолт
				camBack = 10.0;
				camUp = 3.0;
				lookAhead = 10.0;
				fov = 45.0;
				lerpSpeed = 0.05;
			}

			// Геометрия камеры жёсткая: глаз на фиксированном отлёте и высоте,
			// взгляд строго по курсу. Никакого лерпа — плавность даёт
			// интерполяция позиций змейки на стороне C#.
			Handle(Graphic3d_Camera) cam = _view()->Camera();
			cam->SetFOVy(fov);

			// Eye = голова - forward*camBack + up*camUp
			double desiredEyeX = cx - _dirX * camBack + _upX * camUp;
			double desiredEyeY = cy - _dirY * camBack + _upY * camUp;
			double desiredEyeZ = cz - _dirZ * camBack + _upZ * camUp;

			// At = голова + forward*lookAhead
			double desiredAtX = cx + _dirX * lookAhead;
			double desiredAtY = cy + _dirY * lookAhead;
			double desiredAtZ = cz + _dirZ * lookAhead;

			// 3. Текущие позиции
			//Handle(Graphic3d_Camera) cam = _view()->Camera();
			gp_Pnt currentEye = cam->Eye();
			gp_Pnt currentAt = cam->Center();

			// 4. Lerp — запаздывание камеры
			//double speed = 0.08;

			double newEyeX = currentEye.X() + (desiredEyeX - currentEye.X()) * lerpSpeed;
			double newEyeY = currentEye.Y() + (desiredEyeY - currentEye.Y()) * lerpSpeed;
			double newEyeZ = currentEye.Z() + (desiredEyeZ - currentEye.Z()) * lerpSpeed;

			double newAtX = currentAt.X() + (desiredAtX - currentAt.X()) * lerpSpeed;
			double newAtY = currentAt.Y() + (desiredAtY - currentAt.Y()) * lerpSpeed;
			double newAtZ = currentAt.Z() + (desiredAtZ - currentAt.Z()) * lerpSpeed;

			// 5. Применение: позиция + вектор up (камера вращается с кораблём)
			cam->SetEye(gp_Pnt(newEyeX, newEyeY, newEyeZ));
			cam->SetCenter(gp_Pnt(desiredAtX, desiredAtY, desiredAtZ));
			cam->SetUp(gp_Dir(_upX, _upY, _upZ));

			_view()->Redraw();
		}

		//Скалярное произведение viewDir · toPoint — это проверка «перед камерой или за ней».
		// Если точка за глазом, проекция математически «вывернута» и её координаты 
		// бессмысленны — флаг inFront предупреждает об этом.Обратите внимание : 
		// _view()->Project возвращает координаты с origin в левом нижнем углу, 
		// а WPF считает от левого верхнего — поэтому в 
		// C# понадобится sy = height - sy.
		// Проекция 3D-точки в пиксели вьюпорта.
		// inFront = true, если точка перед камерой.
		bool ProjectToScreen(double x, double y, double z,
			int% sx, int% sy, bool% inFront)
		{
			inFront = false;
			if (_view().IsNull()) return false;

			Handle(Graphic3d_Camera) cam = _view()->Camera();
			gp_Pnt eye = cam->Eye();
			gp_Vec viewDir(eye, cam->Center());
			gp_Vec toPoint(x - eye.X(), y - eye.Y(), z - eye.Z());

			if (viewDir.Dot(toPoint) <= 0)
				return true; // сзади — вызов валиден, но inFront=false

			inFront = true;

			Standard_Real px, py;
			_view()->Project(x, y, z, px, py);
			sx = (int)px;
			sy = (int)py;   // origin — левый НИЖНИЙ угол; в WPF перевернём
			return true;
		}
	};
}