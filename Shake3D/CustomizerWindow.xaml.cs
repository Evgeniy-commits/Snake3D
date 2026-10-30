using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Snake3DViewer;
using WF = System.Windows.Forms;
using WButton = System.Windows.Controls.Button;
using WColor = System.Windows.Media.Color;

namespace Snake3D
{
    public partial class CustomizerWindow : Window
    {
        private SnakeViewport? _viewport;
        private DispatcherTimer? _timer;
        private double _elapsed;
        private int _selectedPart;        // 0 корпус, 1 кабина, 2 двигатель, 3 хвост
        private const int TailCount = 6;
        private bool _rotating;

        private static readonly (string Name, double R, double G, double B)[] Palette =
        {
            ("Графит",  0.08, 0.09, 0.11), ("Сталь",   0.55, 0.58, 0.62),
            ("Белый",   0.92, 0.94, 0.97), ("Красный", 0.85, 0.12, 0.10),
            ("Оранж",   1.00, 0.45, 0.10), ("Лайм",    0.80, 1.00, 0.15),
            ("Изумруд", 0.10, 0.75, 0.45), ("Бирюза",  0.10, 0.85, 0.70),
            ("Голубой", 0.40, 0.65, 1.00), ("Фиолет",  0.55, 0.15, 0.95),
            ("Фуксин",  0.95, 0.30, 1.00), ("Янтарь",  1.00, 0.85, 0.20),
        };

        public CustomizerWindow()
        {
            DarkTitle.Apply(this);
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (_viewport != null) return;
                _viewport = new SnakeViewport();
                _viewport.MouseDown += Viewport_MouseDown;
                _viewport.MouseMove += Viewport_MouseMove;
                _viewport.MouseUp += Viewport_MouseUp;
                _viewport.MouseWheel += Viewport_Wheel;
                Host.Child = _viewport;
                Dispatcher.BeginInvoke(new Action(InitScene), DispatcherPriority.ApplicationIdle);
            };
            Closed += (s, e) =>
            {
                _timer?.Stop();
                Host.Child = null;
                _viewport?.Dispose();
                _viewport = null;
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };
        }

        private void InitScene()
        {
            var p = _viewport!.Proxy;
            if (p == null) return;

            p.SetBackgroundColor(10, 10, 16);

            string bg = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"image\hangari.jpg");
            if (System.IO.File.Exists(bg))
                p.SetBackgroundImage(bg);
            else
                p.CreateStarSky(600, 240.0);

            ShipList.Items.Clear();
            foreach (var v in GameConfig.Ship.Variants)
                ShipList.Items.Add(v.Name);
            ShipList.SelectedIndex = GameConfig.Ship.Selected;
            ShipList.SelectionChanged += ShipList_SelectionChanged;

            BuildShip(p);
            BuildTail(p);

            //// Хвост: цепочка звеньев за кораблём
            //p.LoadStepModel(@"models\Tail.stp", 1, GameConfig.SegmentRadius * 1.5);
            //var t = GameConfig.Ship.TailColor;
            //p.SetTailColor(t[0], t[1], t[2]);
            //for (int i = 0; i <= TailCount; i++)
            //    p.AddSegment(0, -i * GameConfig.StepLength, 0, GameConfig.SegmentRadius, i);

            PartsList.Items.Clear();
            PartsList.Items.Add("Корпус");
            PartsList.Items.Add("Кабина");
            PartsList.Items.Add("Двигатель");
            PartsList.Items.Add("Хвост");
            PartsList.SelectedIndex = 0;

