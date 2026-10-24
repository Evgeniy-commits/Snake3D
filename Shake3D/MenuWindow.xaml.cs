using System.Windows;

namespace Snake3D
{
    public partial class MenuWindow : Window
    {
        // Опции читаются из контролов только в момент «Начать» —
        // до этого пользователь волен дёргать их как угодно
        public GameOptions Options { get; } = new();
        public MenuWindow()
        {
            InitializeComponent();

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

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            Options.CockpitView = OptCockpit.IsChecked == true;
            Options.TapControl = OptTap.IsChecked == true;
            Options.WallsOn = OptWalls.IsChecked == true;
            Options.Speed = OptSpeed.Value;
            Options.ArenaSize = OptArena.Value;
            Options.GridStep = OptGrid.Value;
            Options.CrystalCount = (int)OptCrystals.Value;
            Options.MinesCount = (int)OptMines.Value;
            DialogResult = true;
        }

        private void BtnOptions_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = true;
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;   // ShowMenu увидит false и сам закроет главное окно
        }
    }
}