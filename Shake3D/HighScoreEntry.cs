using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snake3D
{
    public class HighScoreEntry
    {
        public string Mode { get; set; } = "FreeHunt";   // "FreeHunt" | "Mission"
        public int Score { get; set; }
        public double Seconds { get; set; }
        public string Date { get; set; } = "";           // "2026-10-25 21:40"
    }

    public static class HighScoreStore
    {
        public static List<HighScoreEntry> Load()
        {
            if (!System.IO.File.Exists(FilePath)) return new();
            try
            {
                return System.Text.Json.JsonSerializer
                    .Deserialize<List<HighScoreEntry>>(System.IO.File.ReadAllText(FilePath)) ?? new();
            }
            catch { return new(); }   // битый файл — начинаем с пустой таблицы
        }

        public static void Add(HighScoreEntry entry)
        {
            var top = Load().Append(entry)
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.Seconds)      // при равных очках — быстрее
                .Take(10)
                .ToList();
            System.IO.File.WriteAllText(FilePath,
                System.Text.Json.JsonSerializer.Serialize(top,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private static readonly string FilePath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "highscores.json");
    }
}
