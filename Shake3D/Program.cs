using System;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace Snake3D
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                var app = new App();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show(e.Exception.ToString(), "Dispatcher Exception");
                    e.Handled = true;
                };
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