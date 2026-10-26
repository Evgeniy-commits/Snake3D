//Краткий «подкапотный» конвейер за один кадр
//WPF DispatcherTimer даёт dt (например, 16.6 мс).

//SnakeGame.Frame(dt):
//применяет удерживаемые повороты (Родригес) — плавно;
//добавляет dt в аккумулятор логики; если набралось 0.05 с — один или несколько Update() (шаг змейки, коллизии, счёт, рост);
//Render(): считает alpha(0..1) от последнего логического тика, интерполирует позиции и ориентации, отправляет их в прокси.

//SnakeSceneProxy:
//MoveHead / MoveSegment строят gp_Trsf(перенос + поворот по базису fwd/up/right)
//и меняют только матрицу уже созданных AIS_Shape;
//UpdateCrystalOrientations синхронно крутит все кристаллы на _spinAngle;
//FollowHead смещает камеру (с lerp или без, в зависимости от режима);
//Redraw() — OCCT перерисовывает сцену через OpenGl_GraphicDriver
//прямо в HWND WPF-контейнера.

//Итого: логика «думает» рывками по 50 мс, рендер сглаживает это в 60 FPS,
//а весь тяжёлый CAD-аппарат OCCT просыпается только один раз при загрузке
//моделей — дальше работают лишь дешёвые матрицы.

using Snake3DViewer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using static System.Net.Mime.MediaTypeNames;

