using Snake3D;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Point = System.Windows.Point;
// Явные алиасы против конфликта WinForms ↔ WPF
using UserControl = System.Windows.Controls.UserControl;

namespace Snake3D
{
    public partial class SnakeView : Window
    {
        private SnakeViewport? _viewport;
        private SnakeGame? _game;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _renderTimer;
        private DateTime _lastRender = DateTime.MinValue;
        private DateTime _lastFrameTime;
        private readonly System.Diagnostics.Stopwatch _uptime = System.Diagnostics.Stopwatch.StartNew();
        private HudOverlay? _hud;
        private DateTime _lastHudUpdate = DateTime.MinValue;
        private bool _isDragging;
        private Point _lastMousePos;
        private HudOverlay? _hub;


        public SnakeView()
        {
            //MessageBox.Show("A. До InitializeComponent");
            InitializeComponent();
            //MessageBox.Show("B. После InitializeComponent");
            Loaded += SnakeView_Loaded;
            Unloaded += SnakeView_Unloaded;
            //MessageBox.Show("C. После подписки на события");
        }

        private void SnakeView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewport = new SnakeViewport();
                ViewportHost.Child = _viewport;

                double arenaSize = 50.0;

                // 1. Рисуем сетку ОДИН раз (до игры)
                // InitializeGameScene вызывает InitialFit() — камера встанет по сетке
                _viewport.InitializeGameScene(arenaSize: arenaSize, segmentRadius: 0.45, foodRadius: 0.4);

                // 2. Создаем логику игры
                _game = new SnakeGame(_viewport.Proxy!, arenaSize, 0.45, 0.4);

                // 3. Подписываемся на события
                _game.ScoreChanged += (s, score) =>
                    Dispatcher.Invoke(() => ScoreText.Text = score.ToString());
                _game.GameOver += (s, ev) =>
                    Dispatcher.Invoke(() => StatusText.Text = "Игра окончена! SPACE — рестарт");

                // 4. Фон
                _viewport.Proxy!.SetBackgroundColor(30, 30, 46);

                // 5. Сброс игры: рисует змею+еду, затем InitialFit()
                //    InitialFit теперь сработает по сетке+змея+еда вместе
                _game.Reset();
                _game.Start();

                // 6. Таймер
                // Логика 10 Гц
                //_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                //_timer.Tick += (s, ev) => _game?.Update();
                //_timer.Start();

                // Рендер 30 Гц — интерполяция
                //_renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                //_renderTimer.Tick += (s, ev) => OnRenderFrame();
                //_renderTimer.Start();
                //CompositionTarget.Rendering += OnRenderFrameRendering;

                // Единый кадровый цикл
                _lastFrameTime = DateTime.UtcNow;
                CompositionTarget.Rendering += OnFrame;

                // HUD-оверлей
                _hud = new HudOverlay();
                _hud.Owner = this;
                _hud.Show();
                UpdateHudPlacement();
                LocationChanged += (s, e) => UpdateHudPlacement();
                SizeChanged += (s, e) => UpdateHudPlacement();

