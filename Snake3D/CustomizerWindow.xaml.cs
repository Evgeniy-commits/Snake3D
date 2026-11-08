using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;            // MouseButtonEventArgs, MouseEventArgs (WPF)
using System.Windows.Media;
using System.Windows.Shapes;           // Ellipse
using System.Windows.Threading;
using Snake3DViewer;
using WF = System.Windows.Forms;       // Forms — только через псевдоним, без импорта

namespace Snake3D
{
    public partial class CustomizerWindow : Window
    {
        private SnakeViewport? _viewport;
        private DispatcherTimer? _timer;
        private double _elapsed;
        private int _selectedPart;        // 0 корпус, 1 кабина, 2 двигатель, 3 хвост
        private const int TailCount = 6;
        private bool _rotating;

        // ─── Пипкер ───
        private double _hue, _sat = 1.0, _val = 1.0;
        private bool _dragging;
        private bool _syncingColor;       // защита от петли: код → поля → TextChanged
        private Ellipse? _specMarker;

        private int _muzzleGun;            // какой ствол редактируем
        private bool _syncingMuzzle;       // защита от петли код → поля

        public CustomizerWindow()
        {
            DarkTitle.Apply(this);
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (_viewport != null) return;
                _viewport = new SnakeViewport();
                _viewport.MouseDown += Viewport_MouseDown;
                _viewport.MouseMove += Viewport_MouseMove;
                _viewport.MouseUp += Viewport_MouseUp;
                _viewport.MouseWheel += Viewport_Wheel;
                Host.Child = _viewport;
                Dispatcher.BeginInvoke(new Action(InitScene), DispatcherPriority.ApplicationIdle);
            };
            Closed += (s, e) =>
            {
                _timer?.Stop();
                SettingsStore.Save();          // страховка: всё, что менялось, — в файл
                Host.Child = null;
                _viewport?.Dispose();
                _viewport = null;
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Close();
            };
        }

        private void InitScene()
        {
            var p = _viewport!.Proxy;
            if (p == null) return;

            p.SetBackgroundColor(10, 10, 16);
            ApplyBackground(p);

            ShipList.Items.Clear();
            foreach (var v in GameConfig.Ship.Variants)
                ShipList.Items.Add(v.Name);
            ShipList.SelectedIndex = GameConfig.Ship.Selected;
            ShipList.SelectionChanged += ShipList_SelectionChanged;

            BuildShip(p);
            BuildTail(p);

            PartsList.Items.Clear();
            PartsList.Items.Add("Корпус");
            PartsList.Items.Add("Кабина");
            PartsList.Items.Add("Двигатель");
            PartsList.Items.Add("Хвост");
            PartsList.Items.Add("Пушки");
            PartsList.Items.Add("Снаряд");
            PartsList.SelectedIndex = 0;

            InitMarkers();
            ShowCurrentColor();

            p.FitScene();
            p.FitShipClose(4.0, 0.75);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (s, e) => Animate();
            _timer.Start();
        }

