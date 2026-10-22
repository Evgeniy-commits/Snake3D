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
#include <BRepExtrema_DistShapeShape.hxx>
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
    static TCollection_AsciiString ToAsciiString(String^ str)
    {
        if (String::IsNullOrEmpty(str))
            return TCollection_AsciiString();
        array<unsigned char>^ bytes = System::Text::Encoding::UTF8->GetBytes(str);
        if (bytes->Length <= 256)
        {
            pin_ptr<unsigned char> pin = &bytes[0];
            return TCollection_AsciiString(reinterpret_cast<const char*>(pin));
        }
        else
        {
            std::vector<char> buf(bytes->Length + 1);
            pin_ptr<unsigned char> pin = &bytes[0];
            memcpy(buf.data(), pin, bytes->Length);
            buf[bytes->Length] = '\0';
            return TCollection_AsciiString(buf.data());
        }
    }

    public ref class SnakeSceneProxy
    {
    private:
        // OCCT-хендлы через NCollection_Haft
        NCollection_Haft<Handle(V3d_Viewer)> _viewer;
        NCollection_Haft<Handle(V3d_View)> _view;
        NCollection_Haft<Handle(AIS_InteractiveContext)> _aisContext;
        NCollection_Haft<Handle(OpenGl_GraphicDriver)> _graphicDriver;
        NCollection_Haft<Handle(AIS_Shape)> _foodShape;

        // Нативные указатели на векторы 
        std::vector<Handle(AIS_Shape)>* _snakeSegments;
        std::vector<Handle(AIS_Shape)>* _walls;
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

        //Для стен
        void AddWallBox(const gp_Pnt& p, double dx, double dy, double dz)
        {
            TopoDS_Shape w = BRepPrimAPI_MakeBox(p, dx, dy, dz).Shape();
            Handle(AIS_Shape) aisShape = new AIS_Shape(w);
            aisShape->SetDisplayMode(1);
            aisShape->SetColor(Quantity_Color(0.18, 0.18, 0.22, Quantity_TOC_RGB));
            aisShape->SetTransparency(0.75);
            _walls->push_back(aisShape);
            _aisContext()->Display(aisShape, Standard_True);
        }

    public:
        SnakeSceneProxy()
        {
            _viewer() = NULL;
            _view() = NULL;
            _aisContext() = NULL;
            _graphicDriver() = NULL;
            _foodShape() = NULL;
            _snakeSegments = new std::vector<Handle(AIS_Shape)>();
            _walls = new std::vector<Handle(AIS_Shape)>();
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

        ~SnakeSceneProxy()
        {
            delete _snakeSegments;
            delete _walls;
            delete _basePositions;
            delete _positions;
            _basePositions = nullptr;
            _positions = nullptr;

            delete _headModel; 
            _headModel = nullptr;

            delete _crystals;
            _crystals = nullptr;

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
            delete _basePositions;
            delete _positions;
            _basePositions = nullptr;
            _positions = nullptr;

            delete _headModel; 
            _headModel = nullptr;

            delete _crystals;
            _crystals = nullptr;

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

        // kind: 1=хвост, 2=кристалл, 4=мина. targetSize — габарит модели в единицах арены.
        bool LoadStepModel(System::String^ path, int kind, double targetSize)
        {
            if (kind < 0 || kind > 4) return false;

            msclr::interop::marshal_context ctx;
            std::string nativePath = ctx.marshal_as<std::string>(path);

            STEPControl_Reader reader;
            if (reader.ReadFile(nativePath.c_str()) != IFSelect_RetDone) return false;
            reader.TransferRoots();
            if (reader.NbShapes() < 1) return false;

            TopoDS_Shape raw = reader.OneShape();

            Bnd_Box box;
            BRepBndLib::Add(raw, box);
            Standard_Real xmin, ymin, zmin, xmax, ymax, zmax;
            box.Get(xmin, ymin, zmin, xmax, ymax, zmax);

            double dx = xmax - xmin, dy = ymax - ymin, dz = zmax - zmin;
            double maxDim = (std::max)(dx, (std::max)(dy, dz));
            if (maxDim < 1e-9) return false;

            double scale = targetSize / maxDim;

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

            gp_Trsf reorient;
            reorient.SetRotation(gp_Ax1(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1)), -M_PI / 2.0);
            scaled = BRepBuilderAPI_Transform(scaled, reorient, Standard_True).Shape();

            delete _headModel;
            _headModel = new TopoDS_Shape(scaled);
            return true;
        }

        bool Initialize(System::IntPtr windowHandle)
        {
            try
            {
                Handle(Aspect_DisplayConnection) dispCon;
                _graphicDriver() = new OpenGl_GraphicDriver(dispCon);

                _viewer() = new V3d_Viewer(_graphicDriver());
                _viewer()->SetDefaultLights();
                _viewer()->SetLightOn();

                _view() = _viewer()->CreateView();

                Handle(WNT_Window) wntWindow = new WNT_Window(reinterpret_cast<HWND>(windowHandle.ToPointer()));
                _view()->SetWindow(wntWindow);
                if (!wntWindow->IsMapped()) wntWindow->Map();

                _aisContext() = new AIS_InteractiveContext(_viewer());
                _aisContext()->UpdateCurrentViewer();

                _view()->MustBeResized();
                _view()->Redraw();

                // Настройка камеры: перспективная проекция, угол обзора 45 градусов
                _view()->Camera()->SetProjectionType(Graphic3d_Camera::Projection_Perspective);
                _view()->Camera()->SetFOVy(45.0);

                // Начальный угол камеры (смотрим немного сверху и сбоку)
                _view()->SetProj(V3d_XposYposZpos);
                _view()->Redraw();
            }
            catch (Standard_Failure& e)
            {
                System::Console::WriteLine("OCCT Error: {0}", gcnew System::String(e.GetMessageString()));
                return false;
            }
            return true;
        }

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
                        // Вызываем стандартный WinAPI для изменения размера окна
                        SetWindowPos(hwnd, NULL, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

                        _view()->MustBeResized();
                        _view()->FitAll();
                        _view()->ZFitAll();
                        _view()->Redraw();

                    }
                }
            }
        }

        void ClearScene()
        {
            if (!_aisContext().IsNull())
                _aisContext()->EraseAll(Standard_False);
            _crystals->clear();
            _crystalBase->clear();    
            _mines->clear();          
            _mineBase->clear();       
            _segmentDirs->clear();    
            _snakeSegments->clear();
            _walls->clear();
            _basePositions->clear();
            _positions->clear();
            _foodShape() = Handle(AIS_Shape)();
            if (!_aisContext().IsNull())
                _aisContext()->UpdateCurrentViewer();
        }

        void AddSegment(double x, double y, double z, double radius, int index)
        {
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

        void AddWall(TopoDS_Shape shape)
        {
            Handle(AIS_Shape) aisShape = new AIS_Shape(shape);
            aisShape->SetDisplayMode(1);
            aisShape->SetColor(Quantity_Color(Quantity_NOC_GREEN));
            _walls->push_back(aisShape);
            _aisContext()->Display(aisShape, Standard_True);
        }

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

        void CreateArenaWalls(double size)
        {
            double half = size / 2.0;
            double step = size / 2.0;

            // --- Задняя (Z = -half) и передняя (Z = +half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, half, -half));
                AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(half, s, -half));
                AddArenaLine(gp_Pnt(s, -half, half), gp_Pnt(s, half, half));
                AddArenaLine(gp_Pnt(-half, s, half), gp_Pnt(half, s, half));
            }

            // --- Левая (X = -half) и правая (X = +half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(-half, s, half));
                AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(-half, half, s));
                AddArenaLine(gp_Pnt(half, s, -half), gp_Pnt(half, s, half));
                AddArenaLine(gp_Pnt(half, -half, s), gp_Pnt(half, half, s));
            }

            // --- Пол (Y = -half) и потолок (Y = +half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, -half, half));
                AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(half, -half, s));
                AddArenaLine(gp_Pnt(s, half, -half), gp_Pnt(s, half, half));
                AddArenaLine(gp_Pnt(-half, half, s), gp_Pnt(half, half, s));
            }
        }

        void AddArenaLine(const gp_Pnt& p1, const gp_Pnt& p2)
        {
            BRepBuilderAPI_MakeEdge edge(p1, p2);
            Handle(AIS_Shape) aisEdge = new AIS_Shape(edge.Shape());
            aisEdge->SetDisplayMode(0);
            aisEdge->SetColor(Quantity_Color(Quantity_NOC_BLACK));
            _aisContext()->Display(aisEdge, Standard_True);
        }

        void AddFood(double x, double y, double z, double radius)
        {
            gp_Pnt center(x, y, z);
            BRepPrimAPI_MakeSphere sphere(center, radius);
            _foodShape() = new AIS_Shape(sphere.Shape());
            _foodShape()->SetDisplayMode(1);
            _foodShape()->SetColor(Quantity_Color(Quantity_NOC_YELLOW));
            _aisContext()->Display(_foodShape(), Standard_True);            
        }

        void AddCrystal(double x, double y, double z, double radius)
        {
            gp_Pnt center(x, y, z);
            BRepPrimAPI_MakeSphere sphere(center, radius);

            Handle(AIS_Shape) aisShape = new AIS_Shape(sphere.Shape());
            aisShape->SetDisplayMode(1);
            aisShape->SetColor(Quantity_Color(Quantity_NOC_CYAN));

            _aisContext()->Display(aisShape, Standard_True);
            _foodShape() = aisShape;
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

            // вращение вокруг вертикали (up): поворачиваем right и fwd, ось up не трогаем
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

        bool CheckCollision(const TopoDS_Shape& s1, const TopoDS_Shape& s2)
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
        }

        void Zoom(int x1, int y1, int x2, int y2)
        {
            if (!_view().IsNull()) _view()->Zoom(x1, y1, x2, y2);
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
        
        void RemoveSegment(int index)
        {
            // 1. Проверка границ
            if (index < 0 || index >= static_cast<int>(_snakeSegments->size()))
                return;

            // 2. Получаем указатель на объект AIS_Shape
            Handle(AIS_Shape) shapeToRemove = (*_snakeSegments)[index];

            // 3. Удаляем объект из контекста отображения (это убирает его с экрана)
            if (!_aisContext().IsNull())
            {
                _aisContext()->Erase(shapeToRemove, Standard_False);
            }

            // 4. Удаляем ссылку из нашего локального вектора
            // erase сдвинет все последующие элементы влево на 1 позицию
            _snakeSegments->erase(_snakeSegments->begin() + index);
            _basePositions->erase(_basePositions->begin() + index);
            _positions->erase(_positions->begin() + index);

            // 5. ВАЖНО: В OCCT индексы в векторе должны совпадать с логикой C#
            // Так как мы используем erase, элементы после удаленного автоматически сдвинулись.
            // Нам НЕ нужно вызывать MoveSegment для обновления индексов, 
            // потому что в C# мы будем обращаться к ним по новым индексам.
            // Однако, если в C# вы храните старые индексы, логика сломается.
            // При правильной реализации в C# (сдвиг списка перед удалением) это сработает идеально.
        }

        void RemoveFood()
        {
            // Удаляем еду из контекста, если она существует
            if (!_foodShape().IsNull() && !_aisContext().IsNull())
            {
                _aisContext()->Erase(_foodShape(), Standard_False);
                _foodShape() = Handle(AIS_Shape)(); // Обнуляем хендл
            }
        }

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
                camBack = 1.0;    // чуть позади центра головы — нос виден внизу экрана
                camUp = 0.5;    // немного выше оси, видно "капот"
                lookAhead = 30.0;   // далеко вперёд — хороший обзор арены
                fov = 75.0;   // широкое поле зрения
                lerpSpeed = 1.0;    // быстрый лерп, кабина не отстаёт от поворотов
            }
            else
            {
                // Дефолт
                camBack = 15.0;
                camUp = 3.0;
                lookAhead = 10.0;
                fov = 45.0;
                lerpSpeed = 0.07;
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

            double newAtX = currentAt.X() + (desiredAtX - currentAt.X()) *lerpSpeed;
            double newAtY = currentAt.Y() + (desiredAtY - currentAt.Y()) *lerpSpeed;
            double newAtZ = currentAt.Z() + (desiredAtZ - currentAt.Z()) *lerpSpeed;

            // 5. Применение: позиция + вектор up (камера вращается с кораблём)
            cam->SetEye(gp_Pnt(newEyeX, newEyeY, newEyeZ));
            cam->SetCenter(gp_Pnt(desiredAtX, desiredAtY, desiredAtZ));
            cam->SetUp(gp_Dir(_upX, _upY, _upZ));

            _view()->Redraw();
        }

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