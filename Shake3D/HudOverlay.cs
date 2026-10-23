//Назначение: показать прицел и маркеры кристаллов поверх 3D-сцены.

//Почему это отдельное окно, а не WPF-элементы на главном окне:
//из - за того же airspace. Нативный HWND OCCT всегда рисуется поверх любых
//WPF-элементов главного окна — прицел и точки в нём бы просто не были видны.
//Решение — второе окно с AllowsTransparency = true и Topmost = true,
//целиком прозрачное, кроме нарисованных элементов.
//Оно живёт своей жизнью и синхронизируется вручную (UpdateHudPlacement).

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Snake3D
{
    public class HudOverlay : Window
    {
        private readonly Canvas _canvas = new();
        private readonly Path? _crosshair;
        private readonly Ellipse? _crystalDot;
        private readonly List<Ellipse> _crystalDots = new();

        public HudOverlay()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Focusable = false;
            ShowActivated = false;

            Content = _canvas;

        //Элементы:
            //_crosshair(Path) — прицел: четыре штриха, нарисованные геометрией
            //M 0,-18 V - 6 M 0,6 V18 M -18,0 H - 6 M 6,0 H18.
            //Позиция меняется через TranslateTransform(PlaceCrosshair),
            //показ / скрытие — ShowCrosshair(виден только в кабине).

            //_crystalDot — одиночный маркер(наследие старой логики «один кристалл»,
            //сейчас не используется — кандидат на удаление вместе с PlaceCrystal).

            //_crystalDots(List<Ellipse>) — пул маркеров для всех кристаллов.

            // Прицел по центру
            _crosshair = new Path
            {
                Stroke = System.Windows.Media.Brushes.LimeGreen,
                StrokeThickness = 2,
                Opacity = 0.8,
                Visibility = Visibility.Collapsed
            };
            _crosshair.Data = Geometry.Parse("M 0,-18 V-6 M 0,6 V18 M -18,0 H-6 M 6,0 H18");
            _crosshair.RenderTransform = new TranslateTransform(0, 0);
            _canvas.Children.Add(_crosshair);

            // Маркер кристалла
            //_crystalDot = new Ellipse
            //{
            //    Width = 16,
            //    Height = 16,
            //    Stroke = System.Windows.Media.Brushes.Orange,
            //    StrokeThickness = 2,
            //    Visibility = Visibility.Collapsed
            //};
            //_canvas.Children.Add(_crystalDot);
        }

        public void ShowCrosshair(bool show)
        {
            _crosshair!.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        public void PlaceCrosshair(double cx, double cy)
        {
            var t = (TranslateTransform)_crosshair!.RenderTransform;
            t.X = cx;
            t.Y = cy;
        }

        // x == null — маркер скрыт (кристалл вне экрана/сзади)
        //public void PlaceCrystal(double? x, double y)
        //{
        //    if (x == null)
        //    {
        //        _crystalDot!.Visibility = Visibility.Collapsed;
        //        return;
        //    }
        //    _crystalDot!.Visibility = Visibility.Visible;
        //    Canvas.SetLeft(_crystalDot, x.Value - _crystalDot.Width / 2);
        //    Canvas.SetTop(_crystalDot, y - _crystalDot.Height / 2);
        //}

        //UpdateCrystalMarkers(xs, ys) — синхронизирует пул точек со списком
        //проекций: недостающие создаёт, лишние убирает, xs[i] == null —
        //кристалл вне экрана или за камерой, маркер скрыт.Позиция через
        //Canvas.SetLeft/SetTop с центровкой(минус половина размера точки).
        public void UpdateCrystalMarkers(IReadOnlyList<double?> xs, IReadOnlyList<double> ys)
        {
            while (_crystalDots.Count < xs.Count)
            {
                var dot = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Stroke = System.Windows.Media.Brushes.Orange,
                    StrokeThickness = 2,
                    Visibility = Visibility.Collapsed
                };
                _canvas.Children.Add(dot);
                _crystalDots.Add(dot);
            }

            while (_crystalDots.Count > xs.Count)
            {
                _canvas.Children.Remove(_crystalDots[_crystalDots.Count - 1]);
                _crystalDots.RemoveAt(_crystalDots.Count - 1);
            }

            for (int i = 0; i < xs.Count; i++)
            {
                if (xs[i] == null)
                {
                    _crystalDots[i].Visibility = Visibility.Collapsed;
                    continue;
                }
                _crystalDots[i].Visibility = Visibility.Visible;
                Canvas.SetLeft(_crystalDots[i], xs[i].Value - _crystalDots[i].Width / 2);
                Canvas.SetTop(_crystalDots[i], ys[i] - _crystalDots[i].Height / 2);
            }
        }
    }
}