        // Сборка корабля из текущего варианта: загрузка, подгонка, окраска
        private void BuildShip(Snake3DViewer.SnakeSceneProxy p)
        {
            var ship = GameConfig.Ship.Cur;

            for (int i = 0; i < ship.Paths.Length; i++)
            {
                int id = GameConfig.Ship.HullId + i;
                p.LoadShipPart(ship.Paths[i], id, ship.Scale);
            }
            // Центрирование — один раз, после загрузки всех частей:
            // метод считает bbox по уже существующим моделям
            p.CenterShipAssembly(GameConfig.Ship.HullId, ship.Paths.Length);

            for (int i = 0; i < ship.Paths.Length; i++)
            {
                int id = GameConfig.Ship.HullId + i;
                var f = ship.Fit[i];
                p.SetPartPlacement(id, f[0], f[1], f[2], f[3], f[4], f[5]);
                p.AddShipPart(id);
                var c = ship.Color[i];
                p.SetPartColor(id, c[0], c[1], c[2], ship.Material[i]);
            }

            // Пушки: ПОСЛЕ CenterShipAssembly — их bbox не должен попасть
            // в общий центр сборки, иначе корабль поедет
            for (int i = 0; i < ship.GunPaths.Length; i++)
            {
                int id = GameConfig.Weapon.GunIdBase + i;
                if (!p.LoadShipPart(ship.GunPaths[i], id, ship.GunScale))
                {
                    System.Diagnostics.Trace.WriteLine($"Пушка {ship.GunPaths[i]} не загрузилась — пропускаем");
                    continue;
                }
                var f = ship.GunFit[i];
                p.SetPartPlacement(id, f[0], f[1], f[2], f[3], f[4], f[5]);
                p.AddShipPart(id);
                var c = ship.GunColor[i];
                p.SetPartColor(id, c[0], c[1], c[2], ship.GunMaterial[i]);

                var m = ship.MuzzleOffset[i];                    
                p.SetMuzzleOffset(id, m[0], m[1], m[2]);
            }
            PlaceProjectiles(p);
        }

        // Снаряды в точках вылета: позиции берём у самого прокси через GetMuzzleWorld —
        // ровно тот же расчёт, что в Fire(), поэтому превью совпадает с игрой 1:1
        private void PlaceProjectiles(Snake3DViewer.SnakeSceneProxy p)
        {
            var ship = GameConfig.Ship.Cur;

            var pc = GameConfig.Weapon.ProjectileColor;
            p.SetProjectileColor(pc[0], pc[1], pc[2]);

            // масштаб как в SnakeGame: ProjectileSize * 6; без файла прокси даст болт
            p.LoadStepModel(GameConfig.Weapon.ProjectileModelPath,
                            GameConfig.Weapon.ProjectileModelId,
                            GameConfig.Weapon.ProjectileVisualScale);

            // корабль в кастомайзере стоит в нуле, курсом по +Y, верхом по +Z —
            // та же ориентация, что после Reset в игре
            p.PlaceShip(0, 0, 0, 0, 1, 0, 0, 0, 1, 0);

            p.ClearProjectiles();
            for (int i = 0; i < ship.GunPaths.Length; i++)
            {
                int gunId = GameConfig.Weapon.GunIdBase + i;
                double mx = 0, my = 0, mz = 0;
                bool ok = p.GetMuzzleWorld(gunId,
                    0, 0, 0,                      // позиция головы/корабля
                    0, 1, 0,                      // forward
                    0, 0, 1,                      // up
                    ref mx, ref my, ref mz);

                System.Diagnostics.Trace.WriteLine(
                            $"[Muzzle] id={gunId} ok={ok} world=({mx:F2}, {my:F2}, {mz:F2})");

                if (!ok) continue;                // ствол не загрузился или офсет не задан

                double dx = 0, dy = 1, dz = 0;
                p.GetMuzzleDir(gunId, 0, 0, 0, 0, 1, 0, 0, 0, 1, ref dx, ref dy, ref dz);

                p.AddProjectileAt(i, mx, my, mz, 0, 1, 0, GameConfig.Weapon.ProjectileSize);
                p.MoveProjectile(i, mx, my, mz);
            }
        }

        private void RefreshMuzzlePanel()
        {
            bool show = _selectedPart == 4;
            MuzzleHeader.Visibility = MuzzlePanel.Visibility =
                show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            var ship = GameConfig.Ship.Cur;
            _syncingMuzzle = true;   // гасим и SelectionChanged, и TextChanged

            GunPick.Items.Clear();
            for (int i = 0; i < ship.GunPaths.Length; i++)
                GunPick.Items.Add(ship.GunPaths.Length == 1 ? "Ствол" : $"Ствол {i + 1}");
            if (_muzzleGun >= ship.GunPaths.Length) _muzzleGun = 0;
            GunPick.SelectedIndex = _muzzleGun;

            var m = ship.MuzzleOffset[_muzzleGun];
            MuzX.Text = m[0].ToString("0.###");
            MuzY.Text = m[1].ToString("0.###");
            MuzZ.Text = m[2].ToString("0.###");
            _syncingMuzzle = false;
        }

