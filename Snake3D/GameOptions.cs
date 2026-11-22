using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Snake3D.GameConfig;

namespace Snake3D
{
    public enum GameMode { FreeHunt, Mission }
    public class GameOptions
    {
        public GameMode Mode;
        public bool CockpitView;
        public bool TapControl;
        public bool WallsOn;
        public bool Fullscreen;
        public double Speed;
        public double ArenaSize;
        public double GridStep;
        public int CrystalCount;
        public int MinesCount;

        public GameDifficulty Difficulty = GameDifficulty.Normal;
        public int BotCount = 3;                 // 0..7
        public double TurnStep = 0.0;            // радиан за тик; 0 = вычислить при первом Reset
                                                 // Параметры миссии (заполняет MissionMenuWindow)
        public bool MissionIsResume;
        public string MissionName = "";
        public int MissionResumeLevel;
        public int MissionResumeScore;
    }
}
