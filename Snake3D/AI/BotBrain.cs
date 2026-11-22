using System;
using System.Collections.Generic;
using static Snake3D.GameConfig;

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
        private double _depotTimer;
        private double _depotRest;   // передышка после отказа от депо
        private int _huntLog;

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

            // Препятствия — только в радиусе прогноза: сегменты дальше
            // lookahead шагов до кандидатов физически не дотянуться
            double obsScanR = bot.Step * (GameConfig.Bot.LookaheadSteps + 1);
            double obsScanR2 = obsScanR * obsScanR;

            var obstacles = new List<Point3D>();

            // Своё тело: только первые 2/3 — кончик хвоста на прогноз
            // не влияет, а перебирать длинную тушу на тик дорого
            int ownObs = bot.Segments.Count * 2 / 3;
            for (int i = 4; i < ownObs; i++)
                if (DistanceSq(head, bot.Segments[i]) < obsScanR2) obstacles.Add(bot.Segments[i]);

            // Тело игрока: только первые 2/3 — хвостовая треть это зона кусания,
            // туда лететь можно (и нужно), а не объезжать
            int playerObs = game.PlayerSegments.Count * 2 / 3;
            for (int i = 0; i < playerObs; i++)
                if (DistanceSq(head, game.PlayerSegments[i]) < obsScanR2)
                    obstacles.Add(game.PlayerSegments[i]);

            // Тела других ботов: то же правило
            foreach (var other in game.Bots)
            {
                if (other == bot || !other.IsAlive) continue;
                int otherObs = other.Segments.Count * 2 / 3;
                for (int i = 0; i < otherObs; i++)
                    if (DistanceSq(head, other.Segments[i]) < obsScanR2)
                        obstacles.Add(other.Segments[i]);
            }

            IReadOnlyList<Point3D> mines = game.Mines;
            IReadOnlyList<Point3D> crystals = game.Crystals;
            IReadOnlyList<Point3D> pickups = game.WeaponPickups;        // пикап оружия
            IReadOnlyList<Point3D> shieldPickups = game.Shields;
            IReadOnlyList<Point3D> boostPickups = game.Boosts;

            // Депо только при критическом здоровье: HP 2 — ещё боеспособен,
            // а щит на арене решает проблему дешевле прогулки к коридору
            bool wantDepot = game.LabOpen && bot.Health <= 1.0;
            if (_depotRest > 0) { _depotRest -= game.LogicInterval; wantDepot = false; }

            Point3D? depotTarget = null;
            if (wantDepot)
            {
                _depotTimer += game.LogicInterval;
                if (_depotTimer > 15.0)     // 15 секунд не зарядился — отпускаем охоту
                {
                    _depotTimer = 0;
                    _depotRest = 10.0;
                }
                else
                {
                    double wpR = bot.Step * 3.0;
                    foreach (var wp in game.LabPath)
                        if (DistanceSq(head, wp) > wpR * wpR) { depotTarget = wp; break; }
                    if (depotTarget == null && game.LabPath.Count > 0)
                        depotTarget = game.LabPath[game.LabPath.Count - 1];
                }
            }
            else
                _depotTimer = 0;


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

            // Ранний выход v3.0: прямой курс свободен от тел — летим прямо.
            // Мины не проверяем: теперь это очковый штраф, а не смерть,
            // Evaluate сам оценит цену пролёта, когда это понадобится
            if (obstacles.Count == 0)
            {
                bool needTurn = false;
                foreach (var c in nearCrystals)
                    if (DistanceSq(head, c) < bot.Step * bot.Step * 25.0) { needTurn = true; break; }
                if (!needTurn && pickups.Count == 0 && shieldPickups.Count == 0 &&
                    boostPickups.Count == 0 && game.SuperCrystals.Count == 0 &&
                    !(game.LabOpen && bot.LabInterest > 0.0))
                    return forward;
            }

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
                                         nearCrystals, pickups, straight, depotTarget,
                                          game.Shields, game.Boosts, game, bot);
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
                IReadOnlyList<Point3D> crystals, IReadOnlyList<Point3D> pickups,
                bool isStraight, Point3D? depotTarget,
                IReadOnlyList<Point3D> shieldList, IReadOnlyList<Point3D> boostList,
                SnakeGame game, BotSnake bot)
        {
            Point3D pos = head;
            Point3D safe = head;
            int free = 0;
            double minePenalty = 0.0;               // штраф за мины на пути
            double crashR2 = crashR * crashR, mineR2 = mineR * mineR;

            // Миссия: пассивные боты (угроза 0) не охотятся и не «подставляются».
            // Объявлена в начале метода, чтобы оба блока ниже — охота и агрессия —
            // видели флаг независимо друг от друга
            bool missionPassive = game.Mode == GameMode.Mission &&
                                  GameConfig.Mission.BotThreat == 0;

            // ── Прогноз полёта: до первой блокировки ──
            for (int k = 1; k <= lookahead; k++)
            {
                pos = pos + dir * stepLen;

                if (Math.Abs(pos.X) > half || Math.Abs(pos.Y) > half || Math.Abs(pos.Z) > half)
                { pos = safe; break; }      // за стеной не оцениваем — откат к безопасной точке

                // Бюджет: не больше 40 проверок препятствий на шаг.
                // Без него цикл по 60+ сегментам ботов съедает кадр
                bool blocked = false;
                int budget = 20;
                foreach (var o in obstacles)
                {
                    double dx = pos.X - o.X, dy = pos.Y - o.Y, dz = pos.Z - o.Z;
                    if (dx * dx + dy * dy + dz * dz < crashR2) { blocked = true; break; }
                    if (--budget <= 0) break;
                }
                // Мины: не блокируют путь, а стоят очков. Исключение — бот,
                // которому нечем платить: тогда мина для него смертельна
                if (!blocked && mines.Count > 0)
                    foreach (var m in mines)
                    {
                        // дешёвый отсев: мина не на курсе кандидата — расстояние не считаем
                        double rx = m.X - head.X, ry = m.Y - head.Y, rz = m.Z - head.Z;
                        double along = rx * dir.X + ry * dir.Y + rz * dir.Z;
                        if (along <= 0.0) continue;

                        double dx = pos.X - m.X, dy = pos.Y - m.Y, dz = pos.Z - m.Z;
                        if (dx * dx + dy * dy + dz * dz < mineR2)
                        {
                            if (bot.Score <= 50) { blocked = true; break; }   // без денег мина = смерть
                            minePenalty += 50.0;                              // цена подрыва
                        }
                    }

                if (blocked) { pos = safe; break; }
                free++;
                safe = pos;
            }

            if (free == 0) return -1e9;         // мгновенная смерть — кандидат мёртв

            // ── Еда и пикапы: ближайшие от точки прибытия ──
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

            double centerDist = Math.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z);

            double score = free * 1000.0;        // безопасность важнее всего — вес задавит еду

            // Штраф за мины ×3: само подрывание стоит 50 очков по правилам игры,
            // утроенный вес добавляет осторожность без запрета полёта сквозь
            score -= minePenalty * 3.0;

            // ── Охота за хвостом: только вне депо-режима. Раненый бот,
            // летящий за патронами, не должен отвлекаться на охоту ──
            if (!depotTarget.HasValue)
            {
                if (!depotTarget.HasValue && !missionPassive)
                {
                    // Агрессия миссии: множитель 1.0..3.0, растёт с уровнем (4+)
                    double aggro = GameConfig.Mission.BotAggression;
                    double hp = HuntPull(pos, game.PlayerSegments, bot, game);
                    // С 6-й миссии игрок — приоритетная добыча: тяга вдвое сильнее
                    double playerBias = GameConfig.Mission.HuntPlayer ? 2.0 : 1.0;
                    score += hp * 4000.0 * aggro * playerBias;
                    foreach (var other in game.Bots)
                    {
                        if (other == null || !other.IsAlive || other == bot) continue;
                        score += HuntPull(pos, other.Segments, bot, game) * 4000.0 * aggro;
                    }
                }
            }

            // ── Еда: в депо — почти не учитывается, там главное коридор ──
            if (depotTarget.HasValue)
            {
                double ddx = pos.X - depotTarget.Value.X;
                double ddy = pos.Y - depotTarget.Value.Y;
                double ddz = pos.Z - depotTarget.Value.Z;
                score -= Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz) * 150.0;
                score -= food * 5.0;
            }
            else
            {
                score -= food * 40.0;       // ближе к еде — лучше
                score -= pickupDist * 80.0; // пикап оружия вдвое «вкуснее» кристалла
            }

            score -= centerDist * 10.0;     // мягкая тяга к центру: не липнем к стенам

            // ── Запас до стены в шагах ──
            double margin = Math.Min(Math.Min(half - Math.Abs(pos.X), half - Math.Abs(pos.Y)),
                                     half - Math.Abs(pos.Z));
            double marginSteps = margin / stepLen;
            if (marginSteps < GameConfig.Bot.LookaheadSteps)
                score -= (GameConfig.Bot.LookaheadSteps - marginSteps) * 30.0;

            if (isStraight) score += 15.0;      // прямой курс предпочтительнее — меньше дёрганья
            score += _rand.NextDouble() * 8.0;  // капля хаоса: боты не ходят строем

            // ── Агрессия: подставиться под прогноз курса врага. Вне депо,
            // вне пассивной миссии ──
            if (!depotTarget.HasValue && !missionPassive)
            {
                double aggro = GameConfig.Mission.BotAggression;
                double aggression = PathThreat(pos, game.Head, game.Forward, stepLen, lookahead) * aggro;
                foreach (var other in game.Bots)
                {
                    if (other == null || !other.IsAlive || other == bot) continue;
                    aggression = Math.Max(aggression,
                        PathThreat(pos, other.Segments[0], other.Forward, stepLen, lookahead) * aggro);
                }
                score += aggression;
            }

            // ── Лабиринтная тяга ──
            if (game.LabOpen && bot.LabInterest > 0.0)
            {
                double f = Math.Min(1.0, bot.LabInterest / 10.0);
                double span = GameConfig.Labyrinth.Spacing * GameConfig.Labyrinth.RingCount;
                double d = AxisDistance(pos, game.LabAxisStart, game.LabAxisDir, span);

                score -= d * 55.0 * f;          // дальняя: коридор виден со всего поля
                double pull = Math.Max(0.0, 1.0 - d / (GameConfig.Labyrinth.RingInnerR * 0.8));
                score += 900.0 * pull * f;      // ближняя: удержание внутри коридора
            }

            // ── Щит: тяга, раненому — сильнее ──
            if (shieldList.Count > 0)
            {
                double d = NearestDist(pos, shieldList);
                double perUnit = bot.Health <= 1.5 ? 70.0 : 45.0;
                score -= d * perUnit;
                if (bot.Health <= 1.5) score += 120.0;
            }

            // ── Бусты и суперы: вне депо ──
            if (!depotTarget.HasValue)
            {
                if (boostList.Count > 0)
                {
                    double d = NearestDist(pos, boostList);
                    score -= d * 50.0;
                }

                var supers = game.SuperCrystals;
                if (supers.Count > 0)
                {
                    double d = NearestDist(pos, supers);
                    score -= d * 120.0;         // супер ценнее кристалла
                }
            }

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
        private static double NearestDist(Point3D from, IReadOnlyList<Point3D> list)
        {
            double best = double.MaxValue;
            foreach (var p in list)
            {
                double d = DistanceSq(from, p);
                if (d < best) best = d;
            }
            return best == double.MaxValue ? 0 : Math.Sqrt(best);
        }

        // Насколько точка pos опасна для летящего по course врага:
        // проекция вперёд по курсу и поперечное отклонение
        private static double PathThreat(Point3D pos, Point3D enemyHead,
            Vector3D enemyFwd, double stepLen, int lookahead)
        {
            Vector3D rel = new Vector3D(pos.X - enemyHead.X, pos.Y - enemyHead.Y, pos.Z - enemyHead.Z);
            double ahead = Vector3D.DotProduct(rel, enemyFwd);
            if (ahead <= 0 || ahead > stepLen * lookahead) return 0;   // враг не долетит или уже прошёл

            Vector3D perp = rel - enemyFwd * ahead;
            double lateral = perp.Length;
            double cutR = stepLen * 2.0;
            if (lateral > cutR) return 0;

            // Чем ближе к курсу и чем раньше враг туда прибудет — тем вкуснее
            return 300.0 * (1.0 - lateral / cutR) * (1.0 - ahead / (stepLen * lookahead));
        }

        // Решение «стрелять или нет». Приоритет цели: игрок → мина.
        // Стреляем только если цель в конусе ~25° от курса — бот не
        // разворачивается на месте ради выстрела.
        public bool WantsFire(BotSnake bot, SnakeGame game, out Vector3D aimDir, out bool atPlayer)
        {
            atPlayer = false;
            aimDir = bot.Forward;
            if (bot.Ammo <= 0 || game.IsGameOver || !game.IsRunning) return false;

            // Миссия: стрелять разрешено только вооружённым ботам (4+).
            // В миссиях 1–3 стволы у ботов не активны вовсе
            if (game.Mode == GameMode.Mission && GameConfig.Mission.BotThreat < 2)
                return false;

            Point3D head = bot.Segments[0];
            Point3D target = default;
            double dist = 0;
            bool haveTarget = false;

            // Приоритет 1а: игрок — с упреждением. Щит игрока: снаряд не берёт,
            // стрелять по нему нет смысла
            Point3D pH = game.Head;

            // Дальность огня растёт с агрессией миссии (миссия 4 — база,
            // дальше +25% за уровень до потолка агрессии 3.0)
            double fireRange = GameConfig.Bot.FireRange * GameConfig.Mission.BotAggression;

            double dx = pH.X - head.X, dy = pH.Y - head.Y, dz = pH.Z - head.Z;
            dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist <= fireRange && !game.ProtectionActive)   // ← имя сверь
            {
                double flight = dist / GameConfig.Weapon.ProjectileSpeed;
                double playerSpeed = GameConfig.StepLength / game.LogicInterval;
                target = new Point3D(
                    pH.X + game.Forward.X * playerSpeed * flight,
                    pH.Y + game.Forward.Y * playerSpeed * flight,
                    pH.Z + game.Forward.Z * playerSpeed * flight);
                haveTarget = true;
                atPlayer = true;
            }

            // Приоритет 1б: вражеские боты без щита — ближайший в конусе.
            // По щитовому боту не стреляем — экономим патроны
            if (!haveTarget)
            {
                double bestD = double.MaxValue;
                foreach (var other in game.Bots)
                {
                    if (other == null || !other.IsAlive || other == bot) continue;
                    if (other.ShieldActive) continue;

                    Point3D oh = other.Segments[0];
                    double ox = oh.X - head.X, oy = oh.Y - head.Y, oz = oh.Z - head.Z;
                    double d = Math.Sqrt(ox * ox + oy * oy + oz * oz);
                    if (d > fireRange || d >= bestD) continue;

                    Vector3D to = new Vector3D(ox, oy, oz).Normalized();
                    if (Vector3D.DotProduct(bot.Forward, to) < GameConfig.Bot.AimConeDot) continue;

                    // упреждение как у игрока, но со скоростью бота
                    double flight = d / GameConfig.Weapon.ProjectileSpeed;
                    double botSpeed = GameConfig.StepLength / game.LogicInterval;  
                    target = new Point3D(
                        oh.X + other.Forward.X * botSpeed * flight,
                        oh.Y + other.Forward.Y * botSpeed * flight,
                        oh.Z + other.Forward.Z * botSpeed * flight);
                    bestD = d; dist = d; haveTarget = true;
                }
            }

            // Приоритет 2: мина на пути. Строго на курсе (лучевой коридор),
            // и только пока боезапас больше половины — не сливаем магазин
            // на штраф в 50 очков
            if (!haveTarget && bot.Ammo > GameConfig.Labyrinth.SpawnAmmo / 2)
            {
                double lookLen = fireRange;
                double tube = bot.SegmentRadius + 2.0;          // узкий коридор курса
                double tube2 = tube * tube;

                foreach (var m in game.Mines)
                {
                    Vector3D rel = new Vector3D(m.X - head.X, m.Y - head.Y, m.Z - head.Z);
                    double along = Vector3D.DotProduct(rel, bot.Forward);
                    if (along <= 0 || along > lookLen) continue;

                    Vector3D lateral = rel - bot.Forward * along;
                    if (lateral.Length < tube)
                    {
                        target = m; dist = along; haveTarget = true;
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

        // Охотничья тяга: укус вблизи + погоня издали. Радиус погони —
        // вся арена: хвост должен быть виден охотнику отовсюду.
        private double HuntPull(Point3D pos, IReadOnlyList<Point3D> victimSegs, BotSnake bot, SnakeGame game)
        {
            // Минимальная добыча — 6 сегментов: у жертвы короче нечего
            // отнять (MinKeepSegments = 5). Было MinKeepSegments — при 5
            // охота стояла мёртво
            if (victimSegs.Count < 6) return 0;

            Point3D tip = victimSegs[victimSegs.Count - 1];
            double d = Math.Sqrt(DistanceSq(pos, tip));      // ← d объявлена ДО первого использования

            double advantage = 1.0 + (bot.Segments.Count - victimSegs.Count) / 20.0;
            if (advantage < 0.4) advantage = 0.4;
            if (victimSegs.Count > 40) advantage += 0.3;   // длинный хвост — приманка

            // Близко: тяга к укусу
            double biteRange = bot.Step * (GameConfig.Bot.LookaheadSteps + 10);
            if (d < biteRange)
                return (1.0 - d / biteRange) * advantage;

            // Погоня: радиус — вся арена. Было 0.5 × арена —
            // хвост за серединой поля бот «не видел»
            double chaseRange = game.ArenaSize;
            if (d >= chaseRange) return 0;

            //if (_huntLog++ % 40 == 0)
            //    System.Diagnostics.Trace.WriteLine(
            //        $"[Hunt] бот {bot.Id} дистанция до хвоста={d:F0} тяга={0.30 * advantage * (1.0 - d / chaseRange):F2}");
            return 0.30 * advantage * (1.0 - d / chaseRange);
        }

        // Разброс прицеливания: случайный сдвиг поперёк линии огня,
        // растёт с дистанцией — «стреляет по sensación, не по лазеру»
        private Vector3D AddAimError(Vector3D dir, double dist)
        {
            Vector3D helper = Math.Abs(dir.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
            Vector3D perp = Vector3D.CrossProduct(dir, helper).Normalized();
            // Разброс прицеливания падает с агрессией: чем выше миссия,
            // тем точнее стрельба. Базовый AimError = 2.0
            double err = GameConfig.Bot.AimError / GameConfig.Mission.BotAggression;
            double shift = (_rand.NextDouble() * 2 - 1) * err;
            return (dir + perp * (shift / Math.Max(dist, 1e-3))).Normalized();
        }

        ////Хелпер////////////////
        // Расстояние от точки до оси коридора (с обрезкой по длине коридора)
        private static double AxisDistance(Point3D pos, Point3D a, Vector3D dir, double span)
        {
            Vector3D rel = new Vector3D(pos.X - a.X, pos.Y - a.Y, pos.Z - a.Z);
            double t = Vector3D.DotProduct(rel, dir);
            if (t < 0) t = 0;
            if (t > span) t = span;
            Vector3D onAxis = new Vector3D(a.X + dir.X * t, a.Y + dir.Y * t, a.Z + dir.Z * t);
            double dx = pos.X - onAxis.X, dy = pos.Y - onAxis.Y, dz = pos.Z - onAxis.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}

//Бот не «думает» — он перебирает. 15 вариантов × 10 шагов = 150 проверок на тик, это копейки даже для трёх ботов.
//Вес free × 1000 означает: пока есть хоть один безопасный вариант, никакая еда его не перебьёт.
//Так бот не умирает за кристалл, зажатый между стеной и миной.
