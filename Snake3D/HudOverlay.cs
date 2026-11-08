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
using System.Windows.Ink;
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

        private readonly StackPanel _healthBar = new();
        private readonly List<System.Windows.Shapes.Rectangle> _healthCells = new();
        private readonly System.Windows.Shapes.Rectangle _hitFlash;
        private readonly System.Windows.Shapes.Rectangle _shieldFlash;

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

            _hitFlash = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x30, 0x30)),
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            _shieldFlash = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x40, 0xA0, 0xFF)),
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            _canvas.Children.Add(_hitFlash);
            _canvas.Children.Add(_shieldFlash);

            // Полоска здоровья: 5 делений, левый верхний угол с отступом.
            // Прозрачный фон панели — оверлей целиком прозрачный.
            _healthBar.Margin = new Thickness(24, 24, 0, 0);
            _healthBar.Orientation = System.Windows.Controls.Orientation.Horizontal;
            _canvas.Children.Add(_healthBar);
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
                    Width = 28,
                    Height = 28,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 0xFF, 0x4C, 0x4C)),  // полупрозрачный красный
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x3B, 0x3B)),      // ярко-красный контур
                    StrokeThickness = 2.5,
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

        // Обновить полоску здоровья: count живых делений из max.
        // Пул делений синхронизируется с max — как маркеры кристаллов.
        public void UpdateHealth(int count, int max)
        {
            while (_healthCells.Count < max)
            {
                var cell = new System.Windows.Shapes.Rectangle
                {
                    Width = 26,
                    Height = 8,
                    Margin = new Thickness(0, 0, 4, 0),
                    RadiusX = 2,
                    RadiusY = 2,
                    StrokeThickness = 1
                };
                _healthBar.Children.Add(cell);
                _healthCells.Add(cell);
            }
            while (_healthCells.Count > max)
            {
                _healthBar.Children.RemoveAt(_healthBar.Children.Count - 1);
                _healthCells.RemoveAt(_healthCells.Count - 1);
            }

            for (int i = 0; i < _healthCells.Count; i++)
            {
                bool alive = i < count;
                _healthCells[i].Fill = alive
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0xE0, 0x50))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 0x60, 0x60, 0x60));
                _healthCells[i].Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    alive ? (byte)0xC0 : (byte)0x40, 0xFF, 0xFF, 0xFF));
            }
        }

        // Вспышка: цвет заливки + анимация прозрачности от 0.55 до 0 за 350 мс
        private void Flash(System.Windows.Shapes.Rectangle rect, System.Windows.Media.Color color)
        {
            rect.Fill = new SolidColorBrush(color);
            var anim = new System.Windows.Media.Animation.DoubleAnimation(
                0.55, 0.0, TimeSpan.FromMilliseconds(350));
            rect.Opacity = 0.55;
            rect.BeginAnimation(OpacityProperty, anim);
        }

        public void ShowRedFlash() => Flash(_hitFlash, System.Windows.Media.Color.FromRgb(0xFF, 0x30, 0x30));
        public void ShowBlueFlash() => Flash(_shieldFlash, System.Windows.Media.Color.FromRgb(0x40, 0xA0, 0xFF));
    }
}
