using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snake3D
{
    public enum GameMode { FreeHunt, Mission }
    public class GameOptions
    {
        public GameMode Mode;
        public bool CockpitView;
        public bool TapControl;
        public bool WallsOn;
        public double Speed;
        public double ArenaSize;
        public double GridStep;
        public int CrystalCount;
        public int MinesCount;
    }
}
