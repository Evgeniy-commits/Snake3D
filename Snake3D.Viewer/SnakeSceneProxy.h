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
        std::vector<gp_Pnt>* _basePositions;
        std::vector<gp_Pnt>* _positions;


        double _dirX, _dirY, _dirZ;
        double _upX, _upY, _upZ;


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

            _dirX = 0.0;  _dirY = 1.0;  _dirZ = 0.0;
            _upX = 0.0;   _upY = 0.0;   _upZ = 1.0;
        }

        ~SnakeSceneProxy()
        {
            delete _snakeSegments;
            delete _walls;
            delete _basePositions;
            delete _positions;
            _basePositions = nullptr;
            _positions = nullptr;
        }

        !SnakeSceneProxy()
        {
            delete _snakeSegments;
            delete _walls;
            delete _basePositions;
            delete _positions;
            _basePositions = nullptr;
            _positions = nullptr;
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

            if (index == 0)
            {
                // Голова — серебристая капсула (космолет)
                gp_Ax2 ax2(gp_Pnt(center.X(), center.Y() - radius, center.Z()), gp_Dir(0, 1, 0));
                TopoDS_Shape cylinder = BRepPrimAPI_MakeCylinder(ax2, radius, radius * 2).Shape();
                TopoDS_Shape sphere1 = BRepPrimAPI_MakeSphere(gp_Pnt(center.X(), center.Y() + radius, center.Z()), radius).Shape();
                TopoDS_Shape sphere2 = BRepPrimAPI_MakeSphere(gp_Pnt(center.X(), center.Y() - radius, center.Z()), radius).Shape();

                //  попарно, без Add()
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
                    if (fuse2.IsDone())
                    {
                        aisShape = new AIS_Shape(fuse2.Shape());
                    }
                    else
                    {
                        aisShape = new AIS_Shape(fuse1.Shape());
                    }
                    // Серебристый цвет через RGB (0.75, 0.75, 0.75)
                    aisShape->SetColor(Quantity_Color(0.75, 0.75, 0.75, Quantity_TOC_RGB));
                }
            }
            else
            {
                // Тело — синие сферы
                BRepPrimAPI_MakeSphere sphere(center, radius);
                aisShape = new AIS_Shape(sphere.Shape());
                aisShape->SetColor(Quantity_Color(Quantity_NOC_BLUE));
            }

            aisShape->SetDisplayMode(1);
            _snakeSegments->push_back(aisShape);
            _basePositions->push_back(center);
            _positions->push_back(center);
            _aisContext()->Display(aisShape, Standard_True);
        }

        void MoveSegment(int index, double x, double y, double z)
        {
            if (index < 0 || index >= static_cast<int>(_snakeSegments->size())) return;
            gp_Pnt newPos(x, y, z);
            //gp_Pnt newCenter(x, y, z);
            //BRepPrimAPI_MakeSphere sphere(newCenter, radius);
           /* gp_Dir axis(0, 0, 1);
            TopoDS_Shape newShape = CreateCapsule(newCenter, axis, radius * 2, radius);
            (*_snakeSegments)[index]->SetShape(newShape);*/
            //(*_snakeSegments)[index]->SetShape(sphere.Shape());

            (*_positions)[index] = newPos;
            gp_Trsf trsf;
            trsf.SetTranslation(gp_Vec((*_basePositions)[index], newPos));
            (*_snakeSegments)[index]->SetLocalTransformation(trsf);

           /* Quantity_Color color = (index == 0)
                ? Quantity_Color(Quantity_NOC_RED)
                : Quantity_Color(Quantity_NOC_BLUE);
            (*_snakeSegments)[index]->SetColor(color);
            _aisContext()->Redisplay((*_snakeSegments)[index], Standard_True);*/
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

        void CreateArenaWalls(double size)
        {
            double half = size / 2.0;
            double step = size / 10.0;

            // --- Стена 1: Задняя (Z = -half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, half, -half));
                AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(half, s, -half));
            }

            // --- Стена 2: Левая (X = -half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(-half, s, half));
                AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(-half, half, s));
            }

            // --- Стена 3: Правая (X = +half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(half, s, -half), gp_Pnt(half, s, half));
                AddArenaLine(gp_Pnt(half, -half, s), gp_Pnt(half, half, s));
            }
        }

        void AddArenaLine(const gp_Pnt& p1, const gp_Pnt& p2)
        {
            BRepBuilderAPI_MakeEdge edge(p1, p2);
            Handle(AIS_Shape) aisEdge = new AIS_Shape(edge.Shape());
            aisEdge->SetDisplayMode(0);
            aisEdge->SetColor(Quantity_Color(0.4, 0.4, 0.4, Quantity_TOC_RGB));
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

            // --- Стена 1: Задняя (Z = -half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, half, -half));
                AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(half, s, -half));
            }

            // --- Стена 2: Левая (X = -half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(-half, s, -half), gp_Pnt(-half, s, half));
                AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(-half, half, s));
            }

            // --- Стена 3: Правая (X = +half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(half, s, -half), gp_Pnt(half, s, half));
                AddArenaLine(gp_Pnt(half, -half, s), gp_Pnt(half, half, s));
            }

            // --- Стена 4: Пол (Y = -half) ---
            for (double s = -half; s <= half + 0.001; s += step)
            {
                AddArenaLine(gp_Pnt(s, -half, -half), gp_Pnt(s, -half, half));
                AddArenaLine(gp_Pnt(-half, -half, s), gp_Pnt(half, -half, s));
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

        void FollowHead(int headIndex)
        {
            if (headIndex < 0 || _snakeSegments == nullptr) return;
            if ((size_t)headIndex >= _snakeSegments->size()) return;
            if (_view().IsNull()) return;

            Handle(AIS_Shape) headShape = (*_snakeSegments)[headIndex];
            if (headShape.IsNull()) return;

            // 1. Центр головы
            Bnd_Box box;
            BRepBndLib::Add(headShape->Shape(), box);
            Standard_Real minX, minY, minZ, maxX, maxY, maxZ;
            box.Get(minX, minY, minZ, maxX, maxY, maxZ);

            /*double cx = (minX + maxX) * 0.5;
            double cy = (minY + maxY) * 0.5;
            double cz = (minZ + maxZ) * 0.5;*/
            double cx = (*_positions)[headIndex].X();
            double cy = (*_positions)[headIndex].Y();
            double cz = (*_positions)[headIndex].Z();

            // 2. Камера: позади по forward, выше по up, смотрит вперёд
            double camBack = 10.0;
            double camUp = 3.0;
            double lookAhead = 4.0;

            // Eye = голова - forward*camBack + up*camUp
            double desiredEyeX = cx - _dirX * camBack + _upX * camUp;
            double desiredEyeY = cy - _dirY * camBack + _upY * camUp;
            double desiredEyeZ = cz - _dirZ * camBack + _upZ * camUp;

            // At = голова + forward*lookAhead
            double desiredAtX = cx + _dirX * lookAhead;
            double desiredAtY = cy + _dirY * lookAhead;
            double desiredAtZ = cz + _dirZ * lookAhead;

            // 3. Текущие позиции
            Handle(Graphic3d_Camera) cam = _view()->Camera();
            gp_Pnt currentEye = cam->Eye();
            gp_Pnt currentAt = cam->Center();

            // 4. Lerp — запаздывание камеры
            double speed = 0.08;

            double newEyeX = currentEye.X() + (desiredEyeX - currentEye.X()) * speed;
            double newEyeY = currentEye.Y() + (desiredEyeY - currentEye.Y()) * speed;
            double newEyeZ = currentEye.Z() + (desiredEyeZ - currentEye.Z()) * speed;

            double newAtX = currentAt.X() + (desiredAtX - currentAt.X()) * speed;
            double newAtY = currentAt.Y() + (desiredAtY - currentAt.Y()) * speed;
            double newAtZ = currentAt.Z() + (desiredAtZ - currentAt.Z()) * speed;

            // 5. Применение: позиция + вектор up (камера вращается с кораблём)
            cam->SetEye(gp_Pnt(newEyeX, newEyeY, newEyeZ));
            cam->SetCenter(gp_Pnt(newAtX, newAtY, newAtZ));
            cam->SetUp(gp_Dir(_upX, _upY, _upZ));

            _view()->Redraw();
        }


    };
}