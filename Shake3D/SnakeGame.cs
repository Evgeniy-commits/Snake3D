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
        public static Vector3D operator * (Vector3D v, double s)
            => new Vector3D(v.X * s, v.Y * s, v.Z * s);
        public static double DotProduct(Vector3D a, Vector3D b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    public class SnakeGame
    {
        private readonly SnakeSceneProxy _proxy;
        private List<Point3D> _segments;
        private Point3D _food;
        private Vector3D _direction;
        private double _segmentRadius = 0.4;
        private double _step = 1.0;
        private double _foodRadius = 0.3;
        private double _arenaSize = 10.0;
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

            //// Инициализация головы (обязательно!)
            //_segments.Add(new Point3D(0, 0, 0));

            _direction = new Vector3D(0, 0, 1);
            _step = 1.0;
            Score = 0;
            IsRunning = true;
            IsGameOver = false;

            //// Первоначальное создание сегмента в OCCT
            //_proxy.AddSegment(0, 0, 0, segmentRadius, 0);
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
                Point3D p = new Point3D(-i * _step, 0, 0);
                _segments.Add(p);
                _proxy.AddSegment(p.X, p.Y, p.Z, _segmentRadius, i);
            }

            SpawnFood();
            Score = 0;
            ScoreChanged?.Invoke(this, Score);

            // Первичная подгонка камеры под змею (один раз при ресете)
            _proxy.Redraw();
        }

        public void SetDirection(double dx, double dy, double dz)
        {
            Vector3D newDir = new Vector3D(dx, dy, dz);
            if (Vector3D.DotProduct(newDir, _direction) < 0) return;
            _direction = newDir;
        }

        public void Update()
        {
            if (!IsRunning || IsGameOver) return;
            if (_segments.Count == 0) { TriggerGameOver(); return; }

            // 1. Новая голова
            Point3D newHead = _segments[0] + _direction * _step;

            // 2. Границы
            if (Math.Abs(newHead.X) > _arenaSize ||
                Math.Abs(newHead.Y) > _arenaSize ||
                Math.Abs(newHead.Z) > _arenaSize)
            {
                TriggerGameOver();
                return;
            }

            // 3. Еда
            bool ateFood = false;
            double distToFood = Distance(newHead, _food);
            if (distToFood < _segmentRadius + _foodRadius)
            {
                ateFood = true;
                Score++;
                ScoreChanged?.Invoke(this, Score);
                SpawnFood();
            }

            // 4. Сохраняем старый хвост (нужен для роста)
            Point3D oldTail = _segments[_segments.Count - 1];

            // 5. Сдвигаем все сегменты на новые позиции (длина НЕ меняется)
            for (int i = _segments.Count - 1; i > 0; i--)
            {
                _segments[i] = _segments[i - 1];
                _proxy.MoveSegment(i, _segments[i].X, _segments[i].Y, _segments[i].Z, _segmentRadius);
            }
            _segments[0] = newHead;
            _proxy.MoveSegment(0, newHead.X, newHead.Y, newHead.Z, _segmentRadius);

            // 6. Если съели еду — растём: добавляем новый сегмент на старом хвосте
            if (ateFood)
            {
                _segments.Add(oldTail);
                _proxy.AddSegment(oldTail.X, oldTail.Y, oldTail.Z, _segmentRadius, _segments.Count - 1);
            }
            // Если НЕ съели — ничего не удаляем!
            // Сдвиг уже переместил хвост вперёд, длина списка не изменилась.

            // 7. Самопересечение
            if (_segments.Count > 3)
            {
                var head = _segments[0];
                for (int i = 2; i < _segments.Count; i++)
                {
                    if (Distance(head, _segments[i]) < 0.1)
                    {
                        TriggerGameOver();
                        return;
                    }
                }
            }

            // 8. Перерисовка
            _proxy.Redraw();
            //_proxy.FollowHead(0);
        }

        private bool IsSelfCollision()
        {
            var head = _segments[0];
            for (int i = 1; i < _segments.Count; i++)
            {
                if (Distance(head, _segments[i]) < 0.1)
                    return true;
            }
            return false;
        }

        private void RebuildScene()
        {
            _proxy.ClearScene();
            for (int i = 0; i < _segments.Count; i++)
            {
                _proxy.AddSegment(_segments[i].X, _segments[i].Y, _segments[i].Z, _segmentRadius, i);
            }
            _proxy.AddFood(_food.X, _food.Y, _food.Z, _foodRadius);
        }

        //private void SpawnFood()
        //{
        //    double x = _random.NextDouble() * (_arenaSize * 2) - _arenaSize;
        //    double y = _random.NextDouble() * (_arenaSize * 2) - _arenaSize;
        //    double z = _random.NextDouble() * (_arenaSize * 2) - _arenaSize;

        //    _food = new Point3D(x, y, z);
        //    _proxy.AddFood(x, y, z, _foodRadius);
        //}
        // Исправленный SpawnFood
        private void SpawnFood()
        {
            // Сначала удаляем старую еду из сцены (наш новый метод)
            _proxy.RemoveFood();

            double x = _random.NextDouble() * (_arenaSize * 2) - _arenaSize;
            double y = _random.NextDouble() * (_arenaSize * 2) - _arenaSize;
            double z = _random.NextDouble() * (_arenaSize * 2) - _arenaSize;

            _food = new Point3D(x, y, z);
            _proxy.AddFood(x, y, z, _foodRadius);
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
    }
}
