//Назначение: мост между миром WPF и миром OCCT.
//Это WinForms-контрол (UserControl из System.Windows.Forms),
//который владеет SnakeSceneProxy и живёт внутри WindowsFormsHost
//(поле ViewportHost в XAML окна).

using Snake3DViewer;
using System;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Runtime.Versioning;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

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
            //говорит WinForms: «я сам отвечаю за свою отрисовку, не стирай фон».
            //Это уменьшает мерцание при ресайзе.Dock = Fill растягивает
            //контрол на всю ячейку хоста.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.Dock = System.Windows.Forms.DockStyle.Fill; // Это правильно, оставляем
        }

        //OnHandleCreated — ключевой момент жизненного цикла.
        //У WinForms-контрола HWND создаётся не в конструкторе, а позже,
        //при добавлении в иерархию окон.Только здесь у нас есть настоящий
        //this.Handle, который можно отдать в OCCT.

        //Логика: Proxy = new SnakeSceneProxy() — создаётся управляемая обёртка;

        // Proxy.Initialize(this.Handle) — OCCT привязывает WNT_Window к нашему
        // HWND и создаёт OpenGL-контекст;

        //OnResize(e) — сразу подгоняем размер вьюпорта под реальные габариты контрола.Флаг _isInitialized защищает от повторной инициализации: OnHandleCreated может сработать при пересоздании хендла, а OpenGL-контекст пересоздавать нельзя — потеряем все ресурсы.
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

        //OnResize — при каждом изменении размеров передаёт их в
        //Proxy.Resize(width, height). Под капотом там SetWindowPos растягивает
        //окно OCCT и MustBeResized пересчитывает вьюпорт и матрицу проекции.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (Proxy == null) return;

            // Передаем текущие размеры контрола в C++
            // C++ через SetWindowPos изменит размер окна OCCT под эти значения
            Proxy.Resize(this.Width, this.Height);
        }

        //InitializeGameScene(arenaSize, ...) — рисует статичную декорацию(сетка)
        //и делает Redraw() с подгонкой камеры.Вызывается из окна один раз,
        //до старта игры.

        //Инициализация самой игры (сцена, объекты)
        public void InitializeGameScene(double arenaSize, double gridStep)
        {
            if (Proxy == null || !_isInitialized) return;

            // Сетка/стены/подсветка строятся в SnakeGame.Reset —
            // здесь только небо: оно не зависит от опций и не стирается рестартом.
            Proxy.CreateStarSky(800, 240.0);
        }

        //Dispose(bool) — при разрушении контрола вызывает Proxy?.ClearScene().
        //Под капотом WindowsFormsHost разрушает HWND, и OCCT-контекст
        //без очистки оставил бы «осиротевшие» объекты.
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

//Под капотом — уже знакомый airspace: OCCT рисует в собственном нативном HWND
//поверх WPF-дерева. WPF не может рисовать под или над этой областью
//(отсюда отдельное окно HudOverlay), а ввод мыши уходит напрямую контролу —
//поэтому все мышиные события подписаны здесь, в WinForms-мире, а не в WPF.

//Метод             | Кто вызывает                      |	Когда	
//OnHandleCreated   | WinForms-инфраструктура
//                    (Control.CreateHandle)	        | контрол добавлен в хост и впервые показан	| живой, ключевой
//Dispose(bool)	    | цепочка Dispose от
//                    WindowsFormsHost / финализатор	| закрытие окна	| живой, страховка

//SnakeViewport(IContainer)	только WinForms Designer / ручной код с контейнером	—	мёртвый, можно удалить
