using System;
using System.Collections.Generic;

namespace Snake3D
{
    // ═══════════════════════════════════════════════════════════════════
    // МОЗГ БОТА. Не знает про прокси и рендер — только геометрия.
    // Каждый тик: перебирает 15 направлений (5 рысканий × 3 тангажа),
    // для каждого просчитывает путь на N шагов вперёд и выбирает лучшее.
    // Приоритет: выживание → еда → держаться подальше от стен.
    // ═══════════════════════════════════════════════════════════════════
    public class BotBrain
    {
        private readonly Random _rand = new Random();
        private const double DegToRad = Math.PI / 180;

        public Vector3D Decide(BotSnake bot, SnakeGame game)
        {
            Point3D head = bot.Segments[0];
            Vector3D forward = bot.Forward;

            // Максимум, на сколько бот может довернуть за один тик.
            // Считается из тех же констант, что у игрока, поэтому бот
            // физически не маневреннее человека.
            double maxTurn = GameConfig.Bot.TurnDegPerSecond * DegToRad * game.LogicInterval;

            // Локальный базис: up — любой перпендикуляр к forward (выбираем
            // «не параллельную» ось), right — перпендикуляр к обоим.
            // Через них строим всех кандидатов на поворот.
            Vector3D helper = Math.Abs(forward.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
            Vector3D up = (helper - forward * Vector3D.DotProduct(forward, helper)).Normalized();
            Vector3D right = Vector3D.CrossProduct(forward, up).Normalized();

            // Препятствия собираем ОДИН раз на тик: своё тело (первые 5
            // сегментов пропускаем — они «в моменте» у головы, как у игрока
            // при самопересечении), игрок целиком, другие живые боты.
            var obstacles = new List<Point3D>();
            for (int i = 5; i < bot.Segments.Count; i++) obstacles.Add(bot.Segments[i]);
            foreach (var s in game.PlayerSegments) obstacles.Add(s);
            foreach (var other in game.Bots)
            {
                if (other == bot || !other.IsAlive) continue;
                obstacles.AddRange(other.Segments);
            }

            IReadOnlyList<Point3D> mines = game.Mines;
            IReadOnlyList<Point3D> crystals = game.Crystals;
            IReadOnlyList<Point3D> pickups = game.WeaponPickups;        // пикап оружия

            // ── Фильтрация мира: один раз на тик, а не на каждого кандидата ──
            // Кристаллы: только ближайшие 3 к голове. 1000 → 3 записи,
            // боту этого достаточно для выбора направления.
            var nearCrystals = new List<Point3D>();
            {
                // Три ближайших за один проход, без сортировки всего списка
                double d0 = double.MaxValue, d1 = double.MaxValue, d2 = double.MaxValue;
                Point3D c0 = default, c1 = default, c2 = default;
                foreach (var c in crystals)
                {
                    double d = DistanceSq(head, c);
                    if (d < d0) { d2 = d1; c2 = c1; d1 = d0; c1 = c0; d0 = d; c0 = c; }
                    else if (d < d1) { d2 = d1; c2 = c1; d1 = d; c1 = c; }
                    else if (d < d2) { d2 = d; c2 = c; }
                }
                if (d0 < double.MaxValue) nearCrystals.Add(c0);
                if (d1 < double.MaxValue) nearCrystals.Add(c1);
                if (d2 < double.MaxValue) nearCrystals.Add(c2);
            }

            // Мины: только в радиусе угрозы (LookaheadSteps × шаг × запас).
            // все мины в списке боту не нужны — важны те, что на пути.
            double mineScanR = bot.Step * (GameConfig.Bot.LookaheadSteps + 5);
            double mineScanR2 = mineScanR * mineScanR;
            var nearMines = new List<Point3D>();
            foreach (var m in mines)
                if (DistanceSq(head, m) < mineScanR2) nearMines.Add(m);

            double half = game.ArenaSize / 1.7;                         // та же граница, что в Advance игрока
            double crashR = GameConfig.StepLength * 0.6;                // тот же радиус аварии
            double mineR = bot.SegmentRadius + bot.FoodRadius + 0.8;    // тот же допуск, что у игрока на минах

            Vector3D best = forward;
            double bestScore = double.NegativeInfinity;

            for (int iy = 0; iy < 5; iy++)
            {
                double yaw = (iy - 2) * maxTurn * 0.5;       // -max, -max/2, 0, +max/2, +max
                for (int ip = 0; ip < 3; ip++)
                {
                    double pitch = (ip - 1) * maxTurn * 0.5; // -max/2, 0, +max/2

                    Vector3D dir = RotateAround(forward, up, yaw);
                    dir = RotateAround(dir, right, pitch).Normalized();

                    bool straight = iy == 2 && ip == 1;
                    double s = Evaluate(dir, head, bot.Step, GameConfig.Bot.LookaheadSteps,
                                         half, crashR, mineR, obstacles, nearMines,
                                         nearCrystals, pickups, straight);
                    if (s > bestScore) { bestScore = s; best = dir; }
                }
            }
            return best;
        }

        // Просчёт кандидата: летим по dir до первой блокировки, считаем очки.
        // free — сколько шагов пройдено без аварии; еда меряется от точки,
        // где бот окажется в конце горизонта прогноза.
        private double Evaluate(Vector3D dir, Point3D head, double stepLen, int lookahead,
            double half, double crashR, double mineR,
            List<Point3D> obstacles, IReadOnlyList<Point3D> mines,
            IReadOnlyList<Point3D> crystals, IReadOnlyList<Point3D> pickups, bool isStraight)
        {
            Point3D pos = head;
            int free = 0;
            double crashR2 = crashR * crashR, mineR2 = mineR * mineR;

            for (int k = 1; k <= lookahead; k++)
            {
                pos = pos + dir * stepLen;

                if (Math.Abs(pos.X) > half || Math.Abs(pos.Y) > half || Math.Abs(pos.Z) > half)
                    break;

                bool blocked = false;
                foreach (var o in obstacles)
                {
                    double dx = pos.X - o.X, dy = pos.Y - o.Y, dz = pos.Z - o.Z;
                    if (dx * dx + dy * dy + dz * dz < crashR2) { blocked = true; break; }
                }
                if (!blocked)
                    foreach (var m in mines)
                    {
                        double dx = pos.X - m.X, dy = pos.Y - m.Y, dz = pos.Z - m.Z;
                        if (dx * dx + dy * dy + dz * dz < mineR2) { blocked = true; break; }
                    }

                if (blocked) break;
                free++;
            }

            if (free == 0) return -1e9;         // мгновенная смерть — кандидат мёртв

            // Ближайший кристалл и ближайший пикап от точки прибытия
            double food = double.MaxValue;
            foreach (var c in crystals)
            {
                double dx = pos.X - c.X, dy = pos.Y - c.Y, dz = pos.Z - c.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < food) food = d;
            }
            if (food == double.MaxValue) food = 0;

            double pickupDist = double.MaxValue;
            foreach (var pk in pickups)
            {
                double dx = pos.X - pk.X, dy = pos.Y - pk.Y, dz = pos.Z - pk.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < pickupDist) pickupDist = d;
            }
            if (pickupDist == double.MaxValue) pickupDist = 0;

