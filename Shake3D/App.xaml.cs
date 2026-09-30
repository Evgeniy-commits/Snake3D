using Snake3D;
using System.Configuration;
using System.Data;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;


namespace Shake3D
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
        }
    }

}
