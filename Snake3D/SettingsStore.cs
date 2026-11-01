using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Snake3D
{
    // Настройки живут в %AppData%\Snake3D\settings.json —
    // место не зависит от того, откуда и как запущен exe
    public static class SettingsStore
    {
        // Хранилище: Документы\Snake3D\settings.json — та же папка, что и рекорды
        private static readonly string StoreDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Snake3D");

        private static string FilePath => Path.Combine(StoreDir, "settings.json");

        public static AppSettings Current { get; private set; } = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,   // Speed == speed == SPEED
            WriteIndented = true
        };

        //При старте - до создания окон
        //public static void Load()
        //{
        //    Trace.WriteLine($"[Settings] Load: {FilePath}, файл есть: {File.Exists(FilePath)}");
        //    try
        //    {
        //        if (File.Exists(FilePath))
        //        {
        //            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        // битый файл — начинаем с дефолтов, приложение не падает
        //        Trace.WriteLine($"settings.json не читается: {ex.Message}");
        //        Current = new AppSettings();
        //    }
        //    ApplyToConfig();
        //}

        //// Перезаписать файл текущим состоянием
        //public static void Save()
        //{
        //    Trace.WriteLine($"[Settings] Save: {FilePath}");
        //    try
        //    {
        //        Directory.CreateDirectory(StoreDir);
        //        CaptureFromConfig();
        //        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
        //    }
        //    catch (Exception ex)
        //    {
        //        Trace.WriteLine($"settings.json не записался: {ex.Message}");
        //    }
        //}

        public static void Load()
        {
            Trace.WriteLine($"[Settings] Load: {FilePath}, файл есть: {File.Exists(FilePath)}");
            try
            {
                if (!File.Exists(FilePath))
                {
                    Save();   // первый запуск: запишем файл с CaptureFromConfig
                    return;
                }

                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
                if (loaded?.Options == null)
                {
                    Trace.WriteLine("[Settings] в файле нет Options — остались дефолты");
                    return;
                }

                Current = loaded;   // целиком: Options + SelectedShip + TailColor + Ships
                ApplyToConfig();    // ← вот это было потеряно
                Trace.WriteLine($"[Settings] прочитано: арена={Current.Options.Arena} " +
                    $"скорость={Current.Options.Speed} кристаллы={Current.Options.Crystals} " +
                    $"мины={Current.Options.Mines} кораблей={Current.Ships.Count}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Settings] ОШИБКА ЧТЕНИЯ: {ex.Message}");
                Current = new AppSettings();
                ApplyToConfig();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(StoreDir);    // вернул — без него WriteAllText падает на чистой машине
                CaptureFromConfig();                    // ← вот это было потеряно
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Settings] не записался: {ex.Message}");
            }
            Trace.WriteLine($"[Settings] Save: {FilePath}");
        }

        //конфиг -> хранилище (перед записью)
        private static void CaptureFromConfig()
        {
            Current.SelectedShip = GameConfig.Ship.Selected;
            Current.TailColor = new[]
            {
                GameConfig.Ship.TailColor[0],
                GameConfig.Ship.TailColor[1],
                GameConfig.Ship.TailColor[2]
            };
            Current.Ships.Clear();
            foreach (var v in GameConfig.Ship.Variants)
                Current.Ships.Add(new ShipSave { Color = v.Color });
        }

        //хранилище -> конфиг (после загрузки)
        private static void ApplyToConfig()
        {
            if (Current.SelectedShip >= 0 &&
                Current.SelectedShip < GameConfig.Ship.Variants.Length)
                GameConfig.Ship.Selected = Current.SelectedShip;

            if (Current.TailColor != null && Current.TailColor.Length == 3)
                GameConfig.Ship.TailColor = Current.TailColor;

            for (int i = 0; i < Current.Ships.Count &&
                            i < GameConfig.Ship.Variants.Length; i++)
            {
                var saved = Current.Ships[i].Color;
                var live = GameConfig.Ship.Variants[i].Color;
                if (saved == null || live == null) continue;
                for (int p = 0; p < saved.Length && p < live.Length; p++)
                    if (saved[p] != null && saved[p].Length == 3)
                        live[p] = saved[p];
            }
        }
    }
}
