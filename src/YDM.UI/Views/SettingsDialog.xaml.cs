using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using YDM.Core.Models;

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
        if (string.IsNullOrWhiteSpace(DefaultFolderTextBox.Text) || !Directory.Exists(DefaultFolderTextBox.Text))
        {
            MessageBox.Show("Please select a valid existing directory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        _settings.DefaultDownloadDirectory = DefaultFolderTextBox.Text.Trim();
        _settings.MaxConcurrentDownloads = maxConcurrent;
        _settings.DefaultConnectionsPerDownload = defConn;
        _settings.AutoStartDownloads = AutoStartCheckBox.IsChecked ?? true;
        _settings.AllowLocalIpAddresses = AllowLocalCheckBox.IsChecked ?? false;

        DialogResult = true;
        Close();
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
