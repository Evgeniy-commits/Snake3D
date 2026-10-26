using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Snake3D
{
    public class HighScoreEntry
    {
        public string Mode { get; set; } = "FreeHunt";   // "FreeHunt" | "Mission"
        public int Score { get; set; }
        public double Seconds { get; set; }                 // целые секунды — для сортировки
        public string Time { get; set; } = "";
        public string Date { get; set; } = "";           // "2026-10-25 21:40"
    }

    public partial class HighScoresWindow : Window
    {
        // Хранилище: Документы\Snake3D\highscores.json — писать можно всегда, без прав админа
        private static readonly string StoreDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Snake3D");

        private static string FilePath => Path.Combine(StoreDir, "highscores.json");

        public HighScoresWindow()
        {
            DarkTitle.Apply(this);
            InitializeComponent();
            Tab_Checked(null, null!);   // заполнить таблицу сразу при открытии
        }

        private void Tab_Checked(object? sender, RoutedEventArgs e)
        {
            if (Grid == null) return;   // InitializeComponent ещё не отработал — защита

            string mode = TabHunt?.IsChecked == true ? "FreeHunt" : "Mission";
            Grid.ItemsSource = Load()
                .Where(r => r.Mode == mode)
                .Select(r => new
                {
                    r.Score,
                    TimeText = TimeSpan.FromSeconds(r.Seconds).ToString(@"mm\:ss"),
                    r.Date
                })
                .ToList();
        }

        // ---------- Хранение ----------

        public static List<HighScoreEntry> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new();

                return JsonSerializer.Deserialize<List<HighScoreEntry>>(
                           File.ReadAllText(FilePath)) ?? new();
            }
            catch
            {
                return new();   // битый файл или сбой чтения — начинаем с пустой таблицы
            }
        }

        public static void Add(HighScoreEntry entry)
        {
            try
            {
                Directory.CreateDirectory(StoreDir);

                // округляем до целых секунд и сразу делаем строку "мм:сс"
                entry.Seconds = (int)Math.Round((double)entry.Seconds);
                entry.Time = TimeSpan.FromSeconds(entry.Seconds).ToString(@"mm\:ss");

                var top = Load()
                    .Append(entry)
                    .OrderByDescending(e => e.Score)
                    .ThenBy(e => e.Seconds)
                    .Take(10)
                    .ToList();

                File.WriteAllText(FilePath, JsonSerializer.Serialize(top,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // не падаем
            }
        }
    }
}