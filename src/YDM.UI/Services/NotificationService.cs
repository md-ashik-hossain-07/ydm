using System;
using System.Windows;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.UI.Views;

namespace YDM.UI.Services;

public class NotificationService
{
    private readonly ISettingsRepository _settingsRepository;

    public NotificationService(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
    }

    public async void ShowJobCompletedNotification(DownloadJob job)
    {
        try
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            if (!settings.EnableNotifications) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var toast = new ToastNotificationWindow(
                    title: "Download Complete — YDM",
                    message: $"{job.FileName} ({FormatBytes(job.DownloadedBytes)})",
                    filePath: job.DestinationPath,
                    isSuccess: true);
                toast.Show();
            });
        }
        catch { }
    }

    public async void ShowJobFailedNotification(DownloadJob job, string? error)
    {
        try
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            if (!settings.EnableNotifications) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var toast = new ToastNotificationWindow(
                    title: "Download Failed — YDM",
                    message: $"{job.FileName}: {error ?? "Download error"}",
                    filePath: job.DestinationPath,
                    isSuccess: false);
                toast.Show();
            });
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }
}