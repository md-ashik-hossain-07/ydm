using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using YDM.Core.Security;

namespace YDM.UI.Views;

public partial class AddDownloadDialog : Window
{
    public string DownloadUrl => UrlTextBox.Text.Trim();
    public string DestinationFolder => FolderTextBox.Text.Trim();
    public string CustomFileName => FileNameTextBox.Text.Trim();
    public bool SetAsDefaultLocation => SetAsDefaultCheckBox.IsChecked ?? false;
    public DuplicateResolution DuplicateAction { get; private set; } = DuplicateResolution.AutoRename;

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
                5 => 32,
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

        if (!UrlSecurityValidator.IsValidDownloadUrl(DownloadUrl, allowLocal: true))
        {
            MessageBox.Show("The entered URL is invalid or uses an unsupported protocol.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validate destination folder
        var validation = PathSanitizer.ValidateDestinationDirectory(DestinationFolder);
        if (!validation.IsValid)
        {
            MessageBox.Show(validation.ErrorMessage, "Invalid Download Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Resolve duplicate file option
        DuplicateAction = DuplicateComboBox.SelectedIndex switch
        {
            0 => DuplicateResolution.AutoRename,
            1 => DuplicateResolution.Prompt,
            2 => DuplicateResolution.Overwrite,
            _ => DuplicateResolution.AutoRename
        };

        string candidateFileName = !string.IsNullOrWhiteSpace(CustomFileName)
            ? PathSanitizer.SanitizeFileName(CustomFileName)
            : PathSanitizer.SanitizeFileName(System.IO.Path.GetFileName(new Uri(DownloadUrl, UriKind.RelativeOrAbsolute).LocalPath));

        string targetFilePath = System.IO.Path.Combine(DestinationFolder, candidateFileName);

        if (File.Exists(targetFilePath) && DuplicateAction == DuplicateResolution.Prompt)
        {
            var result = MessageBox.Show(
                $"The file '{candidateFileName}' already exists in '{DestinationFolder}'.\n\nDo you want to overwrite it?\n\n• Yes: Overwrite the existing file\n• No: Save as a renamed copy\n• Cancel: Change name or location",
                "File Already Exists",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DuplicateAction = DuplicateResolution.Overwrite;
            }
            else if (result == MessageBoxResult.No)
            {
                DuplicateAction = DuplicateResolution.AutoRename;
            }
            else
            {
                return; // User canceled
            }
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
