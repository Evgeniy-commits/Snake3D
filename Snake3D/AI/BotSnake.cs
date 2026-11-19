using Snake3DViewer;
using System;
using System.Collections.Generic;

namespace Snake3D
{
    // ═══════════════════════════════════════════════════════════════════
    // ТЕЛО БОТА. Та же математика, что у игрока: сегменты + шаг вперёд
    // по Forward. Мозг отдаёт направление, тело делает шаг и проверяет
    // столкновения. О щитах/бустах/оружии бот не знает — только мир.
    // ═══════════════════════════════════════════════════════════════════
    public class BotSnake
    {
        public readonly string Name;
        public readonly int Id;                     // номер бота: 0, 1, 2...

        // Глобальный id сегмента в прокси: блок 10000+Id*512+local.
        // Игрок занимает 0..N — пересечений нет.
        public int GlobalId(int local)     // ← было private
            => GameConfig.Bot.SegmentIdBase + Id * GameConfig.Bot.IdStride + local;

        private readonly SnakeSceneProxy _proxy;
        private readonly double _segmentRadius;
        private readonly double _foodRadius;
        private readonly double _step;
        private readonly double _arenaSize;
        private readonly int _baseLength;
        private readonly BotBrain _brain = new();

        public List<Point3D> Segments { get; } = new();
        public Vector3D Forward = new(0, 1, 0);

        // Интерполяция — копия схемы игрока: Prev → Next за время тика.
        // Alpha считается один раз в SnakeGame.Render и передаётся сюда.
        public readonly List<Point3D> Prev = new();
        public readonly List<Point3D> Next = new();

        public bool IsAlive;
        public double RespawnTimer;                 // пауза до следующего появления

        //Те же таймеры, что у игрока
        public bool ShieldActive;
        public double ShieldTimer;
        public bool BoostActive;
        public double BoostTimer;

        // Оружие бота
        public int Ammo;
        public double FireCooldown;
        public double Health;   // у бота 3 очка с рождения
        public int Score;
        public double LabInterest = 30.0;   // сек интереса к лабиринту, стартовый запас
        
        // Цвет корабля — случайный при создании, живёт до конца сессии
        public double[] ShipColor;
        // Цвет хвоста бота — ярче корпуса, чтобы откушенные сегменты читались
        public double[] TailColor;

        private static readonly Random _colorRand = new Random();

        // Мозгу нужны габариты — открываем их явно
        public double Step => _step;
        public double SegmentRadius => _segmentRadius;
        public double FoodRadius => _foodRadius;

        public BotSnake(int id, string name, SnakeSceneProxy proxy,
                        double segmentRadius, double foodRadius,
                        double step, double arenaSize, int baseLength)
        {
            Id = id;
            Name = name;
            _proxy = proxy;
            _segmentRadius = segmentRadius;
            _foodRadius = foodRadius;
            _step = step;
            _arenaSize = arenaSize;
            _baseLength = baseLength;
            IsAlive = false;
            var palette = GameConfig.Bot.Colors;
            ShipColor = palette[_colorRand.Next(palette.Length)];
            TailColor = new[]
            {
                Math.Min(1.0, ShipColor[0] * 1.4 + 0.10),
                Math.Min(1.0, ShipColor[1] * 1.4 + 0.10),
                Math.Min(1.0, ShipColor[2] * 1.4 + 0.10),
            };
        }

        // Спавн: голова + хвост, вытянутый назад против курса
        public void Spawn(Point3D head, Vector3D forward)
        {
            Segments.Clear();
            for (int i = 0; i < _baseLength; i++)
                Segments.Add(head + forward * (-_step * i));
            Forward = forward;

            Prev.Clear(); Next.Clear();
            foreach (var s in Segments) { Prev.Add(s); Next.Add(s); }

            IsAlive = true;
            Ammo = 0;
            FireCooldown = 0.0;
            Health = GameConfig.Bot.MaxHealth;
            ShieldActive = false;
            BoostActive = false;
            RegisterInProxy();
            _proxy.SetBotColor(Id, TailColor[0], TailColor[1], TailColor[2]);
        }

        private void RegisterInProxy()
        {
            for (int i = 0; i < Segments.Count; i++)
                _proxy.AddSegment(Segments[i].X, Segments[i].Y, Segments[i].Z,
                                  _segmentRadius, GlobalId(i));

            // Корабль заменяет голову: прячем два первых сегмента,
            // хвост (2+) остаётся видимым — телом служит
            _proxy.HideSegment(GlobalId(0));
            _proxy.HideSegment(GlobalId(1));
        }

        // Смерть: убираем сегменты бота из прокси
        public void HideInProxy()
        {
            for (int i = 0; i < Segments.Count; i++)
                _proxy.RemoveSegment(GlobalId(i));

            // Корабль паркуем за пределами арены до респауна
            _proxy.PlaceBotShip(Id, 0, -10000.0, 0, 0, 1, 0, 0, 0, 1);
        }

