using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace YDM.UI.Views;

public partial class ToastNotificationWindow : Window
{
    private readonly string? _filePath;
    private readonly DispatcherTimer _autoCloseTimer;

    public ToastNotificationWindow(string title, string message, string? filePath = null, bool isSuccess = true)
    {
        InitializeComponent();
        _filePath = filePath;

        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;

        if (!isSuccess || string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            OpenFileButton.Visibility = Visibility.Collapsed;
        }

        if (string.IsNullOrWhiteSpace(_filePath))
        {
            OpenFolderButton.Visibility = Visibility.Collapsed;
        }

        // Position at bottom-right of primary work area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };
        _autoCloseTimer.Start();
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_filePath) && File.Exists(_filePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _filePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
        Close();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_filePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_filePath}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}