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

    public MainWindow(MainViewModel viewModel, ISettingsRepository settingsRepository)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsRepository = settingsRepository;
        DataContext = _viewModel;

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
            await _viewModel.AddDownloadDirectAsync(
                url: dialog.DownloadUrl,
                destFolder: dialog.DestinationFolder,
                customName: string.IsNullOrWhiteSpace(dialog.CustomFileName) ? null : dialog.CustomFileName,
                connections: dialog.MaxConnections,
                startImmediately: true);
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
            _viewModel.StatusMessage = "Settings saved";
        }
    }
}