        // Откусить хвост с hitIndex до конца.Возвращает число отрезанных.
        // Вызывается только из SnakeGame.ProcessDeferredBites — там уже
        // проверено, что hitIndex попадает в bite-зону
        public int CutTail(int hitIndex)
        {
            int cut = Segments.Count - hitIndex;
            for (int i = hitIndex; i < Segments.Count; i++)
                _proxy.RemoveSegment(GlobalId(i));
            Segments.RemoveRange(hitIndex, cut);
            TrimSnapshot(Prev, hitIndex);
            TrimSnapshot(Next, hitIndex);
            return cut;
        }

        private static void TrimSnapshot(List<Point3D> snap, int from)
        {
            if (snap.Count > from) snap.RemoveRange(from, snap.Count - from);
        }

        // Один логический тик бота — аналог ApplyTurn + Advance + CheckCollisions
        public void Tick(SnakeGame game)
        {
            if (!IsAlive) return;

            if (ShieldActive)
            {
                ShieldTimer -= game.LogicInterval;
                if (ShieldTimer <= 0)
                {
                    ShieldActive = false;
                    _proxy.SetBotShipShield(Id, false, 0.0);
                }
            }
            if (BoostActive)
            {
                BoostTimer -= game.LogicInterval;
                if (BoostTimer <= 0)
                {
                    BoostActive = false;
                    _proxy.SetBotShipBoost(Id, false, 0.0);
                }
            }

            // Интерес к лабиринту: внутри зоны горит вдвое быстрее, вне — медленно
            // восстанавливается. Бот проходит коридор и уходит к еде.
            double dLab = game.LabAxisDistance(Segments[0]);
            if (game.LabOpen && dLab < GameConfig.Labyrinth.RingInnerR * 1.5)
                LabInterest = Math.Max(0.0, LabInterest - game.LogicInterval * 2.0);
            else if (game.LabOpen)
                LabInterest = Math.Min(30.0, LabInterest + game.LogicInterval * 0.5);

            // Буст ускоряет шаг бота на время действия — как у игрока
            double stepNow = BoostActive ? _step * GameConfig.Boost.SpeedMultiplier : _step;

            // Снимок ДО тика — для интерполяции
            Prev.Clear();
            foreach (var s in Segments) Prev.Add(s);

            // 1. Мозг решает, куда лететь
            Forward = _brain.Decide(this, game).Normalized();

            // Зона отчуждения вокруг корабля игрока: бот, подлетевший вплотную,
            // своим хвостом перекрывает кабину. Мягкий руль прочь: вес растёт
            // по мере приближения, телепортации и резких рывков нет
            Point3D myHead = Segments[0];
            Vector3D away = new Vector3D(
                myHead.X - game.PlayerSegments[0].X,
                myHead.Y - game.PlayerSegments[0].Y,
                myHead.Z - game.PlayerSegments[0].Z);
            double dAway = away.Length;
            if (dAway > 1e-6 && dAway < GameConfig.Bot.AvoidRadius)
            {
                away = away * (1.0 / dAway);
                double w = 1.0 - dAway / GameConfig.Bot.AvoidRadius;   // 0..1
                Forward = (Forward + away * (w * 2.0)).Normalized();
            }

            // 2. Шаг: сдвиг тела — в точности как Advance у игрока
            Point3D newHead = Segments[0] + Forward * stepNow;
            Point3D oldTail = Segments[Segments.Count - 1];
            for (int i = Segments.Count - 1; i > 0; i--)
                Segments[i] = Segments[i - 1];
            Segments[0] = newHead;

            // 3. Столкновения
            double half = _arenaSize / 1.7;
            double crashR = stepNow * 0.6;
            bool died = false;

            // стена
            if (Math.Abs(newHead.X) > half ||
                Math.Abs(newHead.Y) > half ||
                Math.Abs(newHead.Z) > half)
                died = true;

            // своё тело (первые 5 — как у игрока)
            if (!died)
                for (int i = 5; i < Segments.Count; i++)
                    if (game.Distance(newHead, Segments[i]) < crashR) { died = true; break; }

            // тело игрока
            if (!died)
            {
                int hit = game.FindContactIndex(game.PlayerSegments, newHead, crashR);
                if (hit >= 0)
                {
                    if (SnakeGame.IsBiteIndex(game.PlayerSegments.Count, hit))
                        game.QueueBiteOnPlayer(this, hit);
                    else if (hit < GameConfig.Bot.ProtectedSegments ||
                             game.PlayerSegments.Count > GameConfig.Bot.MinKeepSegments)                        
                             died = true;
                }
                // Корпус игрока крупнее сегментной сферы: корабль — это
                // слот 0 плюс габарит геометрии. Бот, вошедший в корпус,
                // умирает — иначе пролетает насквозь и перекрывает части
                if (!died &&
                    game.Distance(newHead, game.PlayerSegments[0]) <
                        _segmentRadius + GameConfig.HullRadius)
                    died = true;
            }

            // тела других ботов
            if (!died)
            {
                foreach (var other in game.Bots)
                {
                    if (other == this || !other.IsAlive) continue;
                    int hit = game.FindContactIndex(other.Segments, newHead, crashR);
                    if (hit < 0) continue;

                    if (SnakeGame.IsBiteIndex(other.Segments.Count, hit))
                        game.QueueBite(this, other, hit);
                    else if (hit < GameConfig.Bot.ProtectedSegments ||
                             other.Segments.Count > GameConfig.Bot.MinKeepSegments)
                        died = true;
                    break;
                }
            }

            // мины
            if (!died)
                for (int i = 0; i < game.Mines.Count; i++)
                    if (game.Distance(newHead, game.Mines[i]) <= _segmentRadius + _foodRadius + 0.8)
                    { game.OnBotHitMine(this, i); break; }

            if (died)
            {
                game.KillBot(this, "разбился");
                return;   // Next не обновляем — Render пропустит мёртвого
            }

            // 4. Пикап оружия — бот подбирает наравне с игроком
            var pickups = game.WeaponPickups;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (game.Distance(newHead, pickups[i]) <=
                    _segmentRadius + GameConfig.Weapon.FallbackRadius + 0.8)
                {
                    game.OnBotTookWeapon(this, i);
                    break;
                }
            }

