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


        public static class Ship
        {
            public const int HullId = 5, CockpitId = 6, EngineId = 7;

            public static readonly string[] Paths =
            {
                @"C:\Users\Admin\source\repos\Shake3D\models\Shatl.stp",
                @"C:\Users\Admin\source\repos\Shake3D\models\Cockpit.stp",
                @"C:\Users\Admin\source\repos\Shake3D\models\Engine.stp"
            };

            public static readonly double Scale = 0.65;

            // Подгонка каждой части: dx, dy, dz, yaw, pitch, roll (градусы).
            // Подбирается визуально в режиме обзора — крутите числа, запускайте, смотрите.
            public static readonly double[][] Fit =
            {
                new double[] { 0, 0, 0, 0, 0, -90 },   // корпус — база
                new double[] { 0, 0, 0, 0, 0, -90 },   // кабина — примерно над корпусом
                new double[] { 0, -1.08, 0, 0, 0, -270 },   // двигатель — сзади
            };

            // Цвета: графит, чёрный, плазменный
            public static readonly double[][] Color =
            {
                new double[] { 0.08, 0.09, 0.11 },
                new double[] { 0.03, 0.03, 0.04 },
                new double[] { 0.45, 0.75, 1.00 },
            };

            public static readonly string[] Material = { "STEEL", "SHINY_PLASTIC", "PLASTIC" };

        }
    }
}
