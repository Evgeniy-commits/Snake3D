using Microsoft.VisualBasic.Devices;
using System.Windows;
using System.ComponentModel;

namespace Snake3D
{
    public partial class MenuWindow : Window   
    {        
        private void CollectOptions()
        {
            Options.CockpitView = OptCockpit.IsChecked == true;
            Options.TapControl = OptTap.IsChecked == true;
            Options.WallsOn = OptWalls.IsChecked == true;
            Options.Fullscreen = OptFullscreen.IsChecked == true;
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
        public bool ExitRequested { get; private set; }

        private readonly bool _pauseMode;
        private bool _allowClose;

        public MenuWindow(bool pauseMode = false, bool fullscreen = false)
        {            
            DarkTitle.Apply(this);
            InitializeComponent();

            // Опции по умолчанию = сохранённые: путь «закрыл крестиком»
            // отдаст игре то, что было в прошлый раз, а не нули
            var o = SettingsStore.Current.Options;
            Options = new GameOptions
            {
                Fullscreen = o.Fullscreen,
                CockpitView = o.CockpitView,
                TapControl = o.TapControl,
                WallsOn = o.Walls,
                Speed = o.Speed,
                ArenaSize = o.Arena,
                GridStep = o.Grid,
                CrystalCount = o.Crystals,
                MinesCount = o.Mines,
                Mode = GameMode.FreeHunt        // дефолт режима, меняется кнопками
            };

            LoadOptionsToUi();

            _pauseMode = pauseMode;
            OptFullscreen.IsChecked = fullscreen;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape || e.IsRepeat) return;
                e.Handled = true;
                if (_pauseMode) { Resumed = true; _allowClose = true; DialogResult = true; }   // как «Продолжить»
                else Close();                                              // в главном меню — просто закрыть
            };

            OptSpeed.ValueChanged += (s, e) =>
            {
                SpeedLabel.Text = $"{OptSpeed.Value:0} x множитель";
                PushOptionsToStore();
                SettingsStore.Save();
            };

            OptArena.ValueChanged += (s, e) =>
            {
                ArenaLabel.Text = $"{OptArena.Value:0} в стороне";
                PushOptionsToStore();
                SettingsStore.Save();
            };

            OptGrid.ValueChanged += (s, e) =>
            {
                GridLabel.Text = $"клетка {OptGrid.Value:0.0}";
                PushOptionsToStore();
                SettingsStore.Save();
            };

            OptCrystals.ValueChanged += (s, e) =>
            {
                CrystalLabel.Text = $"{OptCrystals.Value:0} кристаллов на поле";
                PushOptionsToStore();
                SettingsStore.Save();
            };

            OptMines.ValueChanged += (s, e) =>
            {
                MineLabel.Text = OptMines.Value < 1
                    ? "без мин"
                    : $"{OptMines.Value:0} мин на поле";
                PushOptionsToStore();
                SettingsStore.Save();
            };

            BtnContinue.Visibility = pauseMode ? Visibility.Visible : Visibility.Collapsed;

            Closed += (s, e) => { PushOptionsToStore(); SettingsStore.Save(); };
        }
        private void LoadOptionsToUi()
        {
            var o = SettingsStore.Current.Options;
            OptFullscreen.IsChecked = o.Fullscreen;
            OptMusic.IsChecked = o.Music;
            OptMusicVol.Value = o.MusicVolume;
            OptSfx.IsChecked = o.Sfx;
            OptSfxVol.Value = o.SfxVolume;
            OptCockpit.IsChecked = o.CockpitView;
            OptTap.IsChecked = o.TapControl;
            OptWalls.IsChecked = o.Walls;
            OptSpeed.Value = o.Speed;
            OptCrystals.Value = o.Crystals;
            OptMines.Value = o.Mines;
            OptArena.Value = o.Arena;
            OptGrid.Value = o.Grid;
        }
        private void PushOptionsToStore()
        {
            // OptGrid — последний контрол в XAML попапа: пока он null,
            // InitializeComponent ещё не закончился, часть слайдеров не существует
            if (OptGrid == null) return;

            var o = SettingsStore.Current.Options;
            o.Fullscreen = OptFullscreen.IsChecked == true;
            o.Music = OptMusic.IsChecked == true;
            o.MusicVolume = OptMusicVol.Value;
            o.Sfx = OptSfx.IsChecked == true;
            o.SfxVolume = OptSfxVol.Value;
            o.CockpitView = OptCockpit.IsChecked == true;
            o.TapControl = OptTap.IsChecked == true;
            o.Walls = OptWalls.IsChecked == true;
            o.Speed = OptSpeed.Value;
            o.Crystals = (int)OptCrystals.Value;
            o.Mines = (int)OptMines.Value;
            o.Arena = OptArena.Value;
            o.Grid = OptGrid.Value;
        }

        private void BtnOptions_Click(object sender, RoutedEventArgs e)
        {
            OptionsPopup.IsOpen = true;
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            // «Выход» в любом меню = закрыть игру. Никакого ветвления больше нет
            Audio.Service.StopMusic();
            Audio.Service.StopFlight();
            ExitRequested = true;
            DialogResult = false;
            _allowClose = true;
            Close();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            Resumed = true;
            _allowClose = true;
            DialogResult = true;
        }

        private void BtnHunt_Click(object sender, RoutedEventArgs e)
        {
            CollectOptions();
            Options.Mode = GameMode.FreeHunt;
            Audio.Service.StartMusic();
            Audio.Service.PlaySfx("start");
            _allowClose = true;
            DialogResult = true; 
        }

        private void BtnMission_Click(object sender, RoutedEventArgs e)
        {
            CollectOptions();
            Options.Mode = GameMode.Mission;
            Audio.Service.StartMusic();
            Audio.Service.PlaySfx("start");
            _allowClose = true;
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
            PushOptionsToStore();
            SettingsStore.Save();
        }

        private void OptMusicVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MusicVolLabel != null) MusicVolLabel.Text = ((int)e.NewValue).ToString();
            if (OptGrid == null) return;
            Audio.Service.SetMusicVolume(e.NewValue / 100.0);
            PushOptionsToStore();
            SettingsStore.Save();
        }

        private void OptSfxVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SfxVolLabel != null) SfxVolLabel.Text = ((int)e.NewValue).ToString();
            if (OptGrid == null) return;
            Audio.Service.SetSfxVolume(e.NewValue / 100.0);
            PushOptionsToStore();
            SettingsStore.Save();
        }

        private void OptFullscreen_Changed(object sender, RoutedEventArgs e)
        {
            PushOptionsToStore();
            SettingsStore.Save();
        }

        private void BtnCustomizer_Click(object sender, RoutedEventArgs e)
        {
            var win = new CustomizerWindow();
            if (Owner != null)
            {
                win.Width = Owner.ActualWidth > 0 ? Owner.ActualWidth : Owner.Width;
                win.Height = Owner.ActualHeight > 0 ? Owner.ActualHeight : Owner.Height;
                win.Owner = Owner;
            }
            win.ShowDialog();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // в паузе крестик не убивает игру — только прячет панель опций;
            // в главном меню крестик = обычный выход, блокировать нечего
            if (_pauseMode && !_allowClose)
            {
                e.Cancel = true;
                OptionsPopup.IsOpen = false;
                return;
            }
            base.OnClosing(e);
        }
    }
}