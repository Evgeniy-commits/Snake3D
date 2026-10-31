using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace Snake3D
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                var view = new SnakeView();
                view.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Ошибка запуска");
                Shutdown();
            }
            SettingsStore.Load();          // настройки до первого окна
        }
    }

}
