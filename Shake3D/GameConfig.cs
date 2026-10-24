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
    }
}
