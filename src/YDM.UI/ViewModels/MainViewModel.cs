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

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();
    public ICollectionView FilteredDownloads { get; }

    [ObservableProperty]
    private DownloadItemViewModel? _selectedDownload;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private bool _isQueueRunning;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    private CancellationTokenSource? _ipcServerCts;

    public MainViewModel(
        IDownloadEngine engine, 
        IDownloadRepository repository, 
        IDownloadQueueManager queueManager,
        ISettingsRepository settingsRepository)
    {
        _engine = engine;
        _repository = repository;
        _queueManager = queueManager;
        _settingsRepository = settingsRepository;

        FilteredDownloads = CollectionViewSource.GetDefaultView(Downloads);
        FilteredDownloads.Filter = FilterDownloadItem;

        _engine.ProgressChanged += OnEngineProgressChanged;
        _engine.StateChanged += OnEngineStateChanged;
        _queueManager.QueueUpdated += OnQueueUpdated;

        StartIpcNamedPipeServer();
    }

    public async Task InitializeAsync()
    {
        var jobs = await _repository.GetAllAsync();
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Downloads.Clear();
            foreach (var job in jobs)
            {
                Downloads.Add(new DownloadItemViewModel(job));
            }
            StatusMessage = $"Loaded {Downloads.Count} downloads";
        });

        var settings = await _settingsRepository.GetSettingsAsync();
        if (settings.AutoStartDownloads)
        {
            await _queueManager.StartQueueAsync();
            IsQueueRunning = true;
        }
    }

    private bool FilterDownloadItem(object obj)
    {
        if (obj is not DownloadItemViewModel item) return false;

        return SelectedCategory switch
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
            _ => true
        };
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        FilteredDownloads.Refresh();
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

    public async Task AddDownloadDirectAsync(string url, string? destFolder = null, string? customName = null, int connections = 8, bool startImmediately = true)
    {
        var job = await _engine.AddDownloadAsync(
            url: url,
            destinationDirectory: destFolder,
            customFileName: customName,
            maxConnections: connections);

        var vm = new DownloadItemViewModel(job);
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Downloads.Insert(0, vm);
            SelectedDownload = vm;
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
        });
    }

    private void OnEngineStateChanged(object? sender, JobStateChangedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var item = Downloads.FirstOrDefault(d => d.Id == e.JobId);
            item?.UpdateStatus(e.NewStatus, e.ErrorMessage);
            FilteredDownloads.Refresh();
        });
    }

    private void OnQueueUpdated(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsQueueRunning = _queueManager.IsRunning;
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

                    byte[] lenBytes = new byte[4];
                    await server.ReadExactlyAsync(lenBytes, 0, 4, token);
                    int payloadLen = BitConverter.ToInt32(lenBytes, 0);

                    if (payloadLen > 0 && payloadLen < 1024 * 1024)
                    {
                        byte[] payload = new byte[payloadLen];
                        await server.ReadExactlyAsync(payload, 0, payloadLen, token);
                        string json = Encoding.UTF8.GetString(payload);

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string? action = root.TryGetProperty("action", out var actProp) ? actProp.GetString() : null;
                        string? url = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                        string? filename = root.TryGetProperty("fileName", out var fnProp) ? fnProp.GetString() : null;

                        if (string.Equals(action, "download", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(url))
                        {
                            var settings = await _settingsRepository.GetSettingsAsync();
                            await AddDownloadDirectAsync(
                                url: url, 
                                destFolder: settings.DefaultDownloadDirectory, 
                                customName: filename, 
                                connections: settings.DefaultConnectionsPerDownload, 
                                startImmediately: settings.AutoStartDownloads);

                            string response = JsonSerializer.Serialize(new { status = "accepted" });
                            byte[] respBytes = Encoding.UTF8.GetBytes(response);
                            byte[] respLen = BitConverter.GetBytes(respBytes.Length);

                            await server.WriteAsync(respLen, token);
                            await server.WriteAsync(respBytes, token);
                            await server.FlushAsync(token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Continue accepting next pipe connection
                }
            }
        }, token);
    }
}
