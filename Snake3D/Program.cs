//Назначение: запустить WPF-приложение и не дать ему молча умереть при исключении.

using System;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace Snake3D
{
    public static class Program
    {
        //Main — весь код запуска. [STAThread] обязателен для WPF: COM-апартамент
        //должен быть однопоточным, иначе упадёт создание окна.

        [STAThread]
        public static void Main()
        {
			//						Snake3D
            try
            {
                var app = new App();

                //app.DispatcherUnhandledException — перехват исключений в
                //UI-потоке(обработчики событий, рендер - колбэки).
                //Исключение показывается и помечается Handled = true —
                //приложение продолжает жить.
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show(e.Exception.ToString(), "Dispatcher Exception");
                    e.Handled = true;
                };

                //AppDomain.CurrentDomain.UnhandledException — страховка от
                //исключений в фоновых потоках. Здесь поймать и продолжить нельзя:
                //процесс всё равно завершится, остаётся только показать причину.
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    MessageBox.Show(e.ExceptionObject?.ToString(), "Domain Exception");
                };

                var view = new SnakeView();
                app.Run(view);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Exception");
            }
        }
    }
}