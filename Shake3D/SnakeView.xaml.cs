//Назначение: собрать всё вместе: создать вьюпорт и игру, гонять кадровый цикл,
//раздавать ввод (клавиатура — WPF, мышь — WinForms), обновлять HUD.

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
using WF = System.Windows.Forms;
using UserControl = System.Windows.Controls.UserControl;

namespace Snake3D
{
    public partial class SnakeView : Window
    {
        //_viewport, _game — объекты, созданные в Loaded.Nullable(?), потому что до
        //Loaded их нет.
        //_lastFrameTime — время прошлого кадра для расчёта dt.
        //_rotating, _winLastPos — состояние перетаскивания камеры мышью.
        //_timer, _renderTimer, _lastRender, _uptime — остатки старой схемы
        //(отдельный таймер рендера): после перехода на единый
        //CompositionTarget.Rendering не используются и подлежат удалению.

        private SnakeViewport? _viewport;
        private SnakeGame? _game;
        //private DispatcherTimer? _timer;
        //private DispatcherTimer? _renderTimer;
        //private DateTime _lastRender = DateTime.MinValue;
        private DateTime _lastFrameTime;
        private readonly System.Diagnostics.Stopwatch _uptime = System.Diagnostics.Stopwatch.StartNew();
        private HudOverlay? _hud;
        private DateTime _lastHudUpdate = DateTime.MinValue;
        private bool _menuHandled = false;

        //Мышь
        private bool _rotating;
        private Point _winLastPos;

        public SnakeView()
        {
            //MessageBox.Show("A. До InitializeComponent");
            InitializeComponent();
            //MessageBox.Show("B. После InitializeComponent");
            Loaded += SnakeView_Loaded;
            Unloaded += SnakeView_Unloaded;
            //MessageBox.Show("C. После подписки на события");
        }

        //Порядок инициализации важен и всегда одинаков:
        //Создать SnakeViewport и вложить в WindowsFormsHost — только после этого у
        //контрола появится HWND и OCCT сможет инициализироваться(OnHandleCreated).

        //InitializeGameScene — нарисовать сетку, подогнать камеру.

        //Создать SnakeGame — конструктор загрузит
        //STEP-модели(голова, хвост, кристалл, мина) один раз.

        //Подписаться на ScoreChanged/GameOver — события из логики приходят из
        //игрового потока, поэтому внутри подписок стоит
        //Dispatcher.Invoke — принудительный переход в UI-поток для обращения
        //к WPF-элементам (ScoreText, StatusText).

        //SetBackgroundColor — фон сцены.

        //Reset() + Start() — построить сцену и запустить игру.

        //Подписать мышь на WinForms-события контрола (airspace!), клавиатуру — на WPF.

        //CompositionTarget.Rendering += OnFrame — единый кадровый цикл:
        //WPF вызывает обработчик на каждом кадре композитора
        //(синхронно с частотой монитора, ~60 Гц).

        //Создать HudOverlay и следить за перемещением/размером окна,
        //чтобы оверлей ехал вместе с вьюпортом.

        private void SnakeView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_menuHandled) return;      // Loaded может сработать повторно
                _menuHandled = true;

                _viewport = new SnakeViewport();
                ViewportHost.Child = _viewport;

