using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZenoxDownloader.Models;

namespace ZenoxDownloader.Services
{
    public class DownloadQueueService
    {
        private readonly YtDlpService _ytDlpService;
        private readonly SemaphoreSlim _semaphore;
        private CancellationTokenSource _globalCts;
        
        // Maps items to their specific local CTS (for individual cancellation)
        private System.Collections.Concurrent.ConcurrentDictionary<VideoItem, CancellationTokenSource> _activeDownloads;

        public ObservableCollection<VideoItem> Items { get; } = new ObservableCollection<VideoItem>();

        public DownloadQueueService(YtDlpService service)
        {
            _ytDlpService = service;
            _semaphore = new SemaphoreSlim(1, 1); // 1 concurrent download
            _activeDownloads = new System.Collections.Concurrent.ConcurrentDictionary<VideoItem, CancellationTokenSource>();
            _globalCts = new CancellationTokenSource();
            _ = ProcessQueueAsync();
        }

        public void AddToQueue(VideoItem item)
        {
            item.Status = DownloadStatus.Queued;
            lock(Items)
            {
                Items.Add(item);
            }
        }

        private async Task ProcessQueueAsync()
        {
            while (!_globalCts.Token.IsCancellationRequested)
            {
                // Find next queued item
                // We use ToList because we might modify properties on UI thread via Dispatcher if binding, 
                // but here we are in background task. 
                // Note: Collection changes should ideally be on UI thread or use binding operations. 
                // Application.Current.Dispatcher is available in WPF.
                
                VideoItem? nextItem = null;
                lock (Items)
                {
                    nextItem = Items.FirstOrDefault(i => i.Status == DownloadStatus.Queued);
                }

                if (nextItem == null) 
                {
                    await Task.Delay(1000); // Wait bit before checking again
                    continue; 
                }

                await _semaphore.WaitAsync(); // Wait for slot

                try
                {
                    // Double check status
                    if (nextItem.Status != DownloadStatus.Queued) continue;

                    nextItem.Status = DownloadStatus.Downloading;
                    nextItem.StatusMessage = "Starting...";

                    var cts = new CancellationTokenSource();
                    _activeDownloads[nextItem] = cts;

                    var progressHandler = new Progress<string>(line => 
                    {
                        ParseOutput(nextItem, line);
                    });

                    // Construct arguments specific to this item
                    // For now, passing generic arguments, need to allow customization
                    // We need to pass the FULL arguments including output path.
                    // This creates a dependency on UI settings. We should pass config OR generate args before queuing.
                    
                    // Assume 'Args' is stored on the item or we build it here? 
                    // Let's store a generator function or pre-build args.
                    // Ideally VideoItem should have a 'DownloadArgs' property.
                    string args = nextItem.DownloadArgs; 

                    await _ytDlpService.DownloadVideoAsync(nextItem, nextItem.OutputPath, args, cts.Token, progressHandler);
                    
                    nextItem.Status = DownloadStatus.Completed;
                    nextItem.StatusMessage = "Done";
                    nextItem.Progress = 100;
                }
                catch (OperationCanceledException)
                {
                    nextItem.Status = DownloadStatus.Canceled;
                    nextItem.StatusMessage = "Canceled";
                }
                catch (Exception ex)
                {
                    nextItem.Status = DownloadStatus.Error;
                    nextItem.StatusMessage = "Error: " + ex.Message;
                }
                finally
                {
                    _activeDownloads.TryRemove(nextItem, out _);
                    _semaphore.Release();
                }
            }
        }
        
        private void ParseOutput(VideoItem item, string line)
        {
            // Regex for progress
            var match = Regex.Match(line, @"\[download\]\s+(\d+\.?\d*)%\s+of\s+~?([\d\.]+\w+)\s+at\s+([0-9\.]+\w+/s)(?:\s+ETA\s+([\d:]+))?");
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, out double p)) item.Progress = p;
                item.StatusMessage = $"Downloading {p:0.0}% - {match.Groups[3].Value} - ETA {match.Groups[4].Value}";
            }
            else if (line.Contains("[Merger] Merging formats into"))
            {
                var m = Regex.Match(line, "into \"([^\"]+)\"");
                if (m.Success) item.OutputPath = m.Groups[1].Value;
                item.StatusMessage = "Merging formats...";
            }
            else if (line.Contains("[download] Destination:"))
            {
                item.OutputPath = line.Substring(line.IndexOf("Destination:") + 12).Trim();
                item.StatusMessage = "Writing to disk...";
            }
            else if (line.Contains("has already been downloaded"))
            {
                var m = Regex.Match(line, @"\[download\] (.*?) has already been downloaded");
                if (m.Success) item.OutputPath = m.Groups[1].Value.Trim();
            }
            else if (line.Contains("[ExtractAudio] Destination:"))
            {
                item.OutputPath = line.Substring(line.IndexOf("Destination:") + 12).Trim();
            }
        }

        public void CancelItem(VideoItem item)
        {
            if (_activeDownloads.TryGetValue(item, out var cts))
            {
                cts.Cancel();
            }
            else if (item.Status == DownloadStatus.Queued)
            {
                item.Status = DownloadStatus.Canceled;
            }
        }
    }
}
