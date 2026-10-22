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
            _crystalDot = new Ellipse
            {
                Width = 16,
                Height = 16,
                Stroke = System.Windows.Media.Brushes.Orange,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed
            };
            _canvas.Children.Add(_crystalDot);
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
        public void PlaceCrystal(double? x, double y)
        {
            if (x == null)
            {
                _crystalDot!.Visibility = Visibility.Collapsed;
                return;
            }
            _crystalDot!.Visibility = Visibility.Visible;
            Canvas.SetLeft(_crystalDot, x.Value - _crystalDot.Width / 2);
            Canvas.SetTop(_crystalDot, y - _crystalDot.Height / 2);
        }
    }
}