                // 7. События мыши/клавиатуры
                PreviewKeyDown += SnakeView_PreviewKeyDown;
                PreviewKeyUp += SnakeView_PreviewKeyUp;
                PreviewMouseDown += SnakeView_PreviewMouseDown;
                PreviewMouseMove += SnakeView_PreviewMouseMove;
                PreviewMouseUp += SnakeView_PreviewMouseUp;
                PreviewMouseWheel += SnakeView_PreviewMouseWheel;
                Closed += (s, ev) => { _hud?.Close(); _hud = null; };
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Ошибка в Loaded");
            }
        }
        private void SnakeView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_game == null) return;

            switch (e.Key)
            {
                case Key.Space:
                    if (_game.IsGameOver || !_game.IsRunning)
                    {
                        _game.Start();
                        StatusText.Text = "Идёт игра!";
                    }
                    break;
                case Key.A: case Key.Left: _game.StartTurn(1); break;
                case Key.D: case Key.Right: _game.StartTurn(2); break;
                case Key.W: case Key.Up: _game.StartTurn(3); break;
                case Key.S: case Key.Down: _game.StartTurn(4); break;
                case Key.E:
                    if (e.IsRepeat) break;              
                    _viewport?.Proxy?.ToggleViewMode();
                    break;
                case Key.P:
                    if (e.IsRepeat) break;
                    _game.TogglePause();
                    StatusText.Text = _game.IsPaused ? "Пауза — P чтобы продолжить" : "Идёт игра!";
                    break;
                case Key.R:
                    if (e.IsRepeat) break;
                    _game.TapTurnEnabled = !_game.TapTurnEnabled;
                    StatusText.Text = _game.TapTurnEnabled ? "Тап-поворот: ВКЛ" : "Тап-поворот: ВЫКЛ";
                    break;
            }
        }

        private void SnakeView_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (_game == null) return;

            switch (e.Key)
            {
                case Key.A: case Key.Left: _game.ReleaseTurn(1); break;
                case Key.D: case Key.Right: _game.ReleaseTurn(2); break;
                case Key.W: case Key.Up: _game.ReleaseTurn(3); break;
                case Key.S: case Key.Down: _game.ReleaseTurn(4); break;
            }
        }
        //private void OnRenderFrameRendering(object? sender, EventArgs e)
        //{
        //    // не чаще ~30 кадров в секунду, чтобы не перегружать Redraw
        //    if ((DateTime.UtcNow - _lastRender).TotalMilliseconds < 30) return;
        //    _lastRender = DateTime.UtcNow;

        //    OnRenderFrame();
        //}

        private void OnFrame(object? sender, EventArgs e)
        {
            if (_game == null) return;

            var now = DateTime.UtcNow;
            double dt = (now - _lastFrameTime).TotalSeconds;
            _lastFrameTime = now;

            // ограничитель: после сна системы не даём гигантский скачок
            if (dt > 0.5) dt = 0.5;

            _game.Frame(dt);

            // HUD — как раньше, раз в ~30 мс
            if ((DateTime.UtcNow - _lastHudUpdate).TotalMilliseconds >= 30)
            {
                _lastHudUpdate = DateTime.UtcNow;
                UpdateHudVisuals(); // содержимое бывшего OnRenderFrame после _game.Render()
            }
        }

        //private void OnRenderFrame()
        //{
        //    if (_game == null || _hud == null) return;

        //    _game.Render();

        //    bool cockpit = _viewport?.Proxy != null && _viewport.Proxy.IsCockpitView();
        //    _hud.ShowCrosshair(cockpit);

        //    if (!cockpit)
        //    {
        //        _hud.PlaceCrystal(null, 0);
        //        return;
        //    }

        //    // маркер кристалла
        //    var c = _game.CrystalPos;
        //    int sx = 0, sy = 0;
        //    bool inFront = false;
        //    bool ok = _viewport!.Proxy!.ProjectToScreen(c.X, c.Y, c.Z, ref sx, ref sy, ref inFront);

        //    double w = ViewportHost.ActualWidth;
        //    double h = ViewportHost.ActualHeight;

        //    if (ok && inFront && sx >= 0 && sx <= w && sy >= 0 && sy <= h)
        //    {
        //        // V3d даёт координаты от НИЖНЕГО левого угла — переворачиваем по Y
        //        _hud.PlaceCrystal(sx, h - sy);
        //    }
        //    else
        //    {
        //        _hud.PlaceCrystal(null, 0); // вне экрана — скрываем
        //    }
        //}

        private void UpdateHudVisuals()
        {
            if (_hud == null) return;

            bool cockpit = _viewport?.Proxy != null && _viewport.Proxy.IsCockpitView();
            _hud.ShowCrosshair(cockpit);

            if (!cockpit)
            {
                _hud.PlaceCrystal(null, 0);
                return;
            }

            var c = _game!.CrystalPos;
            int sx = 0, sy = 0;
            bool inFront = false;
            bool ok = _viewport!.Proxy!.ProjectToScreen(c.X, c.Y, c.Z, ref sx, ref sy, ref inFront);

            double w = ViewportHost.ActualWidth;
            double h = ViewportHost.ActualHeight;

            if (ok && inFront && sx >= 0 && sx <= w && sy >= 0 && sy <= h)
                _hud.PlaceCrystal(sx, h - sy);
            else
                _hud.PlaceCrystal(null, 0);
        }

        private void UpdateHudPlacement()
        {
            if (_hud == null) return;
            var p = ViewportHost.PointToScreen(new Point(0, 0));
            _hud.Left = p.X;
            _hud.Top = p.Y;
            _hud.Width = ViewportHost.ActualWidth;
            _hud.Height = ViewportHost.ActualHeight;
            _hud.PlaceCrosshair(ViewportHost.ActualWidth / 2, ViewportHost.ActualHeight / 2);
        }

        private void SnakeView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePos = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Left && _viewport?.Proxy != null)
                _viewport.Proxy.StartRotate((int)_lastMousePos.X, (int)_lastMousePos.Y);
        }

        private void SnakeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _viewport?.Proxy == null) return;
            var pos = e.GetPosition(this);
            int x = (int)pos.X;
            int y = (int)pos.Y;

            if (e.LeftButton == MouseButtonState.Pressed)
                _viewport.Proxy.Rotate(x, y);
            else if (e.MiddleButton == MouseButtonState.Pressed)
                _viewport.Proxy.Pan(x - (int)_lastMousePos.X, y - (int)_lastMousePos.Y);

            _lastMousePos = pos;
        }

        private void SnakeView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        private void SnakeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewport?.Proxy == null) return;
            int delta = e.Delta > 0 ? 10 : -10;
            _viewport.Proxy.Zoom(100, 100, 100 + delta, 100);
        }

        private void SnakeView_Unloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            _hud?.Close();
            _hud = null;
        }
    }
}