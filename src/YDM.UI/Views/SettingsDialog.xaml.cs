using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using YDM.Core.Models;
using YDM.Core.Security;

namespace YDM.UI.Views;

public partial class SettingsDialog : Window
{
    private readonly ApplicationSettings _settings;

    public SettingsDialog(ApplicationSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        DefaultFolderTextBox.Text = _settings.DefaultDownloadDirectory;
        MaxConcurrentTextBox.Text = _settings.MaxConcurrentDownloads.ToString();
        DefaultConnectionsTextBox.Text = _settings.DefaultConnectionsPerDownload.ToString();
        AutoStartCheckBox.IsChecked = _settings.AutoStartDownloads;
        AllowLocalCheckBox.IsChecked = _settings.AllowLocalIpAddresses;

        // Theme
        SelectComboBoxByTag(ThemeComboBox, _settings.Theme);

        // Interception Mode
        SelectComboBoxByTag(InterceptionModeComboBox, _settings.InterceptionMode);

        // Clipboard & Notifications
        ClipboardMonitoringCheckBox.IsChecked = _settings.EnableClipboardMonitoring;
        NotificationsCheckBox.IsChecked = _settings.EnableNotifications;

        // Queue Scheduler
        QueueScheduleCheckBox.IsChecked = _settings.QueueScheduleEnabled;
        ScheduleStartTimeTextBox.Text = _settings.QueueScheduleStartTime ?? "01:00";
        ScheduleStopTimeTextBox.Text = _settings.QueueScheduleStopTime ?? "06:00";

        // Deletion confirmation
        ConfirmDeletionCheckBox.IsChecked = _settings.ConfirmFileDeletion;
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Default Download Directory",
            InitialDirectory = Directory.Exists(DefaultFolderTextBox.Text) 
                ? DefaultFolderTextBox.Text 
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == true)
        {
            DefaultFolderTextBox.Text = dialog.FolderName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        string targetFolder = DefaultFolderTextBox.Text.Trim();
        var validation = PathSanitizer.ValidateDestinationDirectory(targetFolder);
        if (!validation.IsValid)
        {
            MessageBox.Show(validation.ErrorMessage, "Invalid Default Download Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MaxConcurrentTextBox.Text, out int maxConcurrent) || maxConcurrent < 1 || maxConcurrent > 10)
        {
            MessageBox.Show("Max simultaneous downloads must be between 1 and 10.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DefaultConnectionsTextBox.Text, out int defConn) || defConn < 1 || defConn > 32)
        {
            MessageBox.Show("Default connections must be between 1 and 32.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.DefaultDownloadDirectory = targetFolder;
        _settings.MaxConcurrentDownloads = maxConcurrent;
        _settings.DefaultConnectionsPerDownload = defConn;
        _settings.AutoStartDownloads = AutoStartCheckBox.IsChecked ?? true;
        _settings.AllowLocalIpAddresses = AllowLocalCheckBox.IsChecked ?? false;

        _settings.Theme = GetSelectedTag(ThemeComboBox, "System");
        _settings.InterceptionMode = GetSelectedTag(InterceptionModeComboBox, "Automatic");
        _settings.EnableBrowserInterception = !string.Equals(_settings.InterceptionMode, "Manual", StringComparison.OrdinalIgnoreCase);

        _settings.EnableClipboardMonitoring = ClipboardMonitoringCheckBox.IsChecked ?? false;
        _settings.EnableNotifications = NotificationsCheckBox.IsChecked ?? true;

        _settings.QueueScheduleEnabled = QueueScheduleCheckBox.IsChecked ?? false;
        _settings.QueueScheduleStartTime = ScheduleStartTimeTextBox.Text.Trim();
        _settings.QueueScheduleStopTime = ScheduleStopTimeTextBox.Text.Trim();

        _settings.ConfirmFileDeletion = ConfirmDeletionCheckBox.IsChecked ?? true;

        DialogResult = true;
        Close();
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void SelectComboBoxByTag(ComboBox comboBox, string? tagValue)
    {
        if (string.IsNullOrWhiteSpace(tagValue)) return;
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = cbi;
                return;
            }
        }
    }

    private static string GetSelectedTag(ComboBox comboBox, string defaultValue)
    {
        if (comboBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            return tag;
        }
        return defaultValue;
    }
}
