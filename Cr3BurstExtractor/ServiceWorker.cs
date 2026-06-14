using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cr3BurstExtractor;

/// <summary>
/// Background worker hosted by the Windows Service.
///
/// Watches the user's scan folder (set in <see cref="UserSettings.ScanFolder"/>)
/// for new .CR3 files; for each one waits until the writer has closed the
/// handle, then runs <see cref="AutoExtractor.ProcessFile"/> with the user's
/// current move/backup preferences.
///
/// A second watcher tails settings.json so toggling
/// <see cref="UserSettings.AutoExtractOnNewFiles"/> or changing the scan
/// folder from the standalone tool re-binds the worker without a service
/// restart. On rebind (and on initial start when the toggle is on), the
/// worker enumerates the scan folder and enqueues any existing files that
/// aren't already in <see cref="NonBurstCache"/> and don't have an
/// already-extracted sibling folder — so "turn on the toggle, then dump
/// files in" works without further user action.
/// </summary>
public sealed class ServiceWorker : BackgroundService
{
    readonly ILogger<ServiceWorker> _logger;
    readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    readonly ConcurrentDictionary<string, DateTime> _recentlyEnqueued = new(StringComparer.OrdinalIgnoreCase);
    static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(2);
    static readonly TimeSpan FileReadyTimeout = TimeSpan.FromSeconds(60);
    static readonly TimeSpan FileReadyPoll    = TimeSpan.FromMilliseconds(500);

    FileSystemWatcher? _settingsWatcher;
    FileSystemWatcher? _scanWatcher;
    CancellationTokenSource? _settingsDebounce;
    string? _activeScanFolder;
    bool _activeAutoExtract;

