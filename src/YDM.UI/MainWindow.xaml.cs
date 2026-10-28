using System.Windows;
using System.Windows.Controls;
using YDM.Core.Interfaces;
using YDM.UI.ViewModels;
using YDM.UI.Views;

namespace YDM.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsRepository _settingsRepository;
    private readonly Services.ClipboardMonitorService _clipboardMonitor;

    public MainWindow(MainViewModel viewModel, ISettingsRepository settingsRepository)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsRepository = settingsRepository;
        DataContext = _viewModel;

        _clipboardMonitor = new Services.ClipboardMonitorService(this, _settingsRepository, async (url) =>
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            var dialog = new AddDownloadDialog(settings.DefaultDownloadDirectory, url)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                if (dialog.SetAsDefaultLocation && !string.Equals(settings.DefaultDownloadDirectory, dialog.DestinationFolder, System.StringComparison.OrdinalIgnoreCase))
                {
                    settings.DefaultDownloadDirectory = dialog.DestinationFolder;
                    await _settingsRepository.SaveSettingsAsync(settings);
                }

                await _viewModel.AddDownloadDirectAsync(
                    url: dialog.DownloadUrl,
                    destFolder: dialog.DestinationFolder,
                    customName: string.IsNullOrWhiteSpace(dialog.CustomFileName) ? null : dialog.CustomFileName,
                    connections: dialog.MaxConnections,
                    startImmediately: true,
                    duplicateResolution: dialog.DuplicateAction);
            }
        });
        _clipboardMonitor.Start();

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
        };
    }

    private async void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        var dialog = new AddDownloadDialog(settings.DefaultDownloadDirectory)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            if (dialog.SetAsDefaultLocation && !string.Equals(settings.DefaultDownloadDirectory, dialog.DestinationFolder, System.StringComparison.OrdinalIgnoreCase))
            {
                settings.DefaultDownloadDirectory = dialog.DestinationFolder;
                await _settingsRepository.SaveSettingsAsync(settings);
            }

            await _viewModel.AddDownloadDirectAsync(
                url: dialog.DownloadUrl,
                destFolder: dialog.DestinationFolder,
                customName: string.IsNullOrWhiteSpace(dialog.CustomFileName) ? null : dialog.CustomFileName,
                connections: dialog.MaxConnections,
                startImmediately: true,
                duplicateResolution: dialog.DuplicateAction);
        }
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string category)
        {
            _viewModel.SelectedCategory = category;
        }
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        var dialog = new SettingsDialog(settings)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            await _settingsRepository.SaveSettingsAsync(settings);
            YDM.UI.Services.ThemeManager.ApplyTheme(settings.Theme);
            _viewModel.QueueManager.MaxConcurrency = settings.MaxConcurrentDownloads;
            _viewModel.StatusMessage = "Settings saved";
        }
    }
}
