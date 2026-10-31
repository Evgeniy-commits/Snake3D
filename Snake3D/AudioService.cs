using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace Snake3D
{
    // Единственный на всё приложение аудио-сервис:
    // музыка не дублируется при переходах меню ↔ игра
    public static class Audio
    {
        public static AudioService Service { get; } = new AudioService();
    }

    // Музыка: плейлист из audio\music, треки в случайном порядке без повторов подряд.
    // Эффекты: пул из 8 плееров — звуки могут перекрываться (кристалл поверх полёта и т.п.).
    // Полёт: непрерывный гул на отдельном плеере с зацикливанием.
    public sealed class AudioService : IDisposable
    {
        private readonly Random _rnd = new Random();


        private readonly MediaPlayer _music;
        private readonly MediaPlayer _menuMusic;
        private bool _menuWanted;
        private string _menuTrack = "";
        private List<string> _playlist = new List<string>();
        private List<int> _order = new List<int>();
        private int _orderPos = -1;
        private bool _musicWanted;
        private bool _menuThemeOn;
        private bool _playlistWasOn;   // играл ли плейлист до темы меню

        private readonly MediaPlayer _flight;
        private bool _flightWanted;

        private readonly Dictionary<string, string> _sfx =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<MediaPlayer> _sfxPool = new List<MediaPlayer>();
        private int _sfxNext;

        public bool MusicEnabled { get; private set; } = true;

        public bool SfxEnabled { get; private set; } = true;
        public double MusicVolume { get; private set; } = 0.6;
        public double SfxVolume { get; private set; } = 0.8;

        private int _sfxCount;
        private DateTime _lastSfxReport = DateTime.UtcNow;



        public AudioService()
        {
            _music = new MediaPlayer();
            _music.MediaEnded += MusicEnded;

            _flight = new MediaPlayer();
            _flight.MediaEnded += FlightEnded;

            _menuMusic = new MediaPlayer();
            _menuMusic.MediaEnded += MenuEnded;

            for (int i = 0; i < 8; ++i)
                _sfxPool.Add(new MediaPlayer());

            LoadFiles();
        }

        private static string Base => AppDomain.CurrentDomain.BaseDirectory;

        private void LoadFiles()
        {
            string musicDir = Path.Combine(Base, "audio", "music");
            if (Directory.Exists(musicDir))
            {
                string menuFile = Path.Combine(musicDir, "Options.mp3");
                _menuTrack = File.Exists(menuFile) ? menuFile : "";
                // Тема меню в игровой плейлист не попадает — иначе она заиграет
                // и в рейсе, где норовит выскочить между боевыми треками
                _playlist = Directory.GetFiles(musicDir, "*.mp3")
                    .Where(f => !Path.GetFileName(f).StartsWith("menu", StringComparison.OrdinalIgnoreCase))
                    .ToList();                
            }

            string sfxDir = Path.Combine(Base, "audio", "sfx");
            if (Directory.Exists(sfxDir))
            {
                foreach (string ext in new[] { "*.mp3", "*.wav" })
                    foreach (string? f in Directory.GetFiles(sfxDir, ext))
                        _sfx[Path.GetFileNameWithoutExtension(f)] = f;
            }

            EnsureOrder();
        }

        //Случайный порядок
        private void EnsureOrder()
        {
            _order = Enumerable.Range(0, _playlist.Count).ToList();
            for (int i = _order.Count - 1; i > 0; --i)
            {
                int j = _rnd.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            _orderPos = -1;
        }

        private void MusicEnded(object sender, EventArgs e) => PlayNextTrack();

        private void PlayNextTrack()
        {
            if (!MusicEnabled || _playlist.Count == 0) return;
            if (_orderPos + 1 >= _order.Count) EnsureOrder();
            ++_orderPos;

            _music.Open(new Uri(_playlist[_order[_orderPos]]));
            _music.Volume = MusicVolume;
            _music.Play();
        }

        public void StartMusic()
        {
            StopMenuMusic();
            if (!MusicEnabled || _playlist.Count == 0) return;
            _musicWanted = true;
            PlayNextTrack();
        }

        public void PauseMusic()
        {
            if (_musicWanted) _music.Pause();
        }

        public void ResumeMusic()
        {
            if (_musicWanted && MusicEnabled) _music.Play();
        }

        public void StopMusic()
        {
            _musicWanted = false;
            _music.Stop();
        }

        public void SetMusicEnabled(bool on)
        {
            MusicEnabled = on;
            if (!on)
            {
                StopMenuMusic();   // ← тема — тоже музыка, её гасим вместе с плейлистом
                StopMusic();       // если StopMusic здесь уже есть — не дублируйте
            }
        }

        public void SetMusicVolume(double v01)
        {
            MusicVolume = v01;
            _music.Volume = v01;
            _menuMusic.Volume = v01;
        }

        // ── Полёт: постоянный гул, пока идёт игра ──

        private void FlightEnded(object sender, EventArgs e)
        {
            if (_flightWanted && SfxEnabled)
            {
                _flight.Position = TimeSpan.Zero;
                _flight.Play();
            }
        }

        public void StartFlight()
        {
            _flightWanted = true;
            if (!SfxEnabled || !_sfx.TryGetValue("engine", out string f)) return;
            _flight.Open(new Uri(f));
            _flight.Volume = SfxVolume * 0.7;   // гул потише эффектов
            _flight.Position = TimeSpan.Zero;
            _flight.Play();
        }

        public void StopFlight()
        {
            _flightWanted = false;
            _flight.Stop();
        }

        // ── Эффекты ──

        public void SetSfxEnabled(bool on)
        {
            SfxEnabled = on;
            if (!on) StopFlight();
            else if (_flightWanted) StartFlight();
        }

        public void SetSfxVolume(double v01)
        {
            SfxVolume = v01;
            _flight.Volume = v01 * 0.7;
        }

        private readonly HashSet<string> _warmed = new();

        public void PlaySfx(string name)
        {
            if (!SfxEnabled || !_sfx.TryGetValue(name, out string file)) return;

            _sfxCount++;
            if ((DateTime.UtcNow - _lastSfxReport).TotalSeconds >= 1)
            {
                System.Diagnostics.Trace.WriteLine($"sfx за сек: {_sfxCount}");
                _sfxCount = 0;
                _lastSfxReport = DateTime.UtcNow;
            }

            bool cold = _warmed.Add(name);

            var p = _sfxPool[_sfxNext];
            _sfxNext = (_sfxNext + 1) % _sfxPool.Count;

            var w = System.Diagnostics.Stopwatch.StartNew();
            p.Open(new Uri(file));
            p.Volume = SfxVolume;
            p.Position = TimeSpan.Zero;
            p.Play();
            w.Stop();

            if (cold)
                System.Diagnostics.Trace.WriteLine(
                    $"sfx cold-open {name}: {w.Elapsed.TotalMilliseconds:F1} мс");
        }

        //Menu канал
        private void MenuEnded(object sender, EventArgs e)
        {
            if (!_menuWanted || !MusicEnabled || _menuTrack == "") return;
            _menuMusic.Position = TimeSpan.Zero;
            _menuMusic.Play();
        }

        public void StartMenuMusic()
        {
            if (_menuWanted) return;                    // тема уже играет — не начинаем заново
            _playlistWasOn = _musicWanted && MusicEnabled;
            if (_playlistWasOn) _music.Pause();         // плейлист замолкает на время темы
            if (!MusicEnabled || _menuTrack == "") return;
            _menuWanted = true;
            _menuMusic.Open(new Uri(_menuTrack));
            _menuMusic.Volume = MusicVolume;
            _menuMusic.Position = TimeSpan.Zero;
            _menuMusic.Play();
        }

        public void StopMenuMusic()
        {
            if (!_menuWanted) return;
            _menuWanted = false;
            _menuMusic.Stop();
            _menuMusic.Close();
            if (_playlistWasOn && MusicEnabled)         // меню закрылось — плейлист возвращается
            {
                _playlistWasOn = false;
                _music.Play();                          // с того же места, где прервались
            }
        }

        public void Dispose()
        {
            _music.Stop(); _music.Close();
            _flight.Stop(); _flight.Close();
            _menuMusic.Stop(); _menuMusic.Close();
            foreach (var p in _sfxPool) { p.Stop(); p.Close(); }
        }

    }
}
