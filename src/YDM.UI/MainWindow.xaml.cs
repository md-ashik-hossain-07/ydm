using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                if (dialog.SetAsDefaultLocation && !string.Equals(settings.DefaultDownloadDirectory, dialog.DestinationFolder, StringComparison.OrdinalIgnoreCase))
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

    // Title Bar Window Control Actions
    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    // Top Action Toolbar
    private async void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsRepository.GetSettingsAsync();
        var dialog = new AddDownloadDialog(settings.DefaultDownloadDirectory)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            if (dialog.SetAsDefaultLocation && !string.Equals(settings.DefaultDownloadDirectory, dialog.DestinationFolder, StringComparison.OrdinalIgnoreCase))
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
        if (sender is FrameworkElement elem && elem.Tag is string category)
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
            _viewModel.IsDarkMode = YDM.UI.Services.ThemeManager.IsDarkThemeActive;
            _viewModel.QueueManager.MaxConcurrency = settings.MaxConcurrentDownloads;
            _viewModel.StatusMessage = "Settings saved";
        }
    }

    private void DetailTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabName)
        {
            _viewModel.SelectedDetailTab = tabName;
        }
    }

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is DownloadItemViewModel item)
        {
            _viewModel.SelectedDownload = item;
        }
    }

    private void OverflowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }
}
