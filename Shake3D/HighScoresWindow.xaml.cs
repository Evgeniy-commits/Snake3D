using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Snake3D
{
    public partial class HighScoresWindow : Window
    {
        public HighScoresWindow()
        {
            InitializeComponent();
            Tab_Checked(null, null!);   // заполнить таблицу сразу при открытии
        }

        private void Tab_Checked(object? sender, RoutedEventArgs e)
        {
            if (Grid == null) return;   // InitializeComponent ещё не отработал — защита

            string mode = TabHunt?.IsChecked == true ? "FreeHunt" : "Mission";
            Grid.ItemsSource = HighScoreStore.Load()
                .Where(r => r.Mode == mode)
                .Select(r => new
                {
                    r.Score,
                    TimeText = TimeSpan.FromSeconds(r.Seconds).ToString(@"mm\:ss"),
                    r.Date
                })
                .ToList();
        }
    }
}