            // Щит — наравне с игроком
            var shields = game.Shields;
            for (int i = 0; i < shields.Count; i++)
            {
                if (game.Distance(newHead, shields[i]) <=
                    _segmentRadius + GameConfig.Shield.FallbackRadius + 0.8)
                {
                    game.OnBotTookShield(this, i);
                    break;
                }
            }

            // Буст — аналогично
            var boosts = game.Boosts;
            for (int i = 0; i < boosts.Count; i++)
            {
                if (game.Distance(newHead, boosts[i]) <=
                    _segmentRadius + GameConfig.Boost.FallbackRadius + 0.8)
                {
                    game.OnBotTookBoost(this, i);
                    break;
                }
            }

            // 5. Еда — только в FreeHunt: в Mission кристаллы — счёт победы,
            // и бот, съев их, сломал бы подсчёт MissionLeft
            if (game.Mode == GameMode.FreeHunt)
            {
                var crystals = game.Crystals;
                for (int i = 0; i < crystals.Count; i++)
                {
                    if (game.Distance(newHead, crystals[i]) <= _segmentRadius + _foodRadius + 0.8)
                    {
                        game.OnBotAteCrystal(i);
                        if (Segments.Count < GameConfig.Bot.MaxLength)   // потолок длины
                        {
                            Segments.Add(oldTail);
                            _proxy.AddSegment(oldTail.X, oldTail.Y, oldTail.Z,
                                              _segmentRadius, GlobalId(Segments.Count - 1));
                            _proxy.SetBotColor(Id, TailColor[0], TailColor[1], TailColor[2]);
                        }
                        break;
                    }
                }
            }

            // 5b. Супер-кристаллы — наравне с игроком
            var supers = game.SuperCrystals;
            for (int i = 0; i < supers.Count; i++)
            {
                if (game.Distance(newHead, supers[i]) <=
                    _segmentRadius + GameConfig.SuperCrystalRadius + 0.8)
                {
                    game.OnBotAteSuperCrystal(i, this);
                    break;
                }
            }

            // 6. Стрельба
            FireCooldown -= game.LogicInterval;
            if (Ammo > 0 && FireCooldown <= 0 && _brain.WantsFire(this, game, out Vector3D aim, out bool atPlayer))
            {
                game.SpawnBotProjectile(this, aim);
                Ammo--;
                FireCooldown = GameConfig.Bot.FireCooldown;

                // Стреляли в игрока — View зажжёт дугу сектора атаки
                if (atPlayer)
                    game.FiredAtPlayer?.Invoke(this.Segments[0]);

                System.Diagnostics.Trace.WriteLine(
                   $"[Fire?] бот {Id} ammo={Ammo} cd={FireCooldown:F2}");
            }

            // Снимок ПОСЛЕ тика
            Next.Clear();
            foreach (var s in Segments) Next.Add(s);
        }

        // Рендер: та же lerp-интерполяция, что у игрока, но по своим спискам
        public void Render(double alpha)
        {
            if (!IsAlive) return;

            for (int i = 0; i < Next.Count; i++)
            {
                Point3D prev = i < Prev.Count ? Prev[i] : Next[i];
                Point3D next = Next[i];
                double x = prev.X + (next.X - prev.X) * alpha;
                double y = prev.Y + (next.Y - prev.Y) * alpha;
                double z = prev.Z + (next.Z - prev.Z) * alpha;

                if (i == 0)
                {
                    // Голова = корабль. Ориентацию не интерполируем (доводим
                    // Forward тиками), позиция — lerp, как у сегментов.
                    Forward = Forward.Normalized();
                    Vector3D helper = Math.Abs(Forward.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0);
                    Vector3D up = (helper - Forward * Vector3D.DotProduct(Forward, helper)).Normalized();
                    _proxy.PlaceBotShip(Id, x, y, z,
                        Forward.X, Forward.Y, Forward.Z, up.X, up.Y, up.Z);
                }
                else
                {
                    _proxy.MoveSegment(GlobalId(i), x, y, z);
                }
            }
        }

    }
}