    public ServiceWorker(ILogger<ServiceWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cr3BurstExtractor service starting.");
        SharedPaths.EnsureDir();
        StartSettingsWatcher();
        await RebindAsync(stoppingToken);

        try
        {
            await foreach (var path in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested) break;
                await ProcessOneAsync(path, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally
        {
            _scanWatcher?.Dispose();
            _settingsWatcher?.Dispose();
            _logger.LogInformation("Cr3BurstExtractor service stopped.");
        }
    }

    void StartSettingsWatcher()
    {
        try
        {
            _settingsWatcher = new FileSystemWatcher(SharedPaths.Dir, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _settingsWatcher.Changed += (_, _) => DebounceSettingsReload();
            _settingsWatcher.Created += (_, _) => DebounceSettingsReload();
            _settingsWatcher.Renamed += (_, _) => DebounceSettingsReload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to watch settings.json; settings changes will require a service restart.");
        }
    }

    void DebounceSettingsReload()
    {
        // Debounce: a single Save() often produces multiple events.
        _settingsDebounce?.Cancel();
        _settingsDebounce = new CancellationTokenSource();
        var token = _settingsDebounce.Token;
        _ = Task.Delay(300, token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            try
            {
                UserSettings.Reload();
                await RebindAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Settings reload failed.");
            }
        }, TaskScheduler.Default);
    }

    async Task RebindAsync(CancellationToken stoppingToken)
    {
        string? scanFolder = UserSettings.ScanFolder;
        bool autoExtract = UserSettings.AutoExtractOnNewFiles;

        if (string.Equals(scanFolder, _activeScanFolder, StringComparison.OrdinalIgnoreCase)
            && autoExtract == _activeAutoExtract
            && _scanWatcher != null == autoExtract)
        {
            // No change in the values we care about.
            return;
        }

        _scanWatcher?.Dispose();
        _scanWatcher = null;
        _activeScanFolder = scanFolder;
        _activeAutoExtract = autoExtract;

        if (!autoExtract)
        {
            _logger.LogInformation("Auto-extract is disabled; idle until enabled.");
            return;
        }
        if (string.IsNullOrWhiteSpace(scanFolder) || !Directory.Exists(scanFolder))
        {
            _logger.LogWarning("Scan folder is unset or missing ({Folder}); idle until valid.", scanFolder);
            return;
        }
        if (scanFolder.StartsWith(@"\\"))
        {
            _logger.LogWarning("Scan folder is a UNC path ({Folder}); FileSystemWatcher reliability over SMB is limited. Proceeding anyway.", scanFolder);
        }

        try
        {
            _scanWatcher = new FileSystemWatcher(scanFolder, "*.CR3")
            {
                IncludeSubdirectories = true,
                // Created/Renamed cover the camera-offload patterns (direct write
                // and atomic .tmp -> .CR3 rename). Changed is intentionally NOT
                // wired — a long copy fires Changed hundreds of times and our
                // file-ready loop already handles "is the writer done yet?".
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                InternalBufferSize = 65536,
                EnableRaisingEvents = true,
            };
            _scanWatcher.Created += (_, e) => Enqueue(e.FullPath);
            _scanWatcher.Renamed += (_, e) => Enqueue(e.FullPath);
            _scanWatcher.Error += (_, e) =>
            {
                _logger.LogWarning(e.GetException(), "Watcher error; re-enumerating scan folder.");
                ReconcileExistingFiles(scanFolder);
            };
            _logger.LogInformation("Watching {Folder} for new .CR3 files.", scanFolder);

            // Initial sweep: anything already in the folder that we haven't
            // processed should be processed now.
            ReconcileExistingFiles(scanFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watcher on {Folder}.", scanFolder);
        }

        await Task.CompletedTask;
    }

    void ReconcileExistingFiles(string scanFolder)
    {
        try
        {
            NonBurstCache.Reload();
            foreach (var file in Directory.EnumerateFiles(scanFolder, "*.CR3", SearchOption.AllDirectories))
            {
                // Skip files that already have a sibling extraction folder
                // (the per-burst output dir BurstExtractor writes into).
                string parent = Path.GetDirectoryName(Path.GetFullPath(file))!;
                string siblingDir = Path.Combine(parent, Path.GetFileNameWithoutExtension(file));
                if (Directory.Exists(siblingDir)) continue;

                try
                {
                    var info = new FileInfo(file);
                    if (NonBurstCache.IsKnownNonBurst(file, info)) continue;
                }
                catch { /* fall through and enqueue — worker will handle it */ }

                Enqueue(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconcile sweep failed for {Folder}.", scanFolder);
        }
    }

    void Enqueue(string path)
    {
        var now = DateTime.UtcNow;
        if (_recentlyEnqueued.TryGetValue(path, out var last) && now - last < DedupeWindow)
            return;
        _recentlyEnqueued[path] = now;

        // Opportunistic GC of the dedupe map — it's only used to coalesce
        // bursts of events for the same path, so anything older than the
        // window is dead weight.
        if (_recentlyEnqueued.Count > 256)
        {
            foreach (var kv in _recentlyEnqueued)
                if (now - kv.Value > DedupeWindow) _recentlyEnqueued.TryRemove(kv.Key, out _);
        }

        _queue.Writer.TryWrite(path);
    }

    async Task ProcessOneAsync(string path, CancellationToken stoppingToken)
    {
        try
        {
            if (!File.Exists(path)) return;

            if (!await WaitForFileReadyAsync(path, stoppingToken))
            {
                _logger.LogWarning("Timed out waiting for {Path} to settle; skipping for now.", path);
                return;
            }

            // Re-read the cache so writes from the standalone tool are visible.
            NonBurstCache.Reload();

            string? backup = UserSettings.BackupFolder;
            bool moveOriginals = UserSettings.MoveOriginalsToBackup;

            var result = AutoExtractor.ProcessFile(
                path,
                moveOriginals,
                backup,
                msg => { if (!string.IsNullOrEmpty(msg)) _logger.LogInformation("{Msg}", msg); });

            switch (result.Outcome)
            {
                case AutoExtractOutcome.Extracted:
                    _logger.LogInformation("Extracted {Count} frame(s) from {Path}.", result.FrameCount, path);
                    break;
                case AutoExtractOutcome.SkippedNonBurst:
                    _logger.LogInformation("Marked {Path} as non-burst (single frame).", path);
                    break;
                case AutoExtractOutcome.Cached:
                    // Quiet — this is the common no-op case during reconcile sweeps.
                    break;
                case AutoExtractOutcome.Error:
                    _logger.LogError(result.Error, "Failed to process {Path}.", path);
                    break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing {Path}.", path);
        }
    }

    static async Task<bool> WaitForFileReadyAsync(string path, CancellationToken stoppingToken)
    {
        var deadline = DateTime.UtcNow + FileReadyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (IOException) { /* still being written */ }
            catch (UnauthorizedAccessException) { /* permission may settle once writer closes */ }

            try { await Task.Delay(FileReadyPoll, stoppingToken); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }
}
