using System;
using System.Windows;
using YDM.Engine;
using YDM.Queue;
using YDM.Storage;
using YDM.Storage.Repositories;
using YDM.UI.ViewModels;

namespace YDM.UI;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var connectionFactory = new SqliteConnectionFactory();
            var downloadRepository = new SqliteDownloadRepository(connectionFactory);
            var settingsRepository = new SqliteSettingsRepository(connectionFactory);

            await downloadRepository.InitializeAsync();
            await settingsRepository.InitializeAsync();

            var settings = await settingsRepository.GetSettingsAsync();

            var engine = new DownloadEngine(downloadRepository);
            var queueManager = new DownloadQueueManager(engine, downloadRepository, settings.MaxConcurrentDownloads);

            var mainViewModel = new MainViewModel(engine, downloadRepository, queueManager, settingsRepository);
            var mainWindow = new MainWindow(mainViewModel, settingsRepository);

            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Fatal error starting YDM: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                "YDM Startup Error", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
