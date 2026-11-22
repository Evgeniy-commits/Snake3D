using System.ComponentModel;
using System.Windows;
using static Snake3D.GameConfig;

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

            Options.Difficulty = DiffEasy.IsChecked == true ? GameDifficulty.Easy :
                         DiffHard.IsChecked == true ? GameDifficulty.Hard :
                         DiffSurvival.IsChecked == true ? GameDifficulty.Survival :
                         GameDifficulty.Normal;
            Options.BotCount = (int)OptBots.Value;
            Options.TurnStep = OptTurn.Value;
        }

        // Опции читаются из контролов только в момент «Начать» —
        // до этого пользователь волен дёргать их как угодно
        public GameOptions Options { get; private set; } = new();
        public bool Resumed { get; private set; }
        public bool ExitRequested { get; private set; }

        private readonly bool _pauseMode;
        private bool _allowClose;
        private static int _instances;

        public MenuWindow(bool pauseMode = false, bool fullscreen = false)
        {
            _instances++;
            System.Diagnostics.Trace.WriteLine($"[MenuWindow] создан экземпляр #{_instances} " +
                $"pause={pauseMode}\n{new System.Diagnostics.StackTrace()}");
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
            OptFullscreen.IsChecked = fullscreen || SettingsStore.Current.Options.Fullscreen;

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
            };

            OptArena.ValueChanged += (s, e) =>
            {
                ArenaLabel.Text = $"{OptArena.Value:0} в стороне";                
            };

            OptGrid.ValueChanged += (s, e) =>
            {
                GridLabel.Text = $"клетка {OptGrid.Value:0.0}";               
            };

            OptCrystals.ValueChanged += (s, e) =>
            {
                CrystalLabel.Text = $"{OptCrystals.Value:0} кристаллов на поле";                
            };

            OptMines.ValueChanged += (s, e) =>
            {
                MineLabel.Text = OptMines.Value < 1
                    ? "без мин"
                    : $"{OptMines.Value:0} мин на поле";               
            };

            OptBots.ValueChanged += (s, e) =>
                BotsLabel.Text = OptBots.Value < 1 ? "без ботов" : $"{OptBots.Value:0} ботов";

            OptTurn.ValueChanged += (s, e) =>
                TurnLabel.Text = OptTurn.Value < 0.001
                    ? "поворот: по умолчанию"
                    : $"{OptTurn.Value:F3} рад/тик";

            BtnContinue.Visibility = pauseMode ? Visibility.Visible : Visibility.Collapsed;

            Closed += (s, e) =>
            {
                CollectOptions();        // крестик тоже отдаёт игре то, что на экране
                PushOptionsToStore();
                SettingsStore.Save();
            };

            DiffEasy.Checked += (s, e) => UpdateDifficultyLabel();
            DiffNormal.Checked += (s, e) => UpdateDifficultyLabel();
            DiffHard.Checked += (s, e) => UpdateDifficultyLabel();
            DiffSurvival.Checked += (s, e) => UpdateDifficultyLabel();
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

            OptBots.Value = o.Bots;
            OptTurn.Value = o.TurnStep;

            // Сложность: одна из четырёх радиокнопок
            if (o.Difficulty == GameDifficulty.Easy) DiffEasy.IsChecked = true;
            else if (o.Difficulty == GameDifficulty.Hard) DiffHard.IsChecked = true;
            else if (o.Difficulty == GameDifficulty.Survival) DiffSurvival.IsChecked = true;
            else DiffNormal.IsChecked = true;
            UpdateDifficultyLabel();
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

            o.Bots = (int)OptBots.Value;
            o.TurnStep = OptTurn.Value;
            o.Difficulty = Options.Difficulty;
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
            // Панель разворачивается на месте; повторный клик — свернуть
            PanelMission.Visibility = PanelMission.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
            PanelMissionName.Visibility = Visibility.Collapsed;   // имя спрятано до запроса
            Options.Mode = GameMode.Mission;

            //CollectOptions();
            //Options.Mode = GameMode.Mission;
            //Audio.Service.StartMusic();
            //Audio.Service.PlaySfx("start");
            //_allowClose = true;
            //DialogResult = true;
        }

        private void BtnMissionNew_Click(object sender, RoutedEventArgs e)
        {
            PanelMissionName.Visibility = Visibility.Visible;
            MissionNameBox.Focus();
        }

        private void MissionNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) BtnMissionStart_Click(sender, e);
        }

        private void BtnMissionStart_Click(object sender, RoutedEventArgs e)
        {
            string name = MissionNameBox.Text.Trim();
            if (name.Length == 0) return;   // без имени не стартуем

            Options.MissionIsResume = false;
            Options.MissionName = name;
            Options.MissionResumeLevel = 1;
            Options.MissionResumeScore = 0;
            CloseMissionToGame();
        }

        private void BtnMissionResume_Click(object sender, RoutedEventArgs e)
        {
            // Единственное новое окно во всей цепочке — список сохранений

            var win = new MissionResumeWindow { Owner = this };
            if (win.ShowDialog() == true && win.Chosen != null)
            {
                Options.MissionIsResume = true;
                Options.MissionName = win.Chosen.Name;
                Options.MissionResumeLevel = win.Chosen.Level;
                Options.MissionResumeScore = win.Chosen.Score;
                CloseMissionToGame();
            }
        }
        private void CloseMissionToGame()
        {
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
        }

        private void OptMusicVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MusicVolLabel != null) MusicVolLabel.Text = ((int)e.NewValue).ToString();
            Audio.Service.SetMusicVolume(e.NewValue / 100.0);            
        }

        private void OptSfxVol_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SfxVolLabel != null) SfxVolLabel.Text = ((int)e.NewValue).ToString();            
            Audio.Service.SetSfxVolume(e.NewValue / 100.0);            
        }

        private void OptFullscreen_Changed(object sender, RoutedEventArgs e)
        {}

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

        private void BtnDifficulty_Click(object sender, RoutedEventArgs e)
        {
            DifficultyPopup.IsOpen = true;
            UpdateDifficultyLabel();
        }

        // Подпись на кнопке отражает выбор — видно и без открытия попапа
        private void UpdateDifficultyLabel()
        {
            BtnDifficulty.Content = DiffEasy.IsChecked == true ? "Сложность: легко" :
                                    DiffHard.IsChecked == true ? "Сложность: сложно" :
                                    DiffSurvival.IsChecked == true ? "Сложность: выживание" :
                                    "Сложность: норма";
        }
    }
}