            // Расстояние от точки прибытия до центра арены — вручную,
            // у Point3D нет метода Length (он есть только у Vector3D)
            double centerDist = Math.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z);

            double score = free * 1000.0        // безопасность важнее всего — вес задавит еду
                         - food * 40.0          // ближе к еде — лучше
                         - pickupDist * 80.0    // пикап оружия вдвое «вкуснее» кристалла
                         - centerDist * 10.0;   // мягкая тяга к центру: не липнем к стенам

            if (isStraight) score += 15.0;      // прямой курс предпочтительнее — меньше дёрганья
            score += _rand.NextDouble() * 8.0;  // капля хаоса: боты не ходят строем
            return score;
        }

        // Поворот Родригеса — та же формула, что в SnakeGame.RotateVector
        private static Vector3D RotateAround(Vector3D v, Vector3D axis, double theta)
        {
            axis = axis.Normalized();
            double cosT = Math.Cos(theta), sinT = Math.Sin(theta);
            var parallel = axis * Vector3D.DotProduct(axis, v);
            var perpendicular = v - parallel;
            var rotatedPerp = perpendicular * cosT + Vector3D.CrossProduct(axis, perpendicular) * sinT;
            return parallel + rotatedPerp;
        }

        private static double DistanceSq(Point3D a, Point3D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        // Решение «стрелять или нет». Приоритет цели: игрок → мина.
        // Стреляем только если цель в конусе ~25° от курса — бот не
        // разворачивается на месте ради выстрела.
        public bool WantsFire(BotSnake bot, SnakeGame game, out Vector3D aimDir)
        {
            aimDir = bot.Forward;
            if (bot.Ammo <= 0 || game.IsGameOver || !game.IsRunning) return false;

            Point3D head = bot.Segments[0];
            Point3D target = default;
            double dist = 0;
            bool haveTarget = false;

            // Приоритет 1: игрок — со «лидом» (упреждением)
            Point3D pH = game.Head;
            double dx = pH.X - head.X, dy = pH.Y - head.Y, dz = pH.Z - head.Z;
            dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist <= GameConfig.Bot.FireRange)
            {
                // Снаряд летит к точке, где игрок ОКАЖЕТСЯ через время полёта:
                // lead = время полёта × скорость игрока (шаг / длительность тика)
                double flight = dist / GameConfig.Weapon.ProjectileSpeed;
                double playerSpeed = GameConfig.StepLength / game.LogicInterval;
                target = new Point3D(
                    pH.X + game.Forward.X * playerSpeed * flight,
                    pH.Y + game.Forward.Y * playerSpeed * flight,
                    pH.Z + game.Forward.Z * playerSpeed * flight);
                haveTarget = true;
            }

            // Приоритет 2: мина на пути — чистим себе дорогу
            if (!haveTarget)
            {
                foreach (var m in game.Mines)
                {
                    double mx = m.X - head.X, my = m.Y - head.Y, mz = m.Z - head.Z;
                    double d = Math.Sqrt(mx * mx + my * my + mz * mz);
                    if (d <= GameConfig.Bot.FireRange)
                    {
                        target = m; dist = d; haveTarget = true;
                        break;
                    }
                }
            }

            if (!haveTarget) return false;

            Vector3D dir = new Vector3D(target.X - head.X, target.Y - head.Y, target.Z - head.Z).Normalized();
            if (Vector3D.DotProduct(bot.Forward, dir) < GameConfig.Bot.AimConeDot) return false;

            aimDir = AddAimError(dir, dist);
            return true;
        }

        // Разброс прицеливания: случайный сдвиг поперёк линии огня,
        // растёт с дистанцией — «стреляет по sensación, не по лазеру»
        private Vector3D AddAimError(Vector3D dir, double dist)
        {
            Vector3D helper = Math.Abs(dir.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
            Vector3D perp = Vector3D.CrossProduct(dir, helper).Normalized();
            double shift = (_rand.NextDouble() * 2 - 1) * GameConfig.Bot.AimError;
            return (dir + perp * (shift / Math.Max(dist, 1e-3))).Normalized();
        }
    }
}

//Бот не «думает» — он перебирает. 15 вариантов × 10 шагов = 150 проверок на тик, это копейки даже для трёх ботов.
//Вес free × 1000 означает: пока есть хоть один безопасный вариант, никакая еда его не перебьёт.
//Так бот не умирает за кристалл, зажатый между стеной и миной.
