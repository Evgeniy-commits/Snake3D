using System.Windows;
using System.Windows.Input;

namespace Snake3D
{
    public partial class MissionResumeWindow : Window
    {
        public MissionProgress Chosen;   // не null — играем за этого игрока

        public MissionResumeWindow()
        {
            DarkTitle.Apply(this);
            InitializeComponent();
            foreach (var p in MissionSaveStore.All())
                SavedList.Items.Add($"{p.Name} — миссия {p.Level}, счёт {p.Score}");
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (SavedList.SelectedIndex < 0) return;
            Chosen = MissionSaveStore.All()[SavedList.SelectedIndex];
            DialogResult = true;
        }

        private void SavedList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnLoad_Click(sender, e);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}