            BuildPalette();
            p.FitScene();
            p.FitShipClose(4.0, 0.75);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (s, e) => Animate();
            _timer.Start();
        }

        // Сборка корабля из текущего варианта: загрузка, подгонка, окраска
        private void BuildShip(Snake3DViewer.SnakeSceneProxy p)
        {
            var ship = GameConfig.Ship.Cur;

            for (int i = 0; i < ship.Paths.Length; i++)
            {
                int id = GameConfig.Ship.HullId + i;
                p.LoadShipPart(ship.Paths[i], id, ship.Scale);
            }
            // Центрирование — один раз, после загрузки всех частей:
            // метод считает bbox по уже существующим моделям
            p.CenterShipAssembly(GameConfig.Ship.HullId, ship.Paths.Length);

            for (int i = 0; i < ship.Paths.Length; i++)
            {
                int id = GameConfig.Ship.HullId + i;
                var f = ship.Fit[i];
                p.SetPartPlacement(id, f[0], f[1], f[2], f[3], f[4], f[5]);
                p.AddShipPart(id);
                var c = ship.Color[i];
                p.SetPartColor(id, c[0], c[1], c[2], ship.Material[i]);
            }
        }

        // Постройка хвоста — отдельный метод, чтобы вызывать и при старте,
        // и после смены варианта (ClearScene его сносит)
        private void BuildTail(Snake3DViewer.SnakeSceneProxy p)
        {
            p.LoadStepModel(@"models\Tail.stp", 1, GameConfig.SegmentRadius * 1.5);
            var t = GameConfig.Ship.TailColor;
            p.SetTailColor(t[0], t[1], t[2]);
            for (int i = 0; i <= TailCount; i++)
                p.AddSegment(0, -i * GameConfig.StepLength, 0, GameConfig.SegmentRadius, i);
        }

        private void ShipList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShipList.SelectedIndex < 0 || _viewport?.Proxy == null) return;
            if (GameConfig.Ship.Selected == ShipList.SelectedIndex) return;

            GameConfig.Ship.Selected = ShipList.SelectedIndex;
            RebuildScene(_viewport.Proxy);
        }

        // Полная пересборка сцены: как Reset в игре, но с фоном кастомайзера
        private void RebuildScene(Snake3DViewer.SnakeSceneProxy p)
        {
            p.ClearScene();

            // Звёзды — часть сцены, ClearScene их сносит. Фон-картинка держится
            // на уровне вида (V3d_View), она переживёт чистку — но проверь глазами
            p.CreateStarSky(600, 240.0);

            BuildShip(p);
            BuildTail(p);

            p.FitScene();
            p.FitShipClose(4.0, 0.75);
        }

        // Корабль стоит на месте, но живёт: двигатель пульсирует, звенья хвоста
        // проворачиваются через один — как в полёте
        private void Animate()
        {
            var p = _viewport?.Proxy;
            if (p == null) return;
            _elapsed += 0.016;
            p.UpdateSpin(0.016);
            p.PlaceShip(0, 0, 0, 0, 1, 0, 0, 0, 1, _elapsed);
            for (int i = 1; i <= TailCount; i++)
                p.MoveSegment(i, 0, -i * GameConfig.StepLength, 0);
            p.Redraw();
        }

        private void BuildPalette()
        {
            PaletteGrid.Children.Clear();
            for (int i = 0; i < Palette.Length; i++)
            {
                int idx = i;
                var c = Palette[i];
                var btn = new WButton
                {
                    Background = new SolidColorBrush(WColor.FromRgb(
                        (byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255))),
                    Height = 36,
                    Margin = new Thickness(3),
                    ToolTip = c.Name
                };
                btn.Click += (s, e) => ApplyColor(idx);
                PaletteGrid.Children.Add(btn);
            }
        }

        private void ApplyColor(int idx)
        {
            if (_viewport?.Proxy == null) return;
            var p = _viewport.Proxy;
            var c = Palette[idx];

            if (_selectedPart == 3)   // хвост — красим всю цепочку и запоминаем
            {
                GameConfig.Ship.TailColor[0] = c.R;
                GameConfig.Ship.TailColor[1] = c.G;
                GameConfig.Ship.TailColor[2] = c.B;
                p.SetTailColor(c.R, c.G, c.B);
                for (int i = 1; i <= TailCount; i++)
                    p.SetSegmentColor(i, c.R, c.G, c.B);
            }
            else
            {
                int part = _selectedPart;
                int id = GameConfig.Ship.HullId + part;
                var ship = GameConfig.Ship.Cur;
                p.SetPartColor(id, c.R, c.G, c.B, ship.Material[part]);
                ship.Color[part][0] = c.R;
                ship.Color[part][1] = c.G;
                ship.Color[part][2] = c.B;
            }
        }

        private void PartsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _selectedPart = PartsList.SelectedIndex < 0 ? 0 : PartsList.SelectedIndex;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // Орбитальная камера: правая кнопка — вращение, колесо — зум
        private void Viewport_MouseDown(object sender, WF.MouseEventArgs e)
        {
            _viewport!.Focus();
            if (e.Button != WF.MouseButtons.Right) return;
            _rotating = true;
            _viewport.Proxy!.StartRotate(e.X, _viewport.Height - e.Y);
        }

        private void Viewport_MouseMove(object sender, WF.MouseEventArgs e)
        {
            if (!_rotating) return;
            _viewport!.Proxy!.Rotate(e.X, _viewport.Height - e.Y);
        }

        private void Viewport_MouseUp(object sender, WF.MouseEventArgs e) => _rotating = false;

        private void Viewport_Wheel(object sender, WF.MouseEventArgs e)
            => _viewport?.Proxy?.ZoomBy(Math.Pow(1.25, e.Delta / 120.0));
    }
}