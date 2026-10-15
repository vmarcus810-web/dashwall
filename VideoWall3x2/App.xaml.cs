using System;
using System.IO;
using System.Linq;
using System.Windows;
using VideoWall;

namespace VideoWall3x2
{
    /// <summary>
    /// Abre o formulário de configuração. Com o argumento /start
    /// inicia o video wall direto (útil para inicialização automática).
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            VideoWallSettings settings;

            try
            {
                settings = ConfigService.Load();
            }
            catch (FileNotFoundException)
            {
                settings = new VideoWallSettings();
                settings.Normalize();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                settings = new VideoWallSettings();
                settings.Normalize();
            }

            var config = new ConfigWindow(settings);
            MainWindow = config;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            bool autoStart = e.Args.Any(a =>
                a.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--start", StringComparison.OrdinalIgnoreCase));

            if (autoStart)
                config.StartWall();
            else
                config.Show();
        }
    }
}
