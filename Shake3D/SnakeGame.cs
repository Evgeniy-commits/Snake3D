using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using Snake3DViewer;

namespace Snake3D
{
    public struct Point3D
    {
        public double X, Y, Z;
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Point3D operator + (Point3D p, Vector3D v)
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
        //private Point3D _food;

        private Point3D _crystalPos;

        private Vector3D _forward;   // куда летим
        private Vector3D _up;        // где верх

        private int _pendingTurn = 0;  // 0=нет, 1=лево, 2=право, 3=вверх, 4=вниз

        private double _segmentRadius = 0.4;
        private double _step = 1.0;
        private double _foodRadius = 0.4;
        private double _arenaSize = 50.0;
        private Random _random = new Random();

        public int Score        { get; private set; }
        public bool IsGameOver  { get; private set; }
        public bool IsRunning   { get; private set; }

        public event EventHandler<int>? ScoreChanged;
        public event EventHandler? GameOver;
        
        public SnakeGame(SnakeSceneProxy proxy, double arenaSize, double segmentRadius, double foodRadius)
        {
            _proxy = proxy;
            _arenaSize = arenaSize;
            _segmentRadius = segmentRadius;
            _foodRadius = foodRadius;
            _segments = new List<Point3D>();
        }

        public void Start()
        {
            Reset();
            IsRunning = true;
            IsGameOver = false;
        }

        public void Reset()
        {
            _proxy.RemoveAllSnake();
            _segments.Clear();

            for (int i = 0; i < 3; i++)
            {
                Point3D p = new Point3D(0, -i * _step, 0);
                _segments.Add(p);
                _proxy.AddSegment(p.X, p.Y, p.Z, _segmentRadius, i);
            }

            // было _direction = new Vector3D(0,0,1)
            _forward = new Vector3D(0, 1, 0);   // летим вперёд по Y
            _up = new Vector3D(0, 0, 1);   // Z — верх
            _pendingTurn = 0;                  
            SpawnCrystal();
            Score = 0;
            ScoreChanged?.Invoke(this, Score);
            _proxy.Redraw();
        }

        public void TurnLeft() { _pendingTurn = 1; }
        public void TurnRight() { _pendingTurn = 2; }
        public void TurnUp() { _pendingTurn = 3; }
        public void TurnDown() { _pendingTurn = 4; }
       
        public void Update()
        {
            if (!IsRunning || IsGameOver) return;
            if (_segments.Count == 0) { TriggerGameOver(); return; }

            // применяем один поворот на 90°, если был запрос
            switch (_pendingTurn)
            {
                case 1: RotateYaw(1); break;  // лево
                case 2: RotateYaw(-1); break;  // право
                case 3: RotatePitch(1); break;  // вверх
                case 4: RotatePitch(-1); break;  // вниз
            }
            _pendingTurn = 0;

            // передаём ориентацию в C++ для камеры
            _proxy.SetOrientation(
                _forward.X, _forward.Y, _forward.Z,
                _up.X, _up.Y, _up.Z);

            //было _direction, теперь _forward
            Point3D newHead = _segments[0] + _forward * _step;

            //границы куба по всем осям
            if (Math.Abs(newHead.X) > _arenaSize ||
                Math.Abs(newHead.Y) > _arenaSize ||
                Math.Abs(newHead.Z) > _arenaSize)
            {
                TriggerGameOver();
                return;
            }

            //еда
            double distToCrystal = Distance(newHead, _crystalPos);

            // Сумма радиусов: голова космолета + кристалл
            double combinedRadius = _segmentRadius + _foodRadius;

            // Допуск «почти коснулся»: если расстояние меньше суммы радиусов + небольшой запас
            // Запас нужен, чтобы компенсировать «пропуск» кадра (космолет пролетел между кадрами)
            double tolerance = 0.8;

            bool ateFood = false;  // ← ОБЪЯВЛЯЕМ ФЛАГ ЗДЕСЬ

            if (distToCrystal <= combinedRadius + tolerance)
            {
                Score += 10;
                ateFood = true;        // ← просто ставим флаг
                SpawnCrystal();
            }

            // запоминаем хвост до сдвига
            Point3D oldTail = _segments[_segments.Count - 1];

            //сдвиг тела
            //Point3D oldTail = _segments[_segments.Count - 1];
            for (int i = _segments.Count - 1; i > 0; i--)
            {
                _segments[i] = _segments[i - 1];
                _proxy.MoveSegment(i, _segments[i].X, _segments[i].Y, _segments[i].Z);
            }
            _segments[0] = newHead;
            _proxy.SetOrientation(_forward.X, _forward.Y, _forward.Z, _up.X, _up.Y, _up.Z);
            //_proxy.MoveSegment(0, newHead.X, newHead.Y, newHead.Z);
            _proxy.MoveHead(0, newHead.X, newHead.Y, newHead.Z);

            //рост
            if (ateFood)
            {
                _segments.Add(oldTail);
                _proxy.AddSegment(oldTail.X, oldTail.Y, oldTail.Z, _segmentRadius, _segments.Count - 1);
            }

            //пропуск первых 4 сегментов (было 2)
            if (_segments.Count > 5)
            {
                var head = _segments[0];
                for (int i = 4; i < _segments.Count; i++)
                {
                    if (Distance(head, _segments[i]) < 0.1)
                    {
                        TriggerGameOver();
                        return;
                    }
                }
            }

            //камера
            _proxy.FollowHead(0);  
            //_proxy.Redraw();
        }

        private void SpawnCrystal()
        {
            double halfSize = _arenaSize / 2.0;
            double margin = _segmentRadius + 0.5;

            Point3D candidate = new Point3D(0, 0, 0);
            int attempts = 0;
            const int maxAttempts = 50;

            bool collidesWithShip;

            _proxy.RemoveFood();

            do
            {
                double x = (_random.NextDouble() * (_arenaSize - 2 * margin)) - (halfSize - margin);
                double y = (_random.NextDouble() * (_arenaSize - 2 * margin)) - (halfSize - margin);
                double z = (_random.NextDouble() * (_arenaSize - 2 * margin)) - (halfSize - margin);

                candidate = new Point3D(x, y, z);

                collidesWithShip = false;
                foreach (var segment in _segments)
                {
                    double dx = segment.X - candidate.X;
                    double dy = segment.Y - candidate.Y;
                    double dz = segment.Z - candidate.Z;
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (dist < (_segmentRadius + _foodRadius))
                    {
                        collidesWithShip = true;
                        break;
                    }
                }

                attempts++;
            } while (collidesWithShip && attempts < maxAttempts);

            if (attempts >= maxAttempts)
            {
                candidate = new Point3D(0, 0, 0);
            }

            _crystalPos = candidate;
            _proxy.AddCrystal(_crystalPos.X, _crystalPos.Y, _crystalPos.Z, _foodRadius);
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
    }
}
