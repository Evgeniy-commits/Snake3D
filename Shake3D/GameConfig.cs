using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snake3D
{
    // Единственный источник констант геометрии игры.
    // значение, используемое больше чем в одном файле,
    // живёт здесь. Локальные копии значений запрещены — только ссылка.
    public static class GameConfig
    {
        public const double DefaultArenaSize = 100.0;
        public const double DefaultGridStep = 5.0;
        public const double SegmentRadius = 0.5;
        public const double FoodRadius = 0.8;
        public const double BaseLogicInterval = 0.05;
        public const int DefaultCrystalCount = 3;
        public const int MaxCrystalCount = 1000;
        public const int DefaultMineCount = 10;
        public const int MaxMineCount = 1000;
        public const double BaseCellsPerSecond = 12.5;
        public const double StepLength = 1.0;
        public const double BaseStepsPerSecond = 15.0;
        public const int MinesPerCristal = 3;


        public static class Ship
        {
            public const int HullId = 5, CockpitId = 6, EngineId = 7;

            public class Variant
            {
                public string Name = "";
                public string[] Paths = Array.Empty<string>();
                public double Scale = 1.0;
                public double[][] Fit = Array.Empty<double[]>();      // dx,dy,dz, yaw,pitch,roll
                public double[][] Color = Array.Empty<double[]>();    // r,g,b на часть
                public string[] Material = Array.Empty<string>();
            }

            public static readonly Variant[] Variants =
            {
                new Variant
                {
                    Name = "Шаттл Mодель-0",
                    Paths = new[]
                    {
                        @"models\Shuttle_0\Shuttle_0.stp",
                        @"models\Shuttle_0\Cockpit_0.stp",
                        @"models\Shuttle_0\Engine_0.stp",
                    },
                    Scale = 0.65,
                    Fit = new[]
                    {
                        new double[] { 0, 0, 0, 0, 0, -90 },
                        new double[] { 0, 0, 0, 0, 0, -90 },
                        new double[] { 0, -1.08, 0, 0, 0, -270 },
                    },
                    Color = new[]
                    {
                        new double[] { 0.08, 0.09, 0.11 },
                        new double[] { 0.03, 0.03, 0.04 },
                        new double[] { 1.00, 0.20, 0.15 },
                    },
                    Material = new[] { "STEEL", "SHINY_PLASTIC", "SHINY_PLASTIC" },
                },
                new Variant
                {
                    Name = "Шаттл Mодель-1",
                    Paths = new[]
                    {
                        @"models\Shuttle_1\Shuttle_1.stp",
                        @"models\Shuttle_1\Cockpit_1.stp",
                        @"models\Shuttle_1\Engine_1.stp",
                    },
                    // У второго набора свои габариты
                    Scale = 0.65,
                    Fit = new[]
                    {
                        new double[] { 0, 0.5, 0, 0, 90, 0 },
                        new double[] { 0, 0.5, 0.005, 0, 90, 0 },      // свои офсеты — подгонка визуально
                        new double[] { 0, 0.5, 0, 0, 90, 0 },
                    },
                    Color = new[]
                    {
                        new double[] { 0.12, 0.14, 0.20 },
                        new double[] { 0.10, 0.60, 0.85 },
                        new double[] { 0.95, 0.75, 0.10 },
                    },
                    Material = new[] { "STEEL", "CHROME", "SHINY_PLASTIC" },
                },
            };

            // Выбранный корабль: индекс в Variants. Меню/кастомайзер пишет сюда,
            // весь остальной код читает только через Cur.
            public static int Selected = 0;
            public static Variant Cur => Variants[Selected];

            // Цвет хвоста — общий для всех кораблей, как и было
            public static double[] TailColor = { 0.45, 0.95, 0.45 };
        }

        public static class Shield
        {
            public const int ModelId = 9;                    // id модели щита в _models прокси
            public const string Path = @"models\Shield.stp";
            public const double Size = 2.0;                     // габарит модели щита в единицах арены
            public const double FallbackRadius = 1.2;           // радиус для fallback-сферы и ореола
            public const int TriggerCount = 20;              // съеденных кристаллов до появления щита
            public const double LifeTime = 60.0;                // сколько щит ждёт на арене, сек
            public const double ProtectionTime = 30.0;          // защита от мин после подбора, сек
            public const double ShipHaloRadius = 2.2;           // радиус ореола вокруг корабля
        }

        public static class Boost
        {
            public const int ModelId = 10;                      // id модели щита в _models прокси
            public const string Path = @"models\Boost.stp";
            public const double Size = 2.0;                     // габарит модели ускорения в единицах арены
            public const double FallbackRadius = 1.4;           // радиус для fallback-сферы и ореола
            public const int TriggerCount = 15;                 // съеденных кристаллов до появления ускорения
            public const double LifeTime = 60.0;                // сколько ускорение ждёт на арене, сек
            public const double BoostTime = 30.0;               // ускорение после подбора, сек
            public const double SpeedMultiplier = 1.5;          // множитель скорости
            public const double ShipHaloRadius = 2.5;           // радиус ореола вокруг корабля
        }
    }
}
