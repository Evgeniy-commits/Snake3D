using System;
using System.Collections.Generic;

namespace Snake3D
{
    //Сохраненные цвета одного варианта корабля
    public class ShipSave
    {
        public double[][] Color { get; set; } = Array.Empty<double[]>();
    }

    //Позиции галочек и ползунов в опциях
    public class MenuOption
    {
        public bool Fullscreen { get; set; }
        public bool Music { get; set; } = true;
        public double MusicVolume { get; set; } = 60;
        public bool Sfx { get; set; } = true;
        public double SfxVolume { get; set; } = 80;
        public bool CockpitView { get; set; }
        public bool TapControl { get; set; }
        public bool Walls { get; set; } = true;
        public double Speed { get; set; } = 1.0;
        public int Crystals { get; set; } = 200;
        public int Mines { get; set; } = 100;
        public double Arena { get; set; } = 120;
        public double Grid { get; set; } = 2.5;
    }

    //Корень файла settings.json - перезаписывается при сохранении
    public class AppSettings
    {
        public int SelectedShip {  get; set; }
        public double[] TailColor { get; set; } = { 0.45, 0.95, 0.45 };
        public List<ShipSave> Ships { get; set; } = new();
        public MenuOption Options { get; set; } = new();
    }
}
