using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Snake3D;

// Явные алиасы против конфликта WinForms ↔ WPF
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace Snake3D
{
    public partial class SnakeView : Window
    {
        private SnakeViewport? _viewport;
        private SnakeGame? _game;
        private DispatcherTimer? _timer;
        private bool _isDragging;
        private Point _lastMousePos;


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

                double arenaSize = 25.0;

                // 1. Рисуем сетку ОДИН раз (до игры)
                // InitializeGameScene вызывает InitialFit() — камера встанет по сетке
                _viewport.InitializeGameScene(arenaSize: arenaSize, segmentRadius: 0.45, foodRadius: 0.4);

                // 2. Создаем логику игры
                _game = new SnakeGame(_viewport.Proxy, arenaSize, 0.45, 0.4);

                // 3. Подписываемся на события
                _game.ScoreChanged += (s, score) =>
                    Dispatcher.Invoke(() => ScoreText.Text = score.ToString());
                _game.GameOver += (s, ev) =>
                    Dispatcher.Invoke(() => StatusText.Text = "Игра окончена! SPACE — рестарт");

                // 4. Фон
                _viewport.Proxy.SetBackgroundColor(30, 30, 46);

                // 5. Сброс игры: рисует змею+еду, затем InitialFit()
                //    InitialFit теперь сработает по сетке+змея+еда вместе
                _game.Reset();
                _game.Start();

                // 6. Таймер
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _timer.Tick += (s, ev) => _game?.Update();
                _timer.Start();

                // 7. События мыши/клавиатуры
                PreviewKeyDown += SnakeView_PreviewKeyDown;
                PreviewKeyUp += SnakeView_PreviewKeyUp;
                PreviewMouseDown += SnakeView_PreviewMouseDown;
                PreviewMouseMove += SnakeView_PreviewMouseMove;
                PreviewMouseUp += SnakeView_PreviewMouseUp;
                PreviewMouseWheel += SnakeView_PreviewMouseWheel;
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
            _timer?.Stop();
        }
    }
}