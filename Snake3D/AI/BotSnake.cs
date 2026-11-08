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

        // Оружие бота
        public int Ammo;
        public double FireCooldown;

        // Цвет корабля — случайный при создании, живёт до конца сессии
        public double[] ShipColor;

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
        }

        // Глобальный id сегмента в прокси: блок 10000+Id*512+local.
        // Игрок занимает 0..N — пересечений нет.
        private int GlobalId(int local)
            => GameConfig.Bot.SegmentIdBase + Id * GameConfig.Bot.IdStride + local;

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
            RegisterInProxy();
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

        // Один логический тик бота — аналог ApplyTurn + Advance + CheckCollisions
        public void Tick(SnakeGame game)
        {
            if (!IsAlive) return;

            // Снимок ДО тика — для интерполяции
            Prev.Clear();
            foreach (var s in Segments) Prev.Add(s);

            // 1. Мозг решает, куда лететь
            Forward = _brain.Decide(this, game).Normalized();

            // 2. Шаг: сдвиг тела — в точности как Advance у игрока
            Point3D newHead = Segments[0] + Forward * _step;
            Point3D oldTail = Segments[Segments.Count - 1];
            for (int i = Segments.Count - 1; i > 0; i--)
                Segments[i] = Segments[i - 1];
            Segments[0] = newHead;

            // 3. Столкновения
            double half = _arenaSize / 1.7;
            double crashR = _step * 0.6;
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
                foreach (var s in game.PlayerSegments)
                    if (game.Distance(newHead, s) < crashR) { died = true; break; }

                // тела других ботов
                if (!died)
                    foreach (var other in game.Bots)
                    {
                        if (other == this || !other.IsAlive) continue;
                        foreach (var s in other.Segments)
                            if (game.Distance(newHead, s) < crashR) { died = true; break; }
                        if (died) break;
                    }
            }

            // мины
            if (!died)
                for (int i = 0; i < game.Mines.Count; i++)
                    if (game.Distance(newHead, game.Mines[i]) <= _segmentRadius + _foodRadius + 0.8)
                    { died = true; break; }

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
                        }
                        break;
                    }
                }
            }

            // 6. Стрельба
            FireCooldown -= game.LogicInterval;
            if (Ammo > 0 && FireCooldown <= 0 && _brain.WantsFire(this, game, out Vector3D aim))
            {
                game.SpawnBotProjectile(this, aim);
                Ammo--;
                FireCooldown = GameConfig.Bot.FireCooldown;
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
                else if (i == 1)
                {
                    // спрятан под кораблём — позицию в прокси всё равно обновляем,
                    // чтобы хвост (i=2) считал направление вдоль цепочки
                    _proxy.MoveSegment(GlobalId(i), x, y, z);
                }
                else
                {
                    _proxy.MoveSegment(GlobalId(i), x, y, z);
                }
            }
        }

    }
}