namespace Snake3D
{
    public struct Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Point3D operator +(Point3D p, Vector3D v)
            => new Point3D(p.X + v.X, p.Y + v.Y, p.Z + v.Z);
    }

    public struct Vector3D
    {
        public double X, Y, Z;
        public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vector3D operator *(Vector3D v, double s)
            => new Vector3D(v.X * s, v.Y * s, v.Z * s);
        public static Vector3D operator *(double s, Vector3D v)
            => new Vector3D(v.X * s, v.Y * s, v.Z * s);
        public static Vector3D operator +(Vector3D a, Vector3D b)
            => new Vector3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3D operator -(Vector3D a, Vector3D b)
            => new Vector3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static double DotProduct(Vector3D a, Vector3D b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vector3D CrossProduct(Vector3D a, Vector3D b)
            => new Vector3D(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vector3D Normalized()
        {
            double len = Length;
            if (len < 1e-10) return new Vector3D(0, 0, 0);
            return new Vector3D(X / len, Y / len, Z / len);
        }
    }

    public class SnakeGame
    {
        private readonly SnakeSceneProxy _proxy;
        private List<Point3D> _segments;

        private readonly bool _shipLoaded;
        private double _elapsed;            //Время пульса двигателя
       
        //кристаллы
        private readonly List<Point3D> _crystals = new();
        public IReadOnlyList<Point3D> Crystals => _crystals;

        //мины
        private readonly List<Point3D> _mines = new();
        public const int MineScore = 50;
        public IReadOnlyList<Point3D> Mines => _mines;

        //Режим «без стен» (клавиша T)
        public bool WallsOff { get; private set; }
        // Радиус зоны предупреждения: 15 единиц = 4 
        private double WarnRadius => 5.0 * _cellSize;
        private double _cellSize;   // ДОЛЖЕН совпадать с шагом CreateGrid

        // Активные подсветки: (ось, сторона, i, j).
        // Diff-множество: прокси вызываем только для различий.
        //private readonly HashSet<(int axis, int side, int i, int j)> _highlightCells = new();
        // Активные подсветки: ключ → текущий уровень яркости 0..1
        private readonly Dictionary<(int axis, int side, int i, int j), double> _highlightCells = new();

        // Фаза «дыхания»: полная синусоида за 0,4 секунды
        private double _pulsePhase;


        private Vector3D _forward;   // куда летим
        private Vector3D _up;        // где верх

        private int _pendingTurn = 0;  // 0=нет, 1=лево, 2=право, 3=вверх, 4=вниз

        private readonly double  _segmentRadius;
        private readonly double  _step = GameConfig.StepLength;
        private readonly double  _foodRadius;
        private readonly double _arenaSize;
        private Random _rand = new Random();

        public int Score { get; private set; }
        public bool IsGameOver { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }

        // Пока пользователь крутит камеру мышью, автослёжку отключаем:
        // FollowHead() в каждом кадре перезаписывает eye/center/up
        // и поворот камеры не видно.
        public bool SuppressCamera { get; set; }

        public event EventHandler<int>? ScoreChanged;
        public event EventHandler? GameOver;

        //Новое меню
        private GameMode _mode;                 
        private int _missionTotal;
        private double _playTime;

        public GameMode Mode => _mode;
        public int MissionTotal => _missionTotal;
        public int MissionLeft => _crystals.Count + _superCrystals.Count;
        public double PlayTime => _playTime;

        public event EventHandler? Victory;

        //Плавный поворот
        private const double TurnSpeedRadPerSecond = Math.PI; //90 градусов в секунду
        private double _yawRate = 0.0;
        private double _pitchRate = 0.0;

        public bool TapTurnEnabled = false;

        // Если клавишу удерживают дольше этого времени — включается плавный поворот.
        // Быстрый тап (< порога) даёт дискретный поворот на 90° при отпускании.
        private static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(200);

        private DateTime? _leftPressTime;
        private DateTime? _rightPressTime;
        private DateTime? _upPressTime;
        private DateTime? _downPressTime;
        private bool _smoothActive;

        //private const double BaseLogicInterval = 0.05;
        public double SpeedMultiplier { get; set; } = 1.0;
        public double LogicInterval =>
            _step / (GameConfig.BaseStepsPerSecond * Math.Max(0.25, SpeedMultiplier));

        private readonly List<Point3D> _prevPositions = new();
        private readonly List<Point3D> _nextPositions = new();
        private Vector3D _prevForward, _nextForward;
        private Vector3D _prevUp, _nextUp;
        private DateTime _lastLogicTick = DateTime.UtcNow;
        private double _logicAccumulator = 0;

        private int _mineCount = GameConfig.DefaultMineCount;
        private int _crystalCount = GameConfig.DefaultCrystalCount;

        // Супер-кристаллы: три штуки, всплывают после 10 съеденных обычных,
        // живут минуту, затем возвращаются в пул обычных
        public const int SuperCrystalScore = 20;
        private const int SuperBatchCount = 3;
        private const int SuperTriggerCount = 10;
        private const double SuperDuration = 60.0;

        private readonly List<Point3D> _superCrystals = new();
        public IReadOnlyList<Point3D> SuperCrystals => _superCrystals;

        private bool _superActive;      // окно супер-кристаллов открыто
        private double _superTimer;     // сколько осталось до конца окна
        private int _eatenSinceSuper;   // счётчик съеденных обычных (идёт только при закрытом окне)

        //Для замера производительности

        private readonly System.Diagnostics.Stopwatch _renderWatch = new();
        private readonly System.Diagnostics.Stopwatch _frameWatch = new();
        private double _frameMsSum;
        private int _frameCount;
        private readonly System.Diagnostics.Stopwatch _watch = new();
        private double _logicMsSum;
        private double _tCrystals;
        private double _tSegments;
        private double _tCamera;
        private double _tRedraw;
        private double _dtSum;
        private double _tHead;

        //Звуки
        public event Action? CrystalEaten;
        public event Action? MineHit;
        public event Action? SuperSpawned;

        public SnakeGame(SnakeSceneProxy proxy, double arenaSize, double gridStep,
                                            double segmentRadius, double foodRadius)
        {
            _proxy = proxy;
            _arenaSize = arenaSize;
            _cellSize = gridStep;
            //_step = gridStep;
            _segmentRadius = segmentRadius;
            _foodRadius = foodRadius;
            _segments = new List<Point3D>();

            string modelsDir = @"models\";

            // Загрузка модели головы — один раз, до первого Reset()
            bool ok = _proxy.LoadHeadModel(System.IO.Path.Combine(modelsDir, "Shatl.stp"), 0.75 * segmentRadius);
            if (!ok)
            {
                // Файл не прочитался — AddSegment сам откатится на fallback-капсулу,
                // но полезно знать об этом сразу
                System.Diagnostics.Trace.WriteLine("Не удалось загрузить модель головы, будет fallback-капсула");
            }

            // Загрузка моделей хвоста, кристалла и мины — один раз, до первого Reset()

            bool tailOk = _proxy.LoadStepModel(System.IO.Path.Combine(modelsDir, "Tail.stp"), 1, segmentRadius * 1.5);
            bool crystalOk = _proxy.LoadStepModel(System.IO.Path.Combine(modelsDir, "Crystall.stp"), 2, foodRadius * 2.0);
            bool mineOk = _proxy.LoadStepModel(System.IO.Path.Combine(modelsDir, "Mine.stp"), 4, foodRadius * 1.5);
            
            // Супер-кристалл: та же модель Crystall.stp, id 8, крупнее обычного
            bool superOk = _proxy.LoadStepModel(System.IO.Path.Combine(modelsDir, "Crystall.stp"), 8, foodRadius * 3.5);
            System.Diagnostics.Trace.WriteLine($"Супер-кристалл: {superOk}");

            System.Diagnostics.Trace.WriteLine($"Модели: tail={tailOk} crystal={crystalOk} mine={mineOk}");

            // Корабль: корпус, кабина, двигатель — id 5, 6, 7.
            // Габариты: корпус ≈ длина старой головы (3 × радиус сегмента),
            // кабина и двигатель — доли от неё, коэффициенты в GameConfig.Ship.Scales.
            bool shipOk = true;

            if (shipOk)
                _proxy.CenterShipAssembly(GameConfig.Ship.HullId, GameConfig.Ship.Paths.Length);

            for (int i = 0; i < GameConfig.Ship.Paths.Length; i++)
            {
                int id = GameConfig.Ship.HullId + i;
                shipOk &= _proxy.LoadShipPart(GameConfig.Ship.Paths[i], id, GameConfig.Ship.Scale);
                System.Diagnostics.Trace.WriteLine($"Часть {id}: {shipOk}");
            }
            _shipLoaded = shipOk;

            // Подгонка и покраска — один раз: _fitTrsf переживает ClearScene.
            if (_shipLoaded)
            {
                for (int i = 0; i < GameConfig.Ship.Paths.Length; i++)
                {
                    int id = GameConfig.Ship.HullId + i;
                    var f = GameConfig.Ship.Fit[i];
                    _proxy.SetPartPlacement(id, f[0], f[1], f[2], f[3], f[4], f[5]);
                    var c = GameConfig.Ship.Color[i];
                    _proxy.SetPartColor(id, c[0], c[1], c[2], GameConfig.Ship.Material[i]);
                }
            }
        }

        public void Start()
        {
            IsRunning = true;
            IsGameOver = false;
        }

        public void TogglePause()
        {
            if (IsGameOver || !IsRunning) return;
            IsPaused = !IsPaused;

            if (IsPaused)
            {
                // на паузе сбрасываем зажатые клавиши,
                // чтобы после снятия змейка не сорвалась в плавный поворот
                StopTurning();
                _leftPressTime = null;
                _rightPressTime = null;
                _upPressTime = null;
                _downPressTime = null;
            }
            else
            {
                // чтобы после паузы не было скачка интерполяции
                _lastLogicTick = DateTime.UtcNow;
            }
        }

        public void ToggleWalls()
        {
            SetWalls(WallsOff);
            if (!WallsOff)
                foreach (var c in _highlightCells.Keys)
                    _proxy.SetHighlightLevel(c.axis, c.side, c.i, c.j, 0.0);
            _highlightCells.Clear();
        }

        // Единственная точка применения опций меню
        // Вызывается ДО Reset: Reset спавнит объекты уже с учётом значений —
        // «спавнить, а потом прятать» не нужно нигде.
        public void ApplyOptions(GameOptions o)
        {
            _mode = o.Mode;
            TapTurnEnabled = o.TapControl;
            SpeedMultiplier = o.Speed;
            SetWalls(o.WallsOn);

            _crystalCount = Math.Clamp(o.CrystalCount, 1, GameConfig.MaxCrystalCount);
            if (_mode == GameMode.Mission)
            {
                _mineCount = 0;                       // миссия стартует без мин
                _missionTotal = _crystalCount;
            }
            else
            {
                _mineCount = Math.Clamp(o.MinesCount, 0, GameConfig.MaxMineCount);
                _missionTotal = 0;
            }
        }

        public void SetWalls(bool on)
        {
            bool newOff = !on;
            if (WallsOff == newOff) return;   //повторный вызов — пустой
            WallsOff = newOff;
            _proxy.SetWallsVisible(on);
        }

        public void Reset()
        {
            _playTime = 0;
            _proxy.ClearScene();

            // Арена: стены + сетка пересоздаются после каждой чистки.
            // Шаг сетки = _cellSize (задан конструктором из опций меню),
            // никаких локальных «5.0» — единый источник истины.
            _proxy.CreateWalls(_arenaSize);
            _proxy.CreateGrid(_arenaSize, _cellSize);
            _proxy.SetHighlightGrid(_arenaSize, _cellSize);

            // Если режим «без стен» был включён до рестарта —
            // CreateWalls отобразил стены заново, прячем обратно
            if (WallsOff) _proxy.SetWallsVisible(false);

            // Корабль: ClearScene стёр части со сцены — пересоздаём их из кэша моделей.
            // до AddSegment(0) — заглушка головы ставится только если
            // _shipParts непуста, иначе прокси откатится на старую голову.
            if (_shipLoaded)
            {
                for (int i = 0; i < GameConfig.Ship.Paths.Length; i++)
                {
                    int id = GameConfig.Ship.HullId + i;
                    _proxy.AddShipPart(id);
                    var c = GameConfig.Ship.Color[i];
                    _proxy.SetPartColor(id, c[0], c[1], c[2], GameConfig.Ship.Material[i]);
                }
            }

            _segments.Clear();

            for (int i = 0; i < 5; i++)
            {
                Point3D p = new Point3D(0, -i * _step, 0);
                _segments.Add(p);
                _proxy.AddSegment(p.X, p.Y, p.Z, _segmentRadius, i);
            }
            if (_shipLoaded)
                _proxy.HideSegment(1);

            _forward = new Vector3D(0, 1, 0);   // летим вперёд по Y
            _up = new Vector3D(0, 0, 1);   // Z — верх
            _pendingTurn = 0;
            _yawRate = 0.0;
            _pitchRate = 0.0;
            _smoothActive = false;
            _leftPressTime = null;
            _rightPressTime = null;
            _upPressTime = null;
            _downPressTime = null;

            _prevPositions.Clear();
            foreach (var s in _segments) _prevPositions.Add(s);
            _nextPositions.Clear();
            foreach (var s in _segments) _nextPositions.Add(s);
            _prevForward = _forward; _nextForward = _forward;
            _prevUp = _up; _nextUp = _up;
            _lastLogicTick = DateTime.UtcNow;
            _elapsed = 0;

            // Кристаллы и мины: раскладка по счётчикам из меню.
            // Списки чистим и заполняем заново — рестарт с нулём старых объектов.
            _crystals.Clear();
            _superCrystals.Clear();
            _superActive = false;
            _superTimer = 0.0;
            _eatenSinceSuper = 0;
            EnsureCrystals();

            _mines.Clear();
            EnsureMines();

            Score = 0;
            ScoreChanged?.Invoke(this, Score);
            _proxy.Redraw();
        }

        // Вызывается из View при КАЖДОМ KeyDown (в том числе при автоповторе WPF).
        // Время нажатия ставим только один раз — иначе автоповтор сбросит таймер
        // и плавный поворот никогда не включится.
        public void StartTurn(int dir) // 1=лево, 2=право, 3=вверх, 4=вниз
        {
            DateTime now = DateTime.UtcNow;
            switch (dir)
            {
                case 1: if (_leftPressTime == null) _leftPressTime = now; break;
                case 2: if (_rightPressTime == null) _rightPressTime = now; break;
                case 3: if (_upPressTime == null) _upPressTime = now; break;
                case 4: if (_downPressTime == null) _downPressTime = now; break;
            }
            
            if (!TapTurnEnabled)
            {
                // плавный режим: реагируем сразу, без порога удержания
                _smoothActive = true;
                ApplyHeldDirections();
            }
            else if (_smoothActive)
            {
                ApplyHeldDirections();
            }
        }
        
        // Вызывается из View при KeyUp.
        public void ReleaseTurn(int dir)
        {
            //!!!!СНАЧАЛА очищаем время нажатия отпущенной клавиши
            switch (dir)
            {
                case 1: _leftPressTime = null; break;
                case 2: _rightPressTime = null; break;
                case 3: _upPressTime = null; break;
                case 4: _downPressTime = null; break;
            }

            if (_smoothActive)
            {
                ApplyHeldDirections();
                if (_yawRate == 0 && _pitchRate == 0) StopTurning();
            }
            else if (TapTurnEnabled)
            {
                _pendingTurn = dir;
            }

        }

        public void StopTurning()
        {
            _yawRate = 0.0;
            _pitchRate = 0.0;
            _smoothActive = false;
        }

        // Шаг 1 конвейера: применить накопленный поворот
        private void ApplyTurn()
        {
            switch (_pendingTurn)
            {
                case 1: RotateYaw(1); break;    // лево
                case 2: RotateYaw(-1); break;   // право
                case 3: RotatePitch(1); break;  // вверх
                case 4: RotatePitch(-1); break; // вниз
            }
            _pendingTurn = 0;
        }

        // Шаг 2 конвейера: шаг вперёд 
        // Возвращает null, если голова вылетела за арену
        // (TriggerGameOver уже вызван). Через oldTail отдаёт
        // позицию хвоста до сдвига — она нужна для роста.
        private bool Advance(out Point3D newHead, out Point3D oldTail)
        {
            newHead = default;
            oldTail = default;

            // было _direction, теперь _forward
            newHead = _segments[0] + _forward * _step;

            double half = _arenaSize / 1.7;

            //границы куба по всем осям
            if (Math.Abs(newHead.X) > half ||
                Math.Abs(newHead.Y) > half ||
                Math.Abs(newHead.Z) > half)
            {
                TriggerGameOver();
                return false;
            }

            // запоминаем хвост до сдвига
            oldTail = _segments[_segments.Count - 1];

            //сдвиг тела
            for (int i = _segments.Count - 1; i > 0; i--)
            {
                _segments[i] = _segments[i - 1];
            }
            _segments[0] = newHead;

            return true;
        }

        // Шаг 3 конвейера: кристаллы, мины, рост, самопересечение
        // Возвращает true, если игра закончена (TriggerGameOver вызван).
        private bool CheckCollisions(Point3D newHead, Point3D oldTail)
        {
            // Сумма радиусов: голова космолета + кристалл
            double combinedRadius = _segmentRadius + _foodRadius;

            // Допуск «почти коснулся»: если расстояние меньше суммы радиусов + небольшой запас
            // Запас нужен, чтобы компенсировать «пропуск» кадра (космолет пролетел между кадрами)
            double tolerance = 0.8;

            bool ateFood = false;

            for (int i = 0; i < _crystals.Count; i++)
            {
                if (Distance(newHead, _crystals[i]) <= _segmentRadius + _foodRadius + tolerance)
                {
                    _proxy.RemoveCrystalAt(i);
                    _crystals.RemoveAt(i);
                    ateFood = true;

                    if (_mode == GameMode.FreeHunt)
                    {
                        EnsureCrystals();      // охота: поле кристаллов пополняется, как раньше
                        SpawnMines(1);         // охота: каждый съеденный кристалл добавляет одну мину
                    }                      
                    else
                        SpawnMines(GameConfig.MinesPerCristal);  // миссия: расплата минами

                    // Отсчёт до спавна супер-кристаллов: идёт только пока окно закрыто
                    if (!_superActive)
                    {
                        _eatenSinceSuper++;
                        if (_eatenSinceSuper >= SuperTriggerCount)
                            TrySpawnSuperCrystals();
                    }

                    Score += 10;
                    ScoreChanged?.Invoke(this, Score);
                    CrystalEaten?.Invoke();

                    if (_mode == GameMode.Mission && _crystals.Count == 0 && _superCrystals.Count == 0)
                    {
                        TriggerVictory();
                        return true;
                    }
                    break;
                }
            }

            // Супер-кристаллы: коллизии те же, что у обычного, награда 20
            for (int i = 0; i < _superCrystals.Count; i++)
            {
                if (Distance(newHead, _superCrystals[i]) <= _segmentRadius + _foodRadius + tolerance)
                {
                    Score += SuperCrystalScore;
                    ScoreChanged?.Invoke(this, Score);
                    
                    CrystalEaten?.Invoke();

                    _proxy.RemoveSuperCrystalAt(i);
                    _superCrystals.RemoveAt(i);

                    if (_mode == GameMode.FreeHunt)
                        EnsureCrystals();   // съеденный супер заменяется обычным
                    else if (_crystals.Count == 0 && _superCrystals.Count == 0)
                    {
                        TriggerVictory();
                        return true;
                    }

                    break;
                }
            }

            for (int i = 0; i < _mines.Count; i++)
            {
                if (Distance(newHead, _mines[i]) <= _segmentRadius + _foodRadius + tolerance)
                {
                    Score -= MineScore;
                    ScoreChanged?.Invoke(this, Score);

                    MineHit?.Invoke();
                    _proxy.RemoveMineAt(i);
                    _mines.RemoveAt(i);

                    if (Score < 0)
                    {
                        TriggerGameOver();
                        return true;
                    }

                    EnsureMines(); //восстанавливаем мины
                    break;
                }
            }

            //рост
            if (ateFood)
            {
                _segments.Add(oldTail);
                _proxy.AddSegment(oldTail.X, oldTail.Y, oldTail.Z, _segmentRadius, _segments.Count - 1);
            }

            //пропуск первых 4 сегментов (было 2)
            if (_segments.Count > 5)
            {
                double crashRadius = _step * 0.6;
                for (int i = 4; i < _segments.Count; i++)
                {
                    if (Distance(newHead, _segments[i]) < crashRadius)
                    {
                        TriggerGameOver();
                        return true;
                    }
                }
            }

            return false;
        }

        public void Update()
        {
            if (!IsRunning || IsGameOver || IsPaused) return;
            if (_segments.Count == 0) { TriggerGameOver(); return; }

            // состояние ДО тика — для интерполяции
            _prevPositions.Clear();
            foreach (var s in _segments) _prevPositions.Add(s);
            _prevForward = _forward;
            _prevUp = _up;

            //double dt = LogicInterval;

            UpdateSmoothTurn();

            // Конвейер тика: поворот → шаг → проверки
            ApplyTurn();
            if (!Advance(out Point3D newHead, out Point3D oldTail))
                return;                                            // проигрыш уже оформлен в Advance
            if (CheckCollisions(newHead, oldTail)) return;         // проигрыш уже оформлен

            // Подсветка границы — раз на логический тик (20 Гц), не на кадр
            UpdateBoundaryHighlights(newHead);

            //камера
            //_proxy.FollowHead(0);
            //_proxy.Redraw();

            // состояние ПОСЛЕ тика
            _nextPositions.Clear();
            foreach (var s in _segments) _nextPositions.Add(s);

            //нормализация, чтобы модель не дергалась
            double fl = Math.Sqrt(_forward.X * _forward.X + _forward.Y * _forward.Y + _forward.Z * _forward.Z);
            if (fl > 1e-9) _forward = new Vector3D(_forward.X / fl, _forward.Y / fl, _forward.Z / fl);

            double ud = _up.X * _forward.X + _up.Y * _forward.Y + _up.Z * _forward.Z;
            _up = new Vector3D(_up.X - ud * _forward.X, _up.Y - ud * _forward.Y, _up.Z - ud * _forward.Z);
            double ul = Math.Sqrt(_up.X * _up.X + _up.Y * _up.Y + _up.Z * _up.Z);
            if (ul > 1e-9) _up = new Vector3D(_up.X / ul, _up.Y / ul, _up.Z / ul);

            _nextForward = _forward;
            _nextUp = _up;
            _lastLogicTick = DateTime.UtcNow;
        }

        //Это классический паттерн game loop с fixed timestep:

        //Логика шагает дискретно(ровно 20 раз в секунду независимо от FPS).
        //dt берётся из таймера WPF — он плавающий.Аккумулятор копит его и
        //«сбрасывает» по 0.05 с на каждый логический тик,
        //возможно выполняя несколько тиков за один кадр(если FPS низкий).
        //Ограничение LogicInterval * 4 — предохранитель: если игра была свернута
        //на минуту, не нужно за один кадр делать 1200 тиков — иначе всё телепортируется.
        //Рендер идёт каждый кадр, но не синхронно с логикой — этим занимается
        //интерполяция.
        public void Frame(double dt)
        {
            _playTime += dt;
            _dtSum += dt;
            _frameWatch.Restart();
            _watch.Restart();

            if (IsRunning && !IsGameOver && !IsPaused)
            {
                // плавный поворот — каждый кадр, мгновенный отклик
                if (_yawRate != 0 || _pitchRate != 0)
                {
                    _prevForward = _forward;
                    _prevUp = _up;
                    if (_yawRate != 0) RotateYawContinuos(_yawRate * dt);
                    if (_pitchRate != 0) RotatePitchContinuos(_pitchRate * dt);
                    _nextForward = _forward;
                    _nextUp = _up;
                }

                _logicAccumulator += dt;
                if (_logicAccumulator > LogicInterval * 4)
                    _logicAccumulator = LogicInterval;

                while (_logicAccumulator >= LogicInterval)
                {
                    Update();
                    _logicAccumulator -= LogicInterval;
                }

                TickSuperCrystals(dt);   // минута супер-кристаллов: тикает пока игра активна
            }

            _pulsePhase += dt * (2.0 * Math.PI / 0.4);

            _elapsed += dt;

            _proxy.UpdateSpin(dt); //копим угол только пока игра идёт

            _watch.Stop();
            _logicMsSum += _watch.Elapsed.TotalMilliseconds;
            // --- конец замера логики ---

            _renderWatch.Restart();
            Render();
            _renderWatch.Stop();
            double tRender = _renderWatch.Elapsed.TotalMilliseconds;

            _frameWatch.Stop();
            _frameMsSum += _frameWatch.Elapsed.TotalMilliseconds;
            if (++_frameCount >= 60)   // раз в секунду при 60 FPS
            {
                double dtAvg = _dtSum / _frameCount;
                double frameAvg = _frameMsSum / _frameCount;
                double logicAvg = _logicMsSum / _frameCount;
                double crystalsAvg = _tCrystals / _frameCount;
                double segmentsAvg = _tSegments / _frameCount;
                double headAvg = _tHead / _frameCount;        // ← добавить сюда
                double cameraAvg = _tCamera / _frameCount;
                double redrawAvg = _tRedraw / _frameCount;
                int segments = _segments.Count, crystals = _crystals.Count, mines = _mines.Count;
                int supers = _proxy.SuperCount;

                _frameMsSum = 0;
                _logicMsSum = 0;
                _tCrystals = _tSegments = _tCamera = _tRedraw = _tHead = 0;   // ← и _tHead в обнуление
                _dtSum = 0;
                _frameCount = 0;

                System.Diagnostics.Trace.WriteLine(
                    $"Кадр: {frameAvg:F2} | логика: {logicAvg:F2} | рендер: {tRender:F2} | кристаллы: {crystalsAvg:F2} " +
                    $"| сегменты: {segmentsAvg:F2} | камера: {cameraAvg:F2} | Redraw: {redrawAvg:F2} " +
                    $"| FPS: {1.0 / dtAvg:F1} | объекты: {segments}+{crystals}+{mines}" +
                    $"| голова: {headAvg:F2}" +
                    $"| супер: {supers}");
            }
        }

        //Каждая позиция сегмента вычисляется как lerp(prev, next, alpha).
        //То есть визуально змейка «плывёт» между логическими позициями,
        //а не прыгает раз в 50 мс.Аналогично интерполируются _forward и _up,
        //с последующей ортогонализацией, чтобы базис не «съезжал» из-за
        //смешивания двух состояний.
        //Здесь же вызываются _proxy.SetOrientation(...) и _proxy.UpdateCrystalOrientations()
        //— OCCT получает актуальные оси полёта.

        //плавно тянет visuals от прошлого тика к новому
        public void Render()
        {
            if (_segments.Count == 0) return;

            double alpha = (DateTime.UtcNow - _lastLogicTick).TotalSeconds / LogicInterval;
            if (alpha < 0) alpha = 0;
            if (alpha > 1) alpha = 1;

            double fx = _prevForward.X + (_nextForward.X - _prevForward.X) * alpha;
            double fy = _prevForward.Y + (_nextForward.Y - _prevForward.Y) * alpha;
            double fz = _prevForward.Z + (_nextForward.Z - _prevForward.Z) * alpha;
            double ux = _prevUp.X + (_nextUp.X - _prevUp.X) * alpha;
            double uy = _prevUp.Y + (_nextUp.Y - _prevUp.Y) * alpha;
            double uz = _prevUp.Z + (_nextUp.Z - _prevUp.Z) * alpha;

            // 1. Нормализуем forward
            double flen = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            if (flen > 1e-9) { fx /= flen; fy /= flen; fz /= flen; }

            // 2. Ортогонализуем up: убираем компоненту вдоль forward
            double dot = ux * fx + uy * fy + uz * fz;
            ux -= dot * fx; uy -= dot * fy; uz -= dot * fz;

            // 3. Нормализуем up
            double ulen = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (ulen > 1e-9) { ux /= ulen; uy /= ulen; uz /= ulen; }

            _watch.Restart();
            _proxy.SetOrientation(fx, fy, fz, ux, uy, uz);
            _proxy.UpdateCrystalOrientations();
            _watch.Stop();
            _tCrystals += _watch.Elapsed.TotalMilliseconds;
                
            _watch.Restart();
            for (int i = 0; i < _nextPositions.Count; i++)
            {
                Point3D prev = i < _prevPositions.Count ? _prevPositions[i] : _nextPositions[i];
                Point3D next = _nextPositions[i];

                double x = prev.X + (next.X - prev.X) * alpha;
                double y = prev.Y + (next.Y - prev.Y) * alpha;
                double z = prev.Z + (next.Z - prev.Z) * alpha;

                if (i == 0)
                {
                    _watch.Restart();
                    if (_shipLoaded)
                        _proxy.PlaceShip(x, y, z, fx, fy, fz, ux, uy, uz, _elapsed);
                    else
                        _proxy.MoveHead(0, x, y, z);
                    _watch.Stop();
                    _tHead += _watch.Elapsed.TotalMilliseconds;
                }
                else
                {
                    if (_shipLoaded && i == 1) continue;
                    _proxy.MoveSegment(i, x, y, z);
                }
            }
            _watch.Stop();
            _tSegments += _watch.Elapsed.TotalMilliseconds;

            _watch.Restart();
            if (!SuppressCamera)            // свободная камера: не трогаем eye/center/up
                _proxy.FollowHead(0);       // камера — ПОСЛЕ перемещения всех объектов
            _watch.Stop();
            _tCamera += _watch.Elapsed.TotalMilliseconds;

            _watch.Restart();
            _proxy.Redraw();
            _watch.Stop();
            _tRedraw += _watch.Elapsed.TotalMilliseconds;
        }

        private void UpdateSmoothTurn()
        {
            if (!TapTurnEnabled) return; // в плавном режиме всё решают StartTurn/ReleaseTurn
            if (_smoothActive) return;

            // Дискретный режим: как раньше — ждём порог удержания
            DateTime now = DateTime.UtcNow;
            if (_leftPressTime.HasValue && now - _leftPressTime.Value > HoldThreshold ||
                _rightPressTime.HasValue && now - _rightPressTime.Value > HoldThreshold ||
                _upPressTime.HasValue && now - _upPressTime.Value > HoldThreshold ||
                _downPressTime.HasValue && now - _downPressTime.Value > HoldThreshold)
            {
                _smoothActive = true;
                ApplyHeldDirections();
            }
        }

        private void ApplyHeldDirections()
        {
            bool up = _upPressTime.HasValue, down = _downPressTime.HasValue;
            bool left = _leftPressTime.HasValue, right = _rightPressTime.HasValue;

            if (up && down) _pitchRate = 0;
            else if (up) _pitchRate = TurnSpeedRadPerSecond;
            else if (down) _pitchRate = -TurnSpeedRadPerSecond;
            else _pitchRate = 0;

            if (left && right) _yawRate = 0;
            else if (left) _yawRate = TurnSpeedRadPerSecond;
            else if (right) _yawRate = -TurnSpeedRadPerSecond;
            else _yawRate = 0;
        }

        // Подсветка границы в режиме «без стен».
        // Для каждой из 6 плоскостей: дистанция головы до плоскости,
        // точка удара курса и пятно подсветки, растущее по мере приближения.
        private void UpdateBoundaryHighlights(Point3D head)
        {
            var wanted = new Dictionary<(int axis, int side, int i, int j), double>();

            if (WallsOff)
            {
                double half = _arenaSize / 2.0;
                int n = (int)Math.Round(_arenaSize / _cellSize); // ячеек на сторону

                for (int axis = 0; axis < 3; axis++)
                {
                    double coord = axis == 0 ? head.X : axis == 1 ? head.Y : head.Z;
                    double f = axis == 0 ? _forward.X : axis == 1 ? _forward.Y : _forward.Z;

                    double d = half - Math.Abs(coord);   // дистанция до плоскости
                    if (d > WarnRadius) continue;

                    int side = coord >= 0 ? 1 : -1;

                    // Точка удара: продолжаем курс до пересечения с плоскостью.
                    // t = d / |f_оси| — луч из головы вдоль forward до плоскости.
                    // Если к плоскости не летим — берём проекцию головы на неё.
                    bool flyingTo = f * side > 1e-6;
                    double t = flyingTo ? d / Math.Abs(f) : 0.0;
                    double ix = head.X + _forward.X * t;
                    double iy = head.Y + _forward.Y * t;
                    double iz = head.Z + _forward.Z * t;

                    // Пятно: на входе в зону — 0.75 клетки (горит одна ячейка),
                    // у самой границы — весь WarnRadius. Максимум гарантирует,
                    // что хотя бы ближайшая ячейка загорится сразу.
                    double r = Math.Max(_cellSize * 0.75, WarnRadius - d);

                    for (int i = 0; i < n; i++)
                    {
                        for (int j = 0; j < n; j++)
                        {
                            double u = -half + (i + 0.5) * _cellSize;
                            double v = -half + (j + 0.5) * _cellSize;

                            double du, dv;
                            if (axis == 0) { du = iy - u; dv = iz - v; }
                            else if (axis == 1) { du = ix - u; dv = iz - v; }
                            else { du = ix - u; dv = iy - v; }

                            double dist = Math.Sqrt(du * du + dv * dv);
                            if (dist > r) continue;

                            // Пространственный градиент: 1.0 в точке удара, 0.0 на краю пятна
                            double level = 1.0 - dist / r;

                            // Временной градиент: пульс 0.78..1.0 — пятно «дышит» даже в покое
                            double pulse = 0.6 + 0.4 * (0.5 + 0.5 * Math.Sin(_pulsePhase)); // размах 0.6..1.0

                            wanted[(axis, side, i, j)] = level * pulse;
                        }
                    }
                }
            }

            // Гасим пропавшие, обновляем заметно изменившиеся
            foreach (var c in _highlightCells)
                if (!wanted.ContainsKey(c.Key))
                    _proxy.SetHighlightLevel(c.Key.axis, c.Key.side, c.Key.i, c.Key.j, 0.0);

            foreach (var kv in wanted)
            {
                if (!_highlightCells.TryGetValue(kv.Key, out double prev) ||
                    Math.Abs(prev - kv.Value) > 0.05)
                    _proxy.SetHighlightLevel(kv.Key.axis, kv.Key.side, kv.Key.i, kv.Key.j, kv.Value);
            }

            _highlightCells.Clear();
            foreach (var kv in wanted) _highlightCells[kv.Key] = kv.Value;

            _proxy.CommitHighlights();   // одна перерисовка на весь тик
        }

        //Поиск свободного места(FindFreeSpot)
        //Случайная точка с проверкой: не на змейке, не на кристалле,
        //не на мине.До 200 попыток; если не нашли — ставим куда угодно
        //(иначе игра зависнет, если арена забита объектами).
        private Point3D FindFreeSpot()
        {
            double margin = 1.5;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                double x = _rand.NextDouble() * (_arenaSize - 2 * margin) - (_arenaSize / 2 - margin);
                double y = _rand.NextDouble() * (_arenaSize - 2 * margin) - (_arenaSize / 2 - margin);
                double z = _rand.NextDouble() * (_arenaSize - 2 * margin) - (_arenaSize / 2 - margin);
                var p = new Point3D(x, y, z);

                bool free = true;

                // не на змейке
                foreach (var s in _segments)
                    if (Distance(p, s) < _segmentRadius + _foodRadius + 2.0) { free = false; break; }

                // не на другом кристалле
                if (free)
                    foreach (var c in _crystals)
                        if (Distance(p, c) < _foodRadius * 2 + 2.0) { free = false; break; }

                if (free)
                    foreach (var m in _mines)
                        if (Distance(p, m) < _foodRadius * 2 + 2.0) { free = false; break; }

                if (free)
                    foreach (var s in _superCrystals)
                        if (Distance(p, s) < _foodRadius * 2 + 2.0) { free = false; break; }

                if (free) return p;
            }
            // не нашли за 200 попыток — ставим куда угодно в пределах
            return new Point3D(
                _rand.NextDouble() * (_arenaSize - 2 * margin) - (_arenaSize / 2 - margin),
                _rand.NextDouble() * (_arenaSize - 2 * margin) - (_arenaSize / 2 - margin),
                _rand.NextDouble() * (_arenaSize - 2 * margin) - (_arenaSize / 2 - margin));
        }

        private void EnsureCrystals()
        {
            // Сумма обычных и супер не должна превышать норму поля —
            // иначе во время окна кристаллы начнут размножаться
            while (_crystals.Count + _superCrystals.Count < _crystalCount)
            {
                Point3D p = FindFreeSpot();
                _crystals.Add(p);
                _proxy.AddCrystalAt(_crystals.Count - 1, p.X, p.Y, p.Z, _foodRadius);
            }
        }

        private void EnsureMines()
        {
            while (_mines.Count < _mineCount)
            {
                Point3D p = FindFreeSpot();
                _mines.Add(p);
                _proxy.AddMineAt(_mines.Count - 1, p.X, p.Y, p.Z, _foodRadius);
            }
        }

        private void SpawnMines(int count)
        {
            for (int k = 0; k < count && _mines.Count < GameConfig.MaxMineCount; k++)
            {
                Point3D p = FindFreeSpot();
                _mines.Add(p);
                _proxy.AddMineAt(_mines.Count - 1, p.X, p.Y, p.Z, _foodRadius);
            }
        }

        // Спавн супер-кристаллов: три обычных случайных заменяются крупными фиолетовыми.
        // Сумма кристаллов на поле не меняется — счёт не дёргается.
        private void TrySpawnSuperCrystals()
        {
            if (_superActive) return;
            if (_crystals.Count < SuperBatchCount) return;   // заменять нечего — ждём следующего съеденного

            // Три случайных разных индекса; удаляем по убыванию, чтобы не сдвинуть невыданные
            var picked = new List<int>();
            while (picked.Count < SuperBatchCount)
            {
                int k = _rand.Next(_crystals.Count);
                if (!picked.Contains(k)) picked.Add(k);
            }
            picked.Sort((a, b) => b.CompareTo(a));

            foreach (int idx in picked)
            {
                Point3D p = _crystals[idx];
                _crystals.RemoveAt(idx);
                _proxy.RemoveCrystalAt(idx);
                _superCrystals.Add(p);
                _proxy.AddSuperCrystalAt(p.X, p.Y, p.Z, _foodRadius);
            }

            _superActive = true;
            _superTimer = SuperDuration;
            _eatenSinceSuper = 0;   // новый отсчёт начнётся только после конца минуты

            SuperSpawned?.Invoke();
        }

        // Таймер окна: выжившие супер-кристаллы возвращаются в пул обычных.
        // Вызывается из Frame — на паузе и после проигрыша минута замирает.
        private void TickSuperCrystals(double dt)
        {
            if (!_superActive) return;

            _superTimer -= dt;
            if (_superTimer > 0) return;

            for (int i = 0; i < _superCrystals.Count; i++)
            {
                Point3D p = _superCrystals[i];
                _proxy.RemoveSuperCrystalAt(i);
                _crystals.Add(p);
                _proxy.AddCrystalAt(_crystals.Count - 1, p.X, p.Y, p.Z, _foodRadius);
            }
            _superCrystals.Clear();
            _superActive = false;
        }

        private void TriggerVictory()
        {
            IsRunning = false;
            Victory?.Invoke(this, EventArgs.Empty);
        }

        private void TriggerGameOver()
        {
            IsGameOver = true;
            IsRunning = false;
            GameOver?.Invoke(this, EventArgs.Empty);
        }

        private double Distance(Point3D a, Point3D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Поворот рысканья (вокруг up): лево/право
        private void RotateYaw(double sign)
        {
            _forward = Rotate90(_forward, _up, sign).Normalized();
        }

        // Поворот тангажа (вокруг right): вверх/вниз
        private void RotatePitch(double sign)
        {
            Vector3D right = Vector3D.CrossProduct(_forward, _up).Normalized();
            _forward = Rotate90(_forward, right, sign).Normalized();
            _up = Vector3D.CrossProduct(right, _forward).Normalized();
        }

        // Вращение на ±90° по формуле Родригеса (cos=0, sin=±1)
        private static Vector3D Rotate90(Vector3D v, Vector3D axis, double sign)
        {
            Vector3D cross = Vector3D.CrossProduct(axis, v);
            double dot = Vector3D.DotProduct(axis, v);
            return (cross * sign + axis * dot).Normalized();
        }

        //Плавный поворот
        private void RotateYawContinuos(double angleRad)
        {
            _forward = RotateVector(_forward, _up, angleRad).Normalized();
        }

        private void RotatePitchContinuos(double angleRad)
        {
            var right = Vector3D.CrossProduct(_forward, _up).Normalized();
            _forward = RotateVector(_forward, right, angleRad).Normalized();
            _up = Vector3D.CrossProduct(right, _forward).Normalized();
        }

        //Это векторная форма поворота: v·cosθ + (axis×v)·sinθ + axis(axis·v)(1−cosθ)
        //после раскрытия скобок.В коде просто разделено на «параллельную» и
        //«перпендикулярную» части.
        //Повороты: рысканье(yaw) — вокруг _up(влево/вправо), тангаж(pitch) —
        //вокруг right = forward × up(вверх/вниз). После pitch пересчитывается
        //_up, чтобы он не «отрывался» от курса.
        //Для быстрых дискретных поворотов есть отдельный трюк — Rotate90
        private static Vector3D RotateVector(Vector3D v, Vector3D axis, double theta)
        {
            axis = axis.Normalized();

            double cosT = Math.Cos(theta);
            double sinT = Math.Sin(theta);

            var parallel = axis * Vector3D.DotProduct(axis, v);
            var perpendicular = v - parallel;
            var rotatedPerp = perpendicular * cosT + Vector3D.CrossProduct(axis, perpendicular) * sinT;

            return parallel + rotatedPerp;
        }
    }
}


//ДЛЯ ПОДСВЕТКИ
//Диагностика коллизии. Причина запаздывания — рассинхрон двух систем координат
//одной и той же арены: стены и сетка построены в координатах ±arenaSize/2,
//а аналитическая проверка границы сравнивала с ±arenaSize.
//Полупрозрачность стен (0.75) визуально маскировала пролет сквозь них,
//поэтому баг выглядел как «задержка», а не «двойная толщина мира».
//Урок: любая аналитическая граница должна вычисляться из того же источника,
//что и геометрия (half = size/2 — единая формула).

//Подсветка. Конвейер за один тик: голова → для каждой плоскости дистанция
//d = half − |coord| → отсечение плоскостей дальше зоны
//предупреждения → луч-пересечение курса с плоскостью (t = d/|f|) → круг радиуса
//max(0.75·cell, R − d) вокруг точки удара → множество ячеек → diff
//с прошлым тиком → точечные SetHighlightCell. Прокси создаёт грань ячейки
//ровно один раз за всю жизнь приложения (кэш в std::map),
//дальше — только включение/выключение видимости. Сдвиг +0.02 от
//плоскости убирает z-fighting с линиями сетки.

//Ключевой компромисс, который стоит зафиксировать: подсветка меняется на
//логических тиках (20 Гц) — на глаз этого достаточно, а diff экономит и OCCT,
//и мусор GC. Если захотите плавного «разгорания» ячеек — это расширение:
//прокси должен принимать уровень (0..1) и менять прозрачность через
//SetTransparency, а diff — включать градиент по значению. Могу расписать
//вторым уровнем сложности, если для пособия нужен вариант «со звёздочкой».
