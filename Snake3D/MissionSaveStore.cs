using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Snake3D
{
    // Прогресс одного игрока в миссиях
    public class MissionProgress
    {
        public string Name = "";
        public int Level;
        public int Score;
        public DateTime SavedAt;
    }

    // Один JSON-файл на всех игроков, ключ — имя
    public static class MissionSaveStore
    {
        private static Dictionary<string, MissionProgress> _cache;

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Snake3D");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "missions.json");
            }
        }

        private static Dictionary<string, MissionProgress> Load()
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, MissionProgress>();
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = JsonSerializer.Deserialize<List<MissionProgress>>(
                        File.ReadAllText(FilePath));
                    if (list != null)
                        foreach (var p in list)
                            _cache[p.Name] = p;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Save] не прочитан: {ex.Message}");
            }
            return _cache;
        }

        public static List<MissionProgress> All()
        {
            var list = new List<MissionProgress>(Load().Values);
            list.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
            return list;
        }

        public static void Save(string name, int level, int score)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            var d = Load();
            var p = d.TryGetValue(name, out var old) ? old : new MissionProgress();
            p.Name = name;
            p.Level = Math.Max(level, p.Level);
            p.Score = Math.Max(score, p.Score);
            p.SavedAt = DateTime.UtcNow;
            d[name] = p;

            try
            {
                string json = JsonSerializer.Serialize(d.Values,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[Save] не записан: {ex.Message}");
            }
        }
    }
}
