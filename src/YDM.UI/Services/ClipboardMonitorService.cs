using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using YDM.Core.Interfaces;
using YDM.Core.Security;
using YDM.UI.Views;

namespace YDM.UI.Services;

public class ClipboardMonitorService
{
    private readonly Window _owner;
    private readonly ISettingsRepository _settingsRepository;
    private readonly Func<string, Task> _onAddUrlCallback;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentDictionary<string, DateTime> _recentPrompts = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex FileExtensionRegex = new(
        @"\.(zip|rar|7z|tar|gz|bz2|xz|iso|pdf|docx?|xlsx?|pptx?|epub|txt|exe|msi|apk|bin|mp4|mkv|avi|mov|webm|mp3|wav|flac|ogg|aac)(\?.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private string? _lastClipboardText;

    public ClipboardMonitorService(Window owner, ISettingsRepository settingsRepository, Func<string, Task> onAddUrlCallback)
    {
        _owner = owner;
        _settingsRepository = settingsRepository;
        _onAddUrlCallback = onAddUrlCallback;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var settings = await _settingsRepository.GetSettingsAsync();
            if (!settings.EnableClipboardMonitoring) return;

            if (!Clipboard.ContainsText()) return;

            string text = Clipboard.GetText().Trim();
            if (string.IsNullOrWhiteSpace(text) || text == _lastClipboardText) return;

            _lastClipboardText = text;

            // Only recognize valid HTTP/HTTPS URLs
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return;
            if (!UrlSecurityValidator.IsValidDownloadUrl(text, allowLocal: settings.AllowLocalIpAddresses)) return;

            // Check if URL looks like a direct downloadable file or ends in supported extension
            bool isDownloadLink = FileExtensionRegex.IsMatch(uri.LocalPath) ||
                                  uri.Query.Contains("download", StringComparison.OrdinalIgnoreCase) ||
                                  uri.Query.Contains("attachment", StringComparison.OrdinalIgnoreCase);

            if (!isDownloadLink) return;

            // Suppress duplicate prompts within 60-second cooldown
            var now = DateTime.UtcNow;
            if (_recentPrompts.TryGetValue(text, out var lastPromptTime) && (now - lastPromptTime).TotalSeconds < 60)
            {
                return;
            }

            _recentPrompts[text] = now;

            // Show non-intrusive prompt
            var result = MessageBox.Show(
                _owner,
                $"A downloadable link was detected in your clipboard:\n\n{text}\n\nWould you like to download this file with YDM?",
                "Download Link Detected — YDM",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _onAddUrlCallback(text);
            }
        }
        catch
        {
            // Ignore transient clipboard access errors (e.g. clipboard opened by another app)
        }
    }
}