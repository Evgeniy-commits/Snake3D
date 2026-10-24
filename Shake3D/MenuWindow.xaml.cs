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

            //if (pauseMode)
            //{
            //    Title = "Пауза";
            //    BtnHunt.Visibility = Visibility.Collapsed;
            //    BtnMission.Visibility = Visibility.Collapsed;
            //    // дальше по вкусам: скрыть OptArena/OptGrid (они требуют рестарта)
            //}
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
            DialogResult = true; 
        }

        private void BtnMission_Click(object sender, RoutedEventArgs e)
        {
            CollectOptions();
            Options.Mode = GameMode.Mission;
            DialogResult = true;
        }

        private void BtnScores_Click(object sender, RoutedEventArgs e)
        {
            new HighScoresWindow { Owner = this }.ShowDialog();
        }
    }
}