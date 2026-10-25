using Microsoft.VisualBasic.Devices;
using System.Windows;

namespace Snake3D
{
    public partial class MenuWindow : Window   
    {        
        private void CollectOptions()
        {
            Options.CockpitView = OptCockpit.IsChecked == true;
            Options.TapControl = OptTap.IsChecked == true;
            Options.WallsOn = OptWalls.IsChecked == true;
            Options.Speed = OptSpeed.Value;
            Options.ArenaSize = OptArena.Value;
            Options.GridStep = OptGrid.Value;
            Options.CrystalCount = (int)OptCrystals.Value;
            Options.MinesCount = (int)OptMines.Value;
        }

        // Опции читаются из контролов только в момент «Начать» —
        // до этого пользователь волен дёргать их как угодно
        public GameOptions Options { get; } = new();
        public bool Resumed { get; private set; }
        private readonly bool _pauseMode;
        public MenuWindow(bool pauseMode = false)
        {
            DarkTitle.Apply(this);
            InitializeComponent();

            _pauseMode = pauseMode;           
            
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape || e.IsRepeat) return;
                e.Handled = true;
                if (_pauseMode) { Resumed = true; DialogResult = true; }   // как «Продолжить»
                else Close();                                              // в главном меню — просто закрыть
            };

            OptSpeed.ValueChanged += (s, e) =>
                SpeedLabel.Text = $"{OptSpeed.Value:0} x множитель";
            OptArena.ValueChanged += (s, e) =>
                ArenaLabel.Text = $"{OptArena.Value:0} в стороне";
            OptGrid.ValueChanged += (s, e) =>
                GridLabel.Text = $"клетка {OptGrid.Value:0.0}";
            OptCrystals.ValueChanged += (s, e) =>
                CrystalLabel.Text = $"{OptCrystals.Value:0} кристаллов на поле";
            OptMines.ValueChanged += (s, e) =>
                MineLabel.Text = OptMines.Value < 1
                    ? "без мин"
                    : $"{OptMines.Value:0} мин на поле";
        }

        private void BtnOptions_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = true;
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Audio.Service.StopMusic();
            Audio.Service.StopFlight();
            DialogResult = false;   // ShowMenu увидит false и сам закроет главное окно
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            Resumed = true;
            DialogResult = true;
        }

        private void BtnHunt_Click(object sender, RoutedEventArgs e)
        {
            CollectOptions();
            Options.Mode = GameMode.FreeHunt;
            Audio.Service.StartMusic();
            Audio.Service.PlaySfx("start");
            DialogResult = true; 
        }

        private void BtnMission_Click(object sender, RoutedEventArgs e)
        {
            CollectOptions();
            Options.Mode = GameMode.Mission;
            Audio.Service.StartMusic();
            Audio.Service.PlaySfx("start");
            DialogResult = true;
        }

        private void BtnScores_Click(object sender, RoutedEventArgs e)
        {
            new HighScoresWindow { Owner = this }.ShowDialog();
        }

        private void OptAudio_Changed(object sender, RoutedEventArgs e)
        {
            // Checked срабатывает уже во время InitializeComponent (IsChecked="True"),
            // когда не все контролы ещё созданы — ждём полной загрузки
            if (OptMusic == null || OptSfx == null) return;

            Audio.Service.SetMusicEnabled(OptMusic.IsChecked == true);
            Audio.Service.SetSfxEnabled(OptSfx.IsChecked == true);
        }

        private void OptMusicVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MusicVolLabel != null) MusicVolLabel.Text = ((int)e.NewValue).ToString();
            Audio.Service.SetMusicVolume(e.NewValue / 100.0);
        }

        private void OptSfxVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SfxVolLabel != null) SfxVolLabel.Text = ((int)e.NewValue).ToString();
            Audio.Service.SetSfxVolume(e.NewValue / 100.0);
        }
    }
}