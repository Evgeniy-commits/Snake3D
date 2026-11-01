using System.Windows;
using System.IO;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace Snake3D
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            
            base.OnStartup(e);
            DispatcherUnhandledException += (s, a) =>
            {
                File.AppendAllText(@"C:\Users\Admin\Documents\Snake3D\crash.log",
                    $"UI: {DateTime.Now:HH:mm:ss.fff}\n{a.Exception}\n\n");
                a.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) =>
                File.AppendAllText(@"C:\Users\Admin\Documents\Snake3D\crash.log",
                    $"Фон: {DateTime.Now:HH:mm:ss.fff}\n{a.ExceptionObject}\n\n");
            try
            {
                SettingsStore.Load();         // настройки до первого окна
                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Ошибка запуска");
                Shutdown();
            }            
        }
    }

}
