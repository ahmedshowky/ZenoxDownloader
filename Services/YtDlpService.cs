using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using ZenoxDownloader.Models;

namespace ZenoxDownloader.Services
{
    public class YtDlpService
    {
        private readonly string _ytDlpPath;

        public YtDlpService(string ytDlpPath)
        {
            _ytDlpPath = ytDlpPath;
        }

        public async Task<(string Title, List<VideoItem> Items)> GetPlaylistMetadataAsync(string url)
        {
            // --flat-playlist to get list fast, -J for JSON
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = $"--flat-playlist -J \"{url}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string output = errorBuilder.ToString();
                    string friendlyMessage = "Unable to process this URL.";

                    // Parse for specific known errors
                    if (output.Contains("Video unavailable"))
                    {
                        if (output.Contains("copyright claim"))
                            friendlyMessage = "This video is unavailable due to a copyright claim.";
                        else
                            friendlyMessage = "This video is unavailable (deleted, private, or restricted).";
                    }
                    else if (output.Contains("playlist type is unviewable"))
                    {
                        friendlyMessage = "This playlist is private or unviewable.";
                    }
                    else if (output.Contains("This video is private"))
                    {
                        friendlyMessage = "This video is private and cannot be accessed.";
                    }
                    else if (output.Contains("Incomplete YouTube ID"))
                    {
                        friendlyMessage = "The video ID in the URL appears to be incomplete.";
                    }
                    else
                    {
                        var errorLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var actualErrors = new List<string>();
                        foreach (var line in errorLines)
                        {
                            if (line.StartsWith("ERROR:") || line.Contains("Error:"))
                                actualErrors.Add(line.Replace("ERROR: [youtube]", "").Replace("ERROR: ", "").Trim());
                        }

                        if (actualErrors.Count > 0)
                            friendlyMessage = string.Join("\n", actualErrors);
                    }

                    throw new Exception(friendlyMessage);
                }
            }

            var json = outputBuilder.ToString();
            var items = new List<VideoItem>();
            string playlistTitle = "Unknown Playlist";

            try 
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    
                    // Try get playlist title
                    if (root.TryGetProperty("title", out var pTitle))
                        playlistTitle = pTitle.GetString() ?? "Unknown";

                    if (root.TryGetProperty("entries", out var entries))
                    {
                        foreach (var entry in entries.EnumerateArray())
                        {
                             // Build VideoItem
                             var item = new VideoItem
                             {
                                 Title = entry.TryGetProperty("title", out var t) ? (t.GetString() ?? "Unknown") : "Unknown",
                                 Url = entry.TryGetProperty("url", out var u) ? (u.GetString() ?? url) : url, 
                                 Duration = entry.TryGetProperty("duration", out var d) ? TimeSpan.FromSeconds(d.GetDouble()).ToString(@"mm\:ss") : "--:--",
                                 Status = DownloadStatus.Queued,
                                 StatusMessage = "Waiting..."
                             };
                             
                             if (!item.Url.StartsWith("http"))
                             {
                                 if (entry.TryGetProperty("id", out var id))
                                 {
                                     item.Url = $"https://www.youtube.com/watch?v={id.GetString()}";
                                 }
                             }

                             // Thumbnails: Look for jpg / high res
                             if (entry.TryGetProperty("thumbnails", out var thumbs) && thumbs.GetArrayLength() > 0)
                             {
                                 string? bestThumb = null;
                                 foreach(var thumb in thumbs.EnumerateArray())
                                 {
                                     if(thumb.TryGetProperty("url", out var tUrl))
                                     {
                                         string? uStr = tUrl.GetString();
                                         // Prefer jpg, but take whatever if not found
                                         if (!string.IsNullOrEmpty(uStr))
                                         {
                                             bestThumb = uStr; // Update with latest (usually higher res in list?)
                                             if (uStr.Contains(".jpg") || uStr.Contains("hqdefault"))
                                             {
                                                 // Keep this one, it's likely good compatibility
                                             }
                                         }
                                     }
                                 }
                                 item.ThumbnailUrl = bestThumb ?? string.Empty;
                             }
                             
                             items.Add(item);
                        }
                    }
                    else
                    {
                         // Single video
                         var item = new VideoItem
                         {
                             Title = root.TryGetProperty("title", out var t) ? (t.GetString() ?? "Unknown") : "Unknown",
                             Url = root.TryGetProperty("webpage_url", out var u) ? (u.GetString() ?? url) : url,
                             Status = DownloadStatus.Queued,
                             StatusMessage = "Waiting..."
                         };
                         
                         // Single video thumbnail
                         if (root.TryGetProperty("thumbnail", out var mainThumb))
                            item.ThumbnailUrl = mainThumb.GetString() ?? string.Empty;
                         else if (root.TryGetProperty("thumbnails", out var thumbs) && thumbs.GetArrayLength() > 0)
                            item.ThumbnailUrl = thumbs[thumbs.GetArrayLength()-1].GetProperty("url").GetString() ?? string.Empty;

                         items.Add(item);
                    }
                }
            }
            catch(Exception ex) 
            {
                Trace.WriteLine($"Error parsing JSON: {ex.Message}");
            }
            
            return (playlistTitle, items);
        }

        public async Task DownloadVideoAsync(VideoItem item, string outputDir, string args, CancellationToken ct, IProgress<string> progressReport)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) progressReport.Report(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) progressReport.Report(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(ct);
                }
                catch (TaskCanceledException)
                {
                    process.Kill();
                    throw;
                }

                if (process.ExitCode != 0 && !ct.IsCancellationRequested)
                {
                    throw new Exception($"Process exited with code {process.ExitCode}");
                }
            }
        }
    }
}