        private void GunPick_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingMuzzle || GunPick.SelectedIndex < 0) return;
            _muzzleGun = GunPick.SelectedIndex;
            RefreshMuzzlePanel();
        }             

        private void MuzBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingMuzzle || _viewport?.Proxy == null) return;
            var ship = GameConfig.Ship.Cur;
            if (_muzzleGun >= ship.MuzzleOffset.Length) return;

            var m = ship.MuzzleOffset[_muzzleGun];
            if (double.TryParse(MuzX.Text, System.Globalization.CultureInfo.InvariantCulture, out var x)) m[0] = x;
            if (double.TryParse(MuzY.Text, System.Globalization.CultureInfo.InvariantCulture, out var y)) m[0] = y;
            if (double.TryParse(MuzZ.Text, System.Globalization.CultureInfo.InvariantCulture, out var z)) m[0] = z;
            //if (double.TryParse(MuzX.Text, out var x)) m[0] = x;
            //if (double.TryParse(MuzY.Text, out var y)) m[1] = y;
            //if (double.TryParse(MuzZ.Text, out var z)) m[2] = z;

            int id = GameConfig.Weapon.GunIdBase + _muzzleGun;
            _viewport.Proxy.SetMuzzleOffset(id, m[0], m[1], m[2]);
            PlaceProjectiles(_viewport.Proxy);
            SettingsStore.Save();
        }

        // Постройка хвоста — отдельный метод, чтобы вызывать и при старте,
        // и после смены варианта (ClearScene его сносит)
        private void BuildTail(Snake3DViewer.SnakeSceneProxy p)
        {
            p.LoadStepModel(@"models\Tail.stp", 1, GameConfig.SegmentRadius * 1.5);
            var t = GameConfig.Ship.TailColor;
            p.SetTailColor(t[0], t[1], t[2]);
            for (int i = 0; i <= TailCount; i++)
                p.AddSegment(0, -i * GameConfig.StepLength, 0, GameConfig.SegmentRadius, i);
        }

        private void ShipList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShipList.SelectedIndex < 0 || _viewport?.Proxy == null) return;
            if (GameConfig.Ship.Selected == ShipList.SelectedIndex) return;

            GameConfig.Ship.Selected = ShipList.SelectedIndex;
            RebuildScene(_viewport.Proxy);
            SettingsStore.Save();
            ShowCurrentColor();   // ползунки и превью — под новый корабль
            _muzzleGun = 0;   // при смене корабля число стволов может быть другим
            RefreshMuzzlePanel();
        }

        // Полная пересборка сцены: как Reset в игре, но с фоном кастомайзера
        private void RebuildScene(Snake3DViewer.SnakeSceneProxy p)
        {
            p.ClearScene();
            ApplyBackground(p);
            BuildShip(p);
            BuildTail(p);
            p.FitScene();
            p.FitShipClose(4.0, 0.75);
        }

        // Корабль стоит на месте, но живёт: двигатель пульсирует, звенья хвоста
        // проворачиваются через один — как в полёте
        private void Animate()
        {
            var p = _viewport?.Proxy;
            if (p == null) return;
            _elapsed += 0.016;
            p.UpdateSpin(0.016);
            p.PlaceShip(0, 0, 0, 0, 1, 0, 0, 0, 1, _elapsed);
            for (int i = 1; i <= TailCount; i++)
                p.MoveSegment(i, 0, -i * GameConfig.StepLength, 0);
            p.Redraw();
        }

        // Фон: картинка ангара, если есть; иначе — звёзды, как в игре
        private void ApplyBackground(Snake3DViewer.SnakeSceneProxy p)
        {
            string bg = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"image\hangari.jpg");
            if (System.IO.File.Exists(bg))
                p.SetBackgroundImage(bg);
            else
                p.CreateStarSky(600, 240.0);
        }

        //Пипкер: HSV → RGB

        private static void HsvToRgb(double h, double s, double v,
                                     out double r, out double g, out double b)
        {
            h %= 360; if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            double m = v - c;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            r += m; g += m; b += m;
        }

        private static void RgbToHsv(double r, double g, double b,
                                     out double h, out double s, out double v)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }

        private void InitMarkers()
        {
            _specMarker = new Ellipse
            {
                Width = 10,
                Height = 10,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 1.5,
                Fill = System.Windows.Media.Brushes.Transparent,
                IsHitTestVisible = false
            };
            SpectrumMarkers.Children.Add(_specMarker);
        }

        //квадрат тон/насыщенность
        private void Spectrum_MouseDown(object sender, MouseButtonEventArgs e)
        { _dragging = true; Spectrum.CaptureMouse(); PickSpectrum(e); }
        private void Spectrum_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        { if (_dragging) PickSpectrum(e); }
        private void Spectrum_MouseUp(object sender, MouseButtonEventArgs e)
        { _dragging = false; Spectrum.ReleaseMouseCapture(); }

        private void PickSpectrum(System.Windows.Input.MouseEventArgs e)
        {
            var pt = e.GetPosition(Spectrum);
            _hue = Math.Clamp(pt.X / Math.Max(Spectrum.ActualWidth, 1) * 360, 0, 359.9);
            _sat = Math.Clamp(pt.Y / Math.Max(Spectrum.ActualHeight, 1), 0, 1);
            SyncFromHsv(apply: true);
        }

        //полоса яркости
        private void Bar_MouseDown(object sender, MouseButtonEventArgs e)
        { _dragging = true; BrightnessBar.CaptureMouse(); PickBar(e); }
        private void Bar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        { if (_dragging) PickBar(e); }
        private void Bar_MouseUp(object sender, MouseButtonEventArgs e)
        { _dragging = false; BrightnessBar.ReleaseMouseCapture(); }

        private void PickBar(System.Windows.Input.MouseEventArgs e)
        {
            var pt = e.GetPosition(BrightnessBar);
            _val = Math.Clamp(1 - pt.Y / Math.Max(BrightnessBar.ActualHeight, 1), 0, 1);
            SyncFromHsv(apply: true);
        }

        //единая точка: HSV → превью, поля, маркеры, сцена
        private void SyncFromHsv(bool apply)
        {
            HsvToRgb(_hue, _sat, _val, out var r, out var g, out var b);

            ColorPreview.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
                (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)));

            _syncingColor = true;
            ColR.Text = ((int)Math.Round(r * 255)).ToString();
            ColG.Text = ((int)Math.Round(g * 255)).ToString();
            ColB.Text = ((int)Math.Round(b * 255)).ToString();
            _syncingColor = false;

            if (_specMarker != null && Spectrum.ActualWidth > 0)
            {
                Canvas.SetLeft(_specMarker, _hue / 360 * Spectrum.ActualWidth - 5);
                Canvas.SetTop(_specMarker, _sat * Spectrum.ActualHeight - 5);
            }
            BarMarker.Margin = new Thickness(0,
                (1 - _val) * Math.Max(BrightnessBar.ActualHeight - 6, 0), 0, 0);

            if (apply)
                ApplyColorToSelection(r, g, b);
        }

        //ручной ввод RGB
        private void ColBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingColor || ColR == null || ColG == null || ColB == null) return;
            if (!byte.TryParse(ColR.Text, out var rr) ||
                !byte.TryParse(ColG.Text, out var gg) ||
                !byte.TryParse(ColB.Text, out var bb)) return;

            RgbToHsv(rr / 255.0, gg / 255.0, bb / 255.0, out _hue, out _sat, out _val);
            SyncFromHsv(apply: true);
        }

        //смена детали: пипкер показывает её текущий цвет
        private void PartsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedPart = PartsList.SelectedIndex < 0 ? 0 : PartsList.SelectedIndex;
            ShowCurrentColor();
            RefreshMuzzlePanel();
        }

        private void ShowCurrentColor()
        {
            double r, g, b;
            if (_selectedPart == 3)          // хвост — общий для всех кораблей
            {
                var t = GameConfig.Ship.TailColor; r = t[0]; g = t[1]; b = t[2];
            }
            else if (_selectedPart == 4)     // пушки: обе красятся одним цветом
            {
                var c = GameConfig.Ship.Cur.GunColor[0]; r = c[0]; g = c[1]; b = c[2];
            }
            else if (_selectedPart == 5)     // снаряд — свойство оружия, не корабля
            {
                var t = GameConfig.Weapon.ProjectileColor; r = t[0]; g = t[1]; b = t[2];
            }
            else
            {
                var c = GameConfig.Ship.Cur.Color[_selectedPart];
                r = c[0]; g = c[1]; b = c[2];
            }
            RgbToHsv(r, g, b, out _hue, out _sat, out _val);
            SyncFromHsv(apply: false);
        }

        //покраска выбранного элемента — общий путь для мыши и полей
        private void ApplyColorToSelection(double r, double g, double b)
        {
            var p = _viewport?.Proxy;
            if (p == null) return;

            if (_selectedPart == 3)   // хвост — вся цепочка и запоминание
            {
                GameConfig.Ship.TailColor[0] = r;
                GameConfig.Ship.TailColor[1] = g;
                GameConfig.Ship.TailColor[2] = b;
                p.SetTailColor(r, g, b);
                for (int i = 1; i <= TailCount; i++)
                    p.SetSegmentColor(i, r, g, b);
            }
            else if (_selectedPart == 4)   // пушки: обе ствола сразу
            {
                var ship = GameConfig.Ship.Cur;
                for (int i = 0; i < ship.GunPaths.Length; i++)
                {
                    ship.GunColor[i][0] = r;
                    ship.GunColor[i][1] = g;
                    ship.GunColor[i][2] = b;
                    p.SetPartColor(GameConfig.Weapon.GunIdBase + i, r, g, b, ship.GunMaterial[i]);
                }
            }
            else if (_selectedPart == 5)   // снаряд: цвет плазмы для всех кораблей
            {
                GameConfig.Weapon.ProjectileColor[0] = r;
                GameConfig.Weapon.ProjectileColor[1] = g;
                GameConfig.Weapon.ProjectileColor[2] = b;
                p.SetProjectileColor(r, g, b);
            }
            else
            {
                int id = GameConfig.Ship.HullId + _selectedPart;
                var ship = GameConfig.Ship.Cur;
                p.SetPartColor(id, r, g, b, ship.Material[_selectedPart]);
                ship.Color[_selectedPart][0] = r;
                ship.Color[_selectedPart][1] = g;
                ship.Color[_selectedPart][2] = b;
            }
            //SettingsStore.Save();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // Орбитальная камера: правая кнопка — вращение, колесо — зум
        private void Viewport_MouseDown(object sender, WF.MouseEventArgs e)
        {
            _viewport!.Focus();
            if (e.Button != WF.MouseButtons.Right) return;
            _rotating = true;
            _viewport.Proxy!.StartRotate(e.X, _viewport.Height - e.Y);
        }

        private void Viewport_MouseMove(object sender, WF.MouseEventArgs e)
        {
            if (!_rotating) return;
            _viewport!.Proxy!.Rotate(e.X, _viewport.Height - e.Y);
        }

        private void BtnRebuild_Click(object sender, RoutedEventArgs e)
        {
            if(_viewport?.Proxy == null) return;
            RebuildScene(_viewport.Proxy);   // внутри — BuildShip с PlaceProjectiles
            ShowCurrentColor();              // пиппер — под цвет из конфига
            RefreshMuzzlePanel();            // поля дула — под новые значения
        }

        private void Viewport_MouseUp(object sender, WF.MouseEventArgs e) => _rotating = false;

        private void Viewport_Wheel(object sender, WF.MouseEventArgs e)
            => _viewport?.Proxy?.ZoomBy(Math.Pow(1.25, e.Delta / 120.0));
    }
}