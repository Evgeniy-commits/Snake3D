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

            public static readonly string[] Paths =
            {
                @"models\Shatl.stp",
                @"models\Cockpit.stp",
                @"models\Engine.stp"
            };

            public static readonly double Scale = 0.65;

            // Подгонка каждой части: dx, dy, dz, yaw, pitch, roll (градусы).
            // Подбирается визуально в режиме обзора — крутите числа, запускайте, смотрите.
            public static readonly double[][] Fit =
            {
                new double[] { 0, 0, 0, 0, 0, -90 },        // корпус — база
                new double[] { 0, 0, 0, 0, 0, -90 },        // кабина — примерно над корпусом
                new double[] { 0, -1.08, 0, 0, 0, -270 }    // двигатель — сзади
            };

            // Цвета: графит, чёрный, плазменный
            public static readonly double[][] Color =
            {
                new double[] { 0.08, 0.09, 0.11 },
                new double[] { 0.03, 0.03, 0.04 },
                new double[] { 1.00, 0.20, 0.15 }
            };

            public static readonly string[] Material = { "STEEL", "SHINY_PLASTIC", "SHINY_PLASTIC" };

        }

        public static class Shield
        {
            public const int    ModelId = 9;                    // id модели щита в _models прокси
            public const string Path = @"models\Shield.stp";
            public const double Size = 2.0;                     // габарит модели щита в единицах арены
            public const double FallbackRadius = 1.2;           // радиус для fallback-сферы и ореола
            public const int    TriggerCount = 20;              // съеденных кристаллов до появления щита
            public const double LifeTime = 60.0;                // сколько щит ждёт на арене, сек
            public const double ProtectionTime = 30.0;          // защита от мин после подбора, сек
            public const double ShipHaloRadius = 2.2;           // радиус ореола вокруг корабля
        }
    }
}
