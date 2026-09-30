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
#include <vcclr.h>
#include <msclr/marshal.h>
#include <Graphic3d_Camera.hxx>

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
        }

        ~SnakeSceneProxy()
        {
            delete _snakeSegments;
            delete _walls;
        }

        !SnakeSceneProxy()
        {
            delete _snakeSegments;
            delete _walls;
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
            _snakeSegments->clear();
            _walls->clear();
            _foodShape() = Handle(AIS_Shape)();
            if (!_aisContext().IsNull())
                _aisContext()->UpdateCurrentViewer();
        }

        void AddSegment(double x, double y, double z, double radius, int index)
        {
            gp_Pnt center(x, y, z);
            BRepPrimAPI_MakeSphere sphere(center, radius);
            /*gp_Dir axis(0, 0, 1);
            TopoDS_Shape capsule = CreateCapsule(center, axis, radius * 2, radius);
            Handle(AIS_Shape) aisShape = new AIS_Shape(capsule);*/
            Handle(AIS_Shape) aisShape = new AIS_Shape(sphere.Shape());
            aisShape->SetDisplayMode(1);
            Quantity_Color color = (index == 0)
                ? Quantity_Color(Quantity_NOC_RED)
                : Quantity_Color(Quantity_NOC_BLUE);
            aisShape->SetColor(color);
            _snakeSegments->push_back(aisShape);
            _aisContext()->Display(aisShape, Standard_True);
        }

        void MoveSegment(int index, double x, double y, double z, double radius)
        {
            if (index < 0 || index >= static_cast<int>(_snakeSegments->size())) return;
            gp_Pnt newCenter(x, y, z);
            BRepPrimAPI_MakeSphere sphere(newCenter, radius);
           /* gp_Dir axis(0, 0, 1);
            TopoDS_Shape newShape = CreateCapsule(newCenter, axis, radius * 2, radius);
            (*_snakeSegments)[index]->SetShape(newShape);*/

            (*_snakeSegments)[index]->SetShape(sphere.Shape());

            Quantity_Color color = (index == 0)
                ? Quantity_Color(Quantity_NOC_RED)
                : Quantity_Color(Quantity_NOC_BLUE);
            (*_snakeSegments)[index]->SetColor(color);
            _aisContext()->Redisplay((*_snakeSegments)[index], Standard_True);
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

            // Левая
            TopoDS_Shape w1 = BRepPrimAPI_MakeBox(
                gp_Pnt(-half + thickness / 2.0, 0, 0),
                thickness, arenaSize, arenaSize
            ).Shape();
            AddWall(w1);

            // Правая
            TopoDS_Shape w2 = BRepPrimAPI_MakeBox(
                gp_Pnt(half - thickness / 2.0, 0, 0),
                thickness, arenaSize, arenaSize
            ).Shape();
            AddWall(w2);

            // Задняя
            TopoDS_Shape w3 = BRepPrimAPI_MakeBox(
                gp_Pnt(0, -half + thickness / 2.0, 0),
                arenaSize, thickness, arenaSize
            ).Shape();
            AddWall(w3);

            // Передняя
            TopoDS_Shape w4 = BRepPrimAPI_MakeBox(
                gp_Pnt(0, half - thickness / 2.0, 0),
                arenaSize, thickness, arenaSize
            ).Shape();
            AddWall(w4);
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

            // Линии вдоль оси X (параллельны оси X, идут по Y)
            for (double y = -half; y <= half + 0.001; y += step)
            {
                gp_Pnt p1(-half, y, 0);
                gp_Pnt p2(half, y, 0);
                AddGridLine(p1, p2, Quantity_NOC_WHITE); // Цвет не используется, берем любой
            }

            // Линии вдоль оси Y (параллельны оси Y, идут по X)
            for (double x = -half; x <= half + 0.001; x += step)
            {
                gp_Pnt p1(x, -half, 0);
                gp_Pnt p2(x, half, 0);
                AddGridLine(p1, p2, Quantity_NOC_WHITE);
            }

            // Оси координат (Красный - X, Зеленый - Y, Синий - Z)
            gp_Pnt origin(0, 0, 0);
            gp_Pnt xAxis(arenaSize * 0.5, 0, 0);
            gp_Pnt yAxis(0, arenaSize * 0.5, 0);
            gp_Pnt zAxis(0, 0, arenaSize * 0.5);

            // Helper для осей с конкретным цветом
            {
                BRepBuilderAPI_MakeEdge edgeX(origin, xAxis);
                Handle(AIS_Shape) aisX = new AIS_Shape(edgeX.Shape());
                aisX->SetDisplayMode(0); aisX->SetColor(Quantity_Color(Quantity_NOC_RED));
                _aisContext()->Display(aisX, Standard_True);
            }
            {
                BRepBuilderAPI_MakeEdge edgeY(origin, yAxis);
                Handle(AIS_Shape) aisY = new AIS_Shape(edgeY.Shape());
                aisY->SetDisplayMode(0); aisY->SetColor(Quantity_Color(Quantity_NOC_GREEN));
                _aisContext()->Display(aisY, Standard_True);
            }
            {
                BRepBuilderAPI_MakeEdge edgeZ(origin, zAxis);
                Handle(AIS_Shape) aisZ = new AIS_Shape(edgeZ.Shape());
                aisZ->SetDisplayMode(0); aisZ->SetColor(Quantity_Color(Quantity_NOC_BLUE));
                _aisContext()->Display(aisZ, Standard_True);
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
            if (!_aisContext().IsNull())
                _aisContext()->UpdateCurrentViewer();
        }

        void FollowHead(int headIndex)
        {
            if (headIndex < 0 || _snakeSegments == nullptr) return;
            if ((size_t)headIndex >= _snakeSegments->size()) return;
            if (_view().IsNull()) return;

            Handle(AIS_Shape) headShape = (*_snakeSegments)[headIndex];
            if (headShape.IsNull()) return;

            // 1. Центр головы через Bnd_Box
            Bnd_Box box;
            BRepBndLib::Add(headShape->Shape(), box);

            Standard_Real minX, minY, minZ, maxX, maxY, maxZ;
            box.Get(minX, minY, minZ, maxX, maxY, maxZ);

            double cx = (minX + maxX) * 0.5;
            double cy = (minY + maxY) * 0.5;
            double cz = (minZ + maxZ) * 0.5;

            // 2. Направление камеры (назад и вверх): (0, -1, 1)
            double dirX = 0.0;
            double dirY = -1.0;
            double dirZ = 1.0;
            double len = sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
            if (len > 0.0) { dirX /= len; dirY /= len; dirZ /= len; }

            double offsetDist = 14.0;

            // 3. Желаемые позиции камеры
            double desiredEyeX = cx - dirX * offsetDist;
            double desiredEyeY = cy - dirY * offsetDist;
            double desiredEyeZ = cz - dirZ * offsetDist;

            double desiredAtX = cx;
            double desiredAtY = cy;
            double desiredAtZ = cz;

            // 4. Текущие позиции камеры через Graphic3d_Camera
            Handle(Graphic3d_Camera) cam = _view()->Camera();

            gp_Pnt currentEye = cam->Eye();
            gp_Pnt currentAt = cam->Center(); // В Graphic3d_Camera метод называется Center, а не At

            double curEyeX = currentEye.X();
            double curEyeY = currentEye.Y();
            double curEyeZ = currentEye.Z();

            double curAtX = currentAt.X();
            double curAtY = currentAt.Y();
            double curAtZ = currentAt.Z();

            // 5. Lerp (интерполяция)
            double speed = 0.18;

            double newEyeX = curEyeX + (desiredEyeX - curEyeX) * speed;
            double newEyeY = curEyeY + (desiredEyeY - curEyeY) * speed;
            double newEyeZ = curEyeZ + (desiredEyeZ - curEyeZ) * speed;

            double newAtX = curAtX + (desiredAtX - curAtX) * speed;
            double newAtY = curAtY + (desiredAtY - curAtY) * speed;
            double newAtZ = curAtZ + (desiredAtZ - curAtZ) * speed;

            // 6. Применение новых позиций через Graphic3d_Camera
            cam->SetEye(gp_Pnt(newEyeX, newEyeY, newEyeZ));
            cam->SetCenter(gp_Pnt(newAtX, newAtY, newAtZ)); // SetCenter вместо SetAt

            _view()->Redraw();
        }

    };
}