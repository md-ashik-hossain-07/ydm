using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YDM.Core;
using YDM.Core.Events;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.Queue;

namespace YDM.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDownloadEngine _engine;
    private readonly IDownloadRepository _repository;
    private readonly IDownloadQueueManager _queueManager;
    private readonly ISettingsRepository _settingsRepository;
    private readonly Services.NotificationService? _notificationService;

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();
    public ICollectionView FilteredDownloads { get; }
    public IDownloadQueueManager QueueManager => _queueManager;

    [ObservableProperty]
    private DownloadItemViewModel? _selectedDownload;

    partial void OnSelectedDownloadChanged(DownloadItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedDownload));
    }

    public bool HasSelectedDownload => SelectedDownload != null;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        FilteredDownloads.Refresh();
    }

    [ObservableProperty]
    private bool _isQueueRunning;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private string _sortOption = "Date Added";

    partial void OnSortOptionChanged(string value)
    {
        ApplySorting(value);
    }

    [ObservableProperty]
    private string _selectedDetailTab = "Details";

    private CancellationTokenSource? _ipcServerCts;

    public MainViewModel(
        IDownloadEngine engine, 
        IDownloadRepository repository, 
        IDownloadQueueManager queueManager,
        ISettingsRepository settingsRepository,
        Services.NotificationService? notificationService = null)
    {
        _engine = engine;
        _repository = repository;
        _queueManager = queueManager;
        _settingsRepository = settingsRepository;
        _notificationService = notificationService;

        FilteredDownloads = CollectionViewSource.GetDefaultView(Downloads);
        FilteredDownloads.Filter = FilterDownloadItem;

        _engine.ProgressChanged += OnEngineProgressChanged;
        _engine.StateChanged += OnEngineStateChanged;
        _queueManager.QueueUpdated += OnQueueUpdated;

        StartIpcNamedPipeServer();
    }

    public async Task InitializeAsync()
    {
        await _queueManager.RecoverInterruptedJobsAsync();

        var jobs = await _repository.GetAllAsync();
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Downloads.Clear();
            foreach (var job in jobs)
            {
                Downloads.Add(new DownloadItemViewModel(job));
            }
            RefreshCounters();
            ApplySorting(SortOption);
            StatusMessage = $"Loaded {Downloads.Count} downloads";
        });

        var settings = await _settingsRepository.GetSettingsAsync();
        _queueManager.ScheduleEnabled = settings.QueueScheduleEnabled;
        _queueManager.ScheduleStartTime = settings.QueueScheduleStartTime;
        _queueManager.ScheduleStopTime = settings.QueueScheduleStopTime;

        IsGridView = !string.Equals(settings.ViewMode, "List", StringComparison.OrdinalIgnoreCase);
        IsDarkMode = Services.ThemeManager.IsDarkThemeActive;

        if (settings.AutoStartDownloads)
        {
            await _queueManager.StartQueueAsync();
            IsQueueRunning = true;
        }

        StartQueueSchedulerTimer();
    }

    private void StartQueueSchedulerTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        timer.Tick += async (s, e) =>
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            if (!settings.QueueScheduleEnabled || string.IsNullOrWhiteSpace(settings.QueueScheduleStartTime)) return;

            string now = DateTime.Now.ToString("HH:mm");
            if (string.Equals(now, settings.QueueScheduleStartTime, StringComparison.OrdinalIgnoreCase) && !_queueManager.IsRunning)
            {
                await _queueManager.StartQueueAsync();
                StatusMessage = "Queue automatically started by scheduler";
            }
        };
        timer.Start();
    }

    private void ApplySorting(string option)
    {
        FilteredDownloads.SortDescriptions.Clear();
        switch (option)
        {
            case "Name":
                FilteredDownloads.SortDescriptions.Add(new SortDescription(nameof(DownloadItemViewModel.FileName), ListSortDirection.Ascending));
                break;
            case "Size":
                FilteredDownloads.SortDescriptions.Add(new SortDescription(nameof(DownloadItemViewModel.TotalBytes), ListSortDirection.Descending));
                break;
            case "Status":
                FilteredDownloads.SortDescriptions.Add(new SortDescription(nameof(DownloadItemViewModel.Status), ListSortDirection.Ascending));
                break;
            case "Date Added":
            default:
                FilteredDownloads.SortDescriptions.Add(new SortDescription(nameof(DownloadItemViewModel.CreatedAt), ListSortDirection.Descending));
                break;
        }
        FilteredDownloads.Refresh();
    }

    private bool FilterDownloadItem(object obj)
    {
        if (obj is not DownloadItemViewModel item) return false;

        bool matchesCategory = SelectedCategory switch
        {
            "All" => true,
            "Downloading" => item.Status == JobStatus.Downloading || item.Status == JobStatus.Probing || item.Status == JobStatus.Connecting || item.Status == JobStatus.Assembling,
            "Completed" => item.Status == JobStatus.Completed,
            "Paused" => item.Status == JobStatus.Paused,
            "Failed" => item.Status == JobStatus.Failed,
            "Compressed" => item.Category == DownloadCategory.Compressed,
            "Documents" => item.Category == DownloadCategory.Documents,
            "Music" => item.Category == DownloadCategory.Music,
            "Programs" => item.Category == DownloadCategory.Programs,
            "Video" => item.Category == DownloadCategory.Video,
            "Others" => item.Category == DownloadCategory.Other,
            _ => true
        };

        if (!matchesCategory) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string query = SearchText.Trim();
            return item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Url.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        FilteredDownloads.Refresh();
    }

    // Counters and Metrics for Sidebar and Status Bar
    public int TotalCount => Downloads.Count;
    public int DownloadingCount => Downloads.Count(d => d.Status is JobStatus.Downloading or JobStatus.Connecting or JobStatus.Probing or JobStatus.Assembling);
    public int CompletedCount => Downloads.Count(d => d.Status == JobStatus.Completed);
    public int PausedCount => Downloads.Count(d => d.Status == JobStatus.Paused);
    public int FailedCount => Downloads.Count(d => d.Status == JobStatus.Failed);
    public int QueuedCount => Downloads.Count(d => d.Status == JobStatus.Queued);

    public int AllCategoriesCount => Downloads.Count;
    public int DocumentsCount => Downloads.Count(d => d.Category == DownloadCategory.Documents);
    public int CompressedCount => Downloads.Count(d => d.Category == DownloadCategory.Compressed);
    public int VideoCount => Downloads.Count(d => d.Category == DownloadCategory.Video);
    public int MusicCount => Downloads.Count(d => d.Category == DownloadCategory.Music);
    public int ProgramsCount => Downloads.Count(d => d.Category == DownloadCategory.Programs);
    public int OthersCount => Downloads.Count(d => d.Category == DownloadCategory.Other);

    public string CurrentCategoryTitle => SelectedCategory switch
    {
        "All" => $"All Downloads ({TotalCount})",
        "Downloading" => $"Downloading ({DownloadingCount})",
        "Completed" => $"Completed ({CompletedCount})",
        "Paused" => $"Paused ({PausedCount})",
        "Failed" => $"Failed ({FailedCount})",
        "Documents" => $"Documents ({DocumentsCount})",
        "Compressed" => $"Compressed ({CompressedCount})",
        "Video" => $"Video ({VideoCount})",
        "Music" => $"Music ({MusicCount})",
        "Programs" => $"Programs ({ProgramsCount})",
        "Others" => $"Others ({OthersCount})",
        _ => $"{SelectedCategory} ({FilteredDownloads.Cast<object>().Count()})"
    };

    public string TotalSpeedFormatted
    {
        get
        {
            long speed = Downloads.Where(d => d.Status is JobStatus.Downloading or JobStatus.Connecting or JobStatus.Probing or JobStatus.Assembling)
                                  .Sum(d => d.SpeedBytesPerSecond);
            return speed > 0 ? $"{DownloadItemViewModel.FormatBytes(speed)}/s" : "0.0 KB/s";
        }
    }

    public string TotalTransferredProgressFormatted
    {
        get
        {
            long downloaded = Downloads.Sum(d => d.DownloadedBytes);
            long total = Downloads.Sum(d => d.TotalBytes ?? d.DownloadedBytes);
            if (total > 0)
            {
                return $"{DownloadItemViewModel.FormatBytes(downloaded)} / {DownloadItemViewModel.FormatBytes(total)}";
            }
            return $"{DownloadItemViewModel.FormatBytes(downloaded)}";
        }
    }

    public void RefreshCounters()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DownloadingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(PausedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(QueuedCount));
        OnPropertyChanged(nameof(AllCategoriesCount));
        OnPropertyChanged(nameof(DocumentsCount));
        OnPropertyChanged(nameof(CompressedCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(MusicCount));
        OnPropertyChanged(nameof(ProgramsCount));
        OnPropertyChanged(nameof(OthersCount));
        OnPropertyChanged(nameof(CurrentCategoryTitle));
        OnPropertyChanged(nameof(TotalSpeedFormatted));
        OnPropertyChanged(nameof(TotalTransferredProgressFormatted));
    }

    [RelayCommand]
    public async Task SetGridViewAsync()
    {
        IsGridView = true;
        var s = await _settingsRepository.GetSettingsAsync();
        s.ViewMode = "Grid";
        await _settingsRepository.SaveSettingsAsync(s);
    }

    [RelayCommand]
    public async Task SetListViewAsync()
    {
        IsGridView = false;
        var s = await _settingsRepository.GetSettingsAsync();
        s.ViewMode = "List";
        await _settingsRepository.SaveSettingsAsync(s);
    }

    [RelayCommand]
    public async Task ToggleThemeAsync()
    {
        IsDarkMode = !IsDarkMode;
        string newTheme = IsDarkMode ? "Dark" : "Light";
        Services.ThemeManager.ApplyTheme(newTheme);
        var s = await _settingsRepository.GetSettingsAsync();
        s.Theme = newTheme;
        await _settingsRepository.SaveSettingsAsync(s);
    }

    [RelayCommand]
    public void CloseDetailsPanel()
    {
        SelectedDownload = null;
    }

    [RelayCommand]
    public void CopyUrl()
    {
        if (SelectedDownload == null || string.IsNullOrWhiteSpace(SelectedDownload.Url)) return;
        Clipboard.SetText(SelectedDownload.Url);
        StatusMessage = "URL copied to clipboard";
    }

    [RelayCommand]
    public void CopySaveLocation()
    {
        if (SelectedDownload == null || string.IsNullOrWhiteSpace(SelectedDownload.DestinationPath)) return;
        Clipboard.SetText(SelectedDownload.DestinationPath);
        StatusMessage = "File path copied to clipboard";
    }

    [RelayCommand]
    public async Task RedownloadAsync()
    {
        if (SelectedDownload == null) return;
        string url = SelectedDownload.Url;
        string? dir = Path.GetDirectoryName(SelectedDownload.DestinationPath);
        string name = SelectedDownload.FileName;
        await AddDownloadDirectAsync(url, dir, name);
    }

    // Per-Item Actions
    [RelayCommand]
    public async Task ResumeItemAsync(DownloadItemViewModel item)
    {
        if (item == null) return;
        SelectedDownload = item;
        await ResumeDownloadAsync();
    }

    [RelayCommand]
    public async Task PauseItemAsync(DownloadItemViewModel item)
    {
        if (item == null) return;
        SelectedDownload = item;
        await PauseDownloadAsync();
    }

    [RelayCommand]
    public async Task CancelItemAsync(DownloadItemViewModel item)
    {
        if (item == null) return;
        SelectedDownload = item;
        await CancelDownloadAsync();
    }

    [RelayCommand]
    public async Task RemoveItemAsync(DownloadItemViewModel item)
    {
        if (item == null) return;
        SelectedDownload = item;
        await RemoveDownloadAsync();
    }

    [RelayCommand]
    public void OpenFileItem(DownloadItemViewModel item)
    {
        if (item == null) return;
        SelectedDownload = item;
        OpenFile();
    }

    [RelayCommand]
    public void OpenFolderItem(DownloadItemViewModel item)
    {
        if (item == null) return;
        SelectedDownload = item;
        OpenFolder();
    }

    [RelayCommand]
    public async Task ClearCompletedHistoryAsync()
    {
        var completed = Downloads.Where(d => d.Status == JobStatus.Completed).ToList();
        if (completed.Count == 0)
        {
            MessageBox.Show("No completed downloads found to clear.", "Clear History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Remove {completed.Count} completed download records from history?\n\nNote: Downloaded files on disk will NOT be deleted.",
            "Clear Completed History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            foreach (var item in completed)
            {
                await _repository.DeleteJobAsync(item.Id);
                Downloads.Remove(item);
            }
            RefreshCounters();
            StatusMessage = $"Cleared {completed.Count} completed items from history";
        }
    }

    [RelayCommand]
    public async Task StartQueueAsync()
    {
        await _queueManager.StartQueueAsync();
        IsQueueRunning = true;
        StatusMessage = "Queue started";
    }

    [RelayCommand]
    public async Task StopQueueAsync()
    {
        await _queueManager.StopQueueAsync();
        IsQueueRunning = false;
        StatusMessage = "Queue paused";
    }

    [RelayCommand]
    public async Task ResumeDownloadAsync()
    {
        if (SelectedDownload == null) return;
        await _queueManager.ResumeJobAsync(SelectedDownload.Id);
        StatusMessage = $"Resuming {SelectedDownload.FileName}";
    }

    [RelayCommand]
    public async Task PauseDownloadAsync()
    {
        if (SelectedDownload == null) return;
        await _queueManager.PauseJobAsync(SelectedDownload.Id);
        StatusMessage = $"Paused {SelectedDownload.FileName}";
    }

    [RelayCommand]
    public async Task CancelDownloadAsync()
    {
        if (SelectedDownload == null) return;
        await _queueManager.CancelJobAsync(SelectedDownload.Id);
        StatusMessage = $"Canceled {SelectedDownload.FileName}";
    }

    [RelayCommand]
    public async Task RemoveDownloadAsync()
    {
        if (SelectedDownload == null) return;

        var result = MessageBox.Show(
            $"Do you want to delete the downloaded file from disk as well as removing it from the list?",
            "Remove Download",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel) return;

        bool deleteFiles = (result == MessageBoxResult.Yes);
        var id = SelectedDownload.Id;
        var name = SelectedDownload.FileName;

        await _queueManager.RemoveJobAsync(id, deleteFiles);
        Downloads.Remove(SelectedDownload);
        SelectedDownload = null;
        RefreshCounters();
        StatusMessage = $"Removed {name}";
    }

    [RelayCommand]
    public void OpenFile()
    {
        if (SelectedDownload == null || string.IsNullOrWhiteSpace(SelectedDownload.DestinationPath)) return;
        if (!File.Exists(SelectedDownload.DestinationPath))
        {
            MessageBox.Show("File does not exist on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SelectedDownload.DestinationPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (SelectedDownload == null || string.IsNullOrWhiteSpace(SelectedDownload.DestinationPath)) return;
        string? folder = Path.GetDirectoryName(SelectedDownload.DestinationPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("Folder does not exist on disk.", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{SelectedDownload.DestinationPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task AddDownloadDirectAsync(
        string url, 
        string? destFolder = null, 
        string? customName = null, 
        int connections = 8, 
        bool startImmediately = true,
        YDM.Core.Security.DuplicateResolution duplicateResolution = YDM.Core.Security.DuplicateResolution.AutoRename,
        string? referrer = null,
        string? cookies = null)
    {
        var job = await _engine.AddDownloadAsync(
            url: url,
            destinationDirectory: destFolder,
            customFileName: customName,
            maxConnections: connections,
            referrer: referrer,
            cookies: cookies,
            duplicateResolution: duplicateResolution);

        var vm = new DownloadItemViewModel(job);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Downloads.Insert(0, vm);
            SelectedDownload = vm;
            RefreshCounters();
        });

        if (startImmediately)
        {
            await _queueManager.EnqueueJobAsync(job.Id);
        }
    }

    private void OnEngineProgressChanged(object? sender, JobProgressEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var item = Downloads.FirstOrDefault(d => d.Id == e.JobId);
            item?.UpdateProgress(e.DownloadedBytes, e.TotalBytes, e.SpeedBytesPerSecond, e.RemainingTime);
            OnPropertyChanged(nameof(TotalSpeedFormatted));
            OnPropertyChanged(nameof(TotalTransferredProgressFormatted));
        });
    }

    private async void OnEngineStateChanged(object? sender, JobStateChangedEventArgs e)
    {
        var job = await _repository.GetByIdAsync(e.JobId);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var item = Downloads.FirstOrDefault(d => d.Id == e.JobId);
            item?.UpdateStatus(e.NewStatus, e.ErrorMessage);
            FilteredDownloads.Refresh();
            RefreshCounters();

            if (job != null)
            {
                if (e.NewStatus == JobStatus.Completed)
                {
                    _notificationService?.ShowJobCompletedNotification(job);
                }
                else if (e.NewStatus == JobStatus.Failed)
                {
                    _notificationService?.ShowJobFailedNotification(job, e.ErrorMessage);
                }
            }
        });
    }

    private void OnQueueUpdated(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsQueueRunning = _queueManager.IsRunning;
            RefreshCounters();
            StatusMessage = IsQueueRunning ? $"Queue active ({_queueManager.ActiveDownloadsCount} running)" : "Queue paused";
        });
    }

    private void StartIpcNamedPipeServer()
    {
        _ipcServerCts = new CancellationTokenSource();
        var token = _ipcServerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        "YDM_IPC", 
                        PipeDirection.InOut, 
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, 
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    using var writer = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true };

                    string? line = await reader.ReadLineAsync(token);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        string action = root.GetProperty("action").GetString() ?? "";

                        if (action == "download")
                        {
                            string url = root.GetProperty("url").GetString() ?? "";
                            string? fileName = root.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                            string? referrer = root.TryGetProperty("referrer", out var rf) ? rf.GetString() : null;
                            string? cookies = root.TryGetProperty("cookies", out var ck) ? ck.GetString() : null;

                            await AddDownloadDirectAsync(
                                url: url, 
                                customName: fileName, 
                                referrer: referrer, 
                                cookies: cookies);

                            await writer.WriteLineAsync(JsonSerializer.Serialize(new { success = true }));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, token);
                }
            }
        }, token);
    }
}
