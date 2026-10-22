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
            Proxy.CreateGrid(arenaSize, 5.0);

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