                // Меню — НЕ в Loaded: окно ещё не показано, ShowDialog падает
                // с InvalidOperationException (EnsureHandle после закрытия окна).
                // Откладываем до момента, когда окно реально на экране:
                // BeginInvoke выполнит ShowMenu после завершения загрузки.
                Dispatcher.BeginInvoke(new Action(ShowMenu),
                                       System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Ошибка в Loaded");
            }
        }

        // Вся инициализация, зависящая от опций меню. Вызывается через BeginInvoke:
        // к этому моменту окно показано, ShowDialog легален.
        private void ShowMenu()
        {
            try
            {
                if (!IsLoaded) return;   // окно закрыли раньше, чем открылось меню

                var menu = new MenuWindow { Owner = this };
                if (menu.ShowDialog() != true) { Close(); return; }   // крестик/«Выход»
                var opt = menu.Options;

                // 1. Сетка + единственная подгонка камеры (небо — внутри, после FitScene)
                _viewport!.InitializeGameScene(opt.ArenaSize, opt.GridStep);

                // 2. Логика игры: модели STEP грузятся один раз в конструкторе
                _game = new SnakeGame(_viewport.Proxy!, opt.ArenaSize, opt.GridStep,
                                      GameConfig.SegmentRadius, GameConfig.FoodRadius);

                // 3. События логики приходят из кадрового цикла — в UI-поток через Dispatcher
                _game.ScoreChanged += (s, score) =>
                    Dispatcher.Invoke(() => ScoreText.Text = score.ToString());
                _game.GameOver += (s, ev) =>
                    Dispatcher.Invoke(() => StatusText.Text = "Игра окончена! SPACE — рестарт");

                // 4. Фон почти чёрный, с холодным оттенком — звёзды проступают
                _viewport.Proxy!.SetBackgroundColor(5, 5, 10);

                // 5. Опции ДО Reset: сцена строится сразу с учётом выбора (мины, кристаллы, стены)
                _game.ApplyOptions(opt);
                _game.Reset();
                _viewport.Proxy!.FitScene();
                _viewport.Proxy!.SetViewMode(opt.CockpitView ? 1 : 0);
                _game.Start();

                // 6. Мышь — на WinForms-события контрола (airspace: WPF-события
                //    над нативным HWND не срабатывают). Клавиатура — на WPF.
                _viewport.MouseDown += Viewport_WinMouseDown;
                _viewport.MouseMove += Viewport_WinMouseMove;
                _viewport.MouseUp += Viewport_WinMouseUp;
                _viewport.MouseWheel += Viewport_WinMouseWheel;

                PreviewKeyDown += SnakeView_PreviewKeyDown;
                PreviewKeyUp += SnakeView_PreviewKeyUp;

                // 7. Единый кадровый цикл
                _lastFrameTime = DateTime.UtcNow;
                CompositionTarget.Rendering += OnFrame;

                // 8. HUD-оверлей
                _hud = new HudOverlay();
                _hud.Owner = this;
                _hud.Show();
                UpdateHudPlacement();
                LocationChanged += (s, e) => UpdateHudPlacement();
                SizeChanged += (s, e) => UpdateHudPlacement();
                Closed += (s, ev) => { _hud?.Close(); _hud = null; };
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Ошибка в меню");
            }
        }
        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;   // ShowMenu увидит false и сам закроет главное окно
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
                case Key.T:
                    if (e.IsRepeat) break;
                    _game.ToggleWalls();
                    StatusText.Text = _game.WallsOff
                        ? "Режим: без стен — границу показывает подсветка"
                        : "Стены: ВКЛ";
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


        //Считает реальное время кадра и передаёт его в игру.
        //Внутри Frame идёт разбор на фиксированные логические тики(20 Гц)
        //плюс рендер каждый кадр.

        //Ограничитель dt > 0.5 защищает от «прыжка во времени»
        //после сна системы или сворачивания окна: без него аккумулятор логики
        //получил бы полминуты сразу и змейка мгновенно врезалась бы в стену.

        //HUD обновляется не каждый кадр, а раз в ~30 мс — проекция 50 кристаллов
        //в пиксели дешевле частоты кадров не требует.

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

        //UpdateHudVisuals — содержимое оверлея:
        //в режиме обзора прицел скрыт, маркеры пустые;

        //в кабине для каждого кристалла вызывается
        //Proxy.ProjectToScreen(x, y, z, ...) — проекция 3D-точки в пиксели вьюпорта.
        //Точка показывается, только если она перед камерой (inFront) и внутри
        //прямоугольника экрана.Координата sy приходят из OCCT с origin
        //в нижнем углу, поэтому в WPF её переворачивают: ys.Add(h - sy).
        private void UpdateHudVisuals()
        {
            if (_hud == null) return;

            bool cockpit = _viewport?.Proxy != null && _viewport.Proxy.IsCockpitView();
            _hud.ShowCrosshair(cockpit);

            //if (!cockpit)
            //{
            //    _hud.PlaceCrystal(null, 0);
            //    return;
            //}

            var crystals = _game.Crystals;
            var xs = new List<double?>();
            var ys = new List<double>();

            double w = ViewportHost.ActualWidth;
            double h = ViewportHost.ActualHeight;

            foreach (var c in crystals)
            {
                int sx = 0, sy = 0;
                bool inFront = false;
                bool ok = _viewport.Proxy.ProjectToScreen(c.X, c.Y, c.Z, ref sx, ref sy, ref inFront);

                if (ok && inFront && sx >= 0 && sx <= w && sy >= 0 && sy <= h)
                {
                    xs.Add(sx);
                    ys.Add(h - sy);   // из нижней системы координат в верхнюю
                }
                else
                {
                    xs.Add(null);
                    ys.Add(0);
                }
            }

            _hud.UpdateCrystalMarkers(xs, ys);
        }

        //UpdateHudPlacement — геометрическая привязка оверлея:
        //ViewportHost.PointToScreen переводит верхний левый угол хоста
        //в экранные координаты, оверлей(отдельное безрамочное окно)
        //ставится точно поверх.Вызывается при LocationChanged и SizeChanged.
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

        //мышь на WinForms-события
        private void Viewport_WinMouseDown(object sender, WF.MouseEventArgs e)
        {
            if (_viewport?.Proxy == null) return;

            // MouseWheel в WinForms уходит только контролу в фокусе
            _viewport.Focus();

            if (e.Button == WF.MouseButtons.Left)
            {
                _rotating = true;
                if (_game != null) _game.SuppressCamera = true;

                int vy = _viewport.Height - e.Y;   // OCCT: ось Y снизу вверх
                _viewport.Proxy.StartRotate(e.X, vy);
            }
        }

        private void Viewport_WinMouseMove(object sender, WF.MouseEventArgs e)
        {
            if (_viewport?.Proxy == null || !_rotating) return;

            int vx = e.X;
            int vy = _viewport.Height - e.Y;

            if (e.Button == WF.MouseButtons.Left)
            {
                _viewport.Proxy.Rotate(vx, vy);
            }
            else if (e.Button == WF.MouseButtons.Middle)
            {
                // Pan: смещение в координатах OCCT — по Y знак переворачивается
                _viewport.Proxy.Pan(e.X - (int)_winLastPos.X, (int)_winLastPos.Y - e.Y);
            }

            _winLastPos = new Point(e.X, e.Y);
        }

        private void Viewport_WinMouseUp(object sender, WF.MouseEventArgs e)
        {
            _rotating = false;
            if (_game != null) _game.SuppressCamera = false;
        }

        private void Viewport_WinMouseWheel(object sender, WF.MouseEventArgs e)
        {
            if (_viewport?.Proxy == null) return;
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            _viewport.Proxy.ZoomBy(factor);
        }

        private void SnakeView_Unloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            _hud?.Close();
            _hud = null;
        }
    }
}


//Почему мышь «мёртвая»
//1. Airspace (главная причина). ViewportHost.Child = _viewport и комментарий про
//«конфликт WinForms ↔ WPF» говорят, что OCCT рисуется в нативном HWND,
//вставленном через WindowsFormsHost. Классическое ограничение WPF: над областью,
//занятой чужим HWND, ввод уходит напрямую в WinForms-контрол — WPF-события
//PreviewMouseDown/Move/Up/Wheel на окне там просто не срабатывают.
//Поэтому клавиши работают (фокус у окна), а мышь — нет.

//2. FollowHead затирает камеру. SnakeGame.Render() вызывает _proxy.FollowHead(0)
//в каждом кадре: даже если бы поворот сработал, следящий кадр тут же перезаписал
//бы eye/center/up.

//3. Конвенции координат. WinForms отдаёт e.Y сверху вниз, OCCT ждёт ось Y снизу
//вверх — без переворота вращение и панорама поедут «вверх ногами». А старый зум
//через Zoom(100,100,110,100) к тому же не различает направление колеса
//(используется только длина вектора).

