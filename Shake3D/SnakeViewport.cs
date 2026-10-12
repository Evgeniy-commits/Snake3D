using Snake3DViewer;
using System;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Snake3D
{
    [SupportedOSPlatform("windows")]
    public partial class SnakeViewport : UserControl
    {
        public SnakeSceneProxy? Proxy { get; private set; }
        private bool _isInitialized = false; // Флаг, чтобы не инициализировать дважды

        public SnakeViewport()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.Dock = System.Windows.Forms.DockStyle.Fill; // Это правильно, оставляем
        }

        public SnakeViewport(IContainer container) : this() // Вызываем основной конструктор, чтобы не дублировать код
        {
            container.Add(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_isInitialized) return;

            Proxy = new SnakeSceneProxy();

            // 1. Инициализируем движок (он создаст все объекты, но размер будет дефолтным)
            Proxy.Initialize(this.Handle);
            _isInitialized = true;

            // 2. СРАЗУ вызываем ресайз с реальными размерами контрола.
            // К этому моменту this.Width и this.Height уже равны размеру внутри WindowsFormsHost
            // благодаря свойству this.Dock = DockStyle.Fill;
            OnResize(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (Proxy == null) return;

            // Передаем текущие размеры контрола в C++
            // C++ через SetWindowPos изменит размер окна OCCT под эти значения
            Proxy.Resize(this.Width, this.Height);
        }

        //Инициализация самой игры (сцена, объекты)
        public void InitializeGameScene(double arenaSize, double segmentRadius, double foodRadius)
        {
            if (Proxy == null || !_isInitialized) return;

            // 1. Сетка (шаг 1.0)
            Proxy.CreateGrid(arenaSize, 1.0);

            // 2. Стены
            //Proxy.CreateWalls(arenaSize);

            // 3. Змея (начальная позиция)
            // Голова
            //Proxy.AddSegment(0, 0, 0, segmentRadius, 0);
            // Тело 1
            //Proxy.AddSegment(-1, 0, 0, segmentRadius, 1);
            // Тело 2
            //Proxy.AddSegment(-2, 0, 0, segmentRadius, 2);

            // 4. Еда
            //Proxy.AddFood(5, 5, 0, foodRadius);

            // 5. Финальная подгонка камеры
            Proxy.Redraw();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Proxy?.ClearScene();
            }
            base.Dispose(disposing);
        }
    }
}
