using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace YDM.UI.Views;

public partial class AddDownloadDialog : Window
{
    public string DownloadUrl => UrlTextBox.Text.Trim();
    public string DestinationFolder => FolderTextBox.Text.Trim();
    public string CustomFileName => FileNameTextBox.Text.Trim();
    public int MaxConnections
    {
        get
        {
            return ConnectionsComboBox.SelectedIndex switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                3 => 8,
                4 => 16,
                _ => 8
            };
        }
    }

    public AddDownloadDialog(string defaultFolder, string? prefilledUrl = null)
    {
        InitializeComponent();
        FolderTextBox.Text = defaultFolder;

        if (!string.IsNullOrWhiteSpace(prefilledUrl))
        {
            UrlTextBox.Text = prefilledUrl;
        }

        UrlTextBox.Focus();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder",
            InitialDirectory = Directory.Exists(FolderTextBox.Text) ? FolderTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == true)
        {
            FolderTextBox.Text = dialog.FolderName;
        }
    }

    private void DownloadNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            MessageBox.Show("Please enter a valid download URL.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
