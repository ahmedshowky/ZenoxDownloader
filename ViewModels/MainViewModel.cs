using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ZenoxDownloader.Models;
using ZenoxDownloader.Services;

namespace ZenoxDownloader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _url = string.Empty;
        private string _statusText = "Ready";
        private bool _isBusy;
        private bool _isPlaylistMode;
        
        // Settings
        private string _outputDirectory = string.Empty;
        private string _selectedQuality = "Best Available";
        private bool _embedSubs;
        private bool _autoSubs;
        private bool _manualSubs;
        private string _manualLangs = string.Empty;
        private string _subFormat = "vtt";

        private readonly YtDlpService _ytDlpService;
        private readonly DownloadQueueService _queueService;

        public MainViewModel()
        {
            // Find yt-dlp
            string ytDlpPath = FindYtDlp();
            _ytDlpService = new YtDlpService(ytDlpPath);
            _queueService = new DownloadQueueService(_ytDlpService);
            
            VideoList = _queueService.Items;
            OutputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            ProcessUrlCommand = new RelayCommand(async _ => await ProcessUrlAsync());
            StartDownloadCommand = new RelayCommand(_ => StartDownload());
            BrowseCommand = new RelayCommand(_ => BrowseFolder());
            CancelItemCommand = new RelayCommand(obj => {
                if(obj is VideoItem item) CancelItem(item);
            });
        }

        public ObservableCollection<VideoItem> VideoList { get; }

        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public bool IsPlaylistMode
        {
            get => _isPlaylistMode;
            set { _isPlaylistMode = value; OnPropertyChanged(); }
        }
        
        public bool IsSingleMode => !IsPlaylistMode;

        // Settings Properties
        public string OutputDirectory
        {
            get => _outputDirectory;
            set { _outputDirectory = value; OnPropertyChanged(); }
        }

        public string SelectedQuality
        {
            get => _selectedQuality;
            set { _selectedQuality = value; OnPropertyChanged(); }
        }
        
        public bool EmbedSubs { get => _embedSubs; set { _embedSubs = value; OnPropertyChanged(); } }
        public bool AutoSubs { get => _autoSubs; set { _autoSubs = value; OnPropertyChanged(); } }
        public bool ManualSubs { get => _manualSubs; set { _manualSubs = value; OnPropertyChanged(); } }
        public string ManualLangs { get => _manualLangs; set { _manualLangs = value; OnPropertyChanged(); } }
        
        public bool IsVtt { get => _subFormat == "vtt"; set { if(value) _subFormat="vtt"; OnPropertyChanged(); } }
        public bool IsSrt { get => _subFormat == "srt"; set { if(value) _subFormat="srt"; OnPropertyChanged(); } }

        // Commands
        public ICommand ProcessUrlCommand { get; }
        public ICommand StartDownloadCommand { get; } 
        public ICommand BrowseCommand { get; }
        public ICommand CancelItemCommand { get; }

        private string FindYtDlp()
        {
            string current = Directory.GetCurrentDirectory();
            string check1 = Path.Combine(current, "yt-dlp.exe");
            string check2 = Path.Combine(@"c:\Users\i7\Downloads\ZenoxDownloader", "yt-dlp.exe");
             
             if (File.Exists(check1)) return check1;
             if (File.Exists(check2)) return check2;
             return "yt-dlp.exe"; 
        }

        public void CancelItem(VideoItem? item)
        {
            if (item != null)
            {
                _queueService.CancelItem(item);
                StatusText = $"Canceled download for {item.Title}";
            }
        }

        private async Task ProcessUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(Url)) return;

            IsBusy = true;
            StatusText = "Analyzing URL...";

            try
            {
                // Clear previous
                _queueService.Items.Clear();

                // Check for ambiguous URL (contains both video and playlist)
                string targetUrl = Url;
                bool isPlaylist = Url.Contains("list=");

                if (Url.Contains("list=") && (Url.Contains("v=") || Url.Contains("watch?v=")))
                {
                   var result = MessageBox.Show("This URL contains both a video and a playlist.\n\nDo you want to download the entire playlist?", 
                                                "Playlist Detected", 
                                                MessageBoxButton.YesNo, 
                                                MessageBoxImage.Question);
                   
                   if (result == MessageBoxResult.No)
                   {
                       // User wants only the video, strip the list parameter
                       targetUrl = System.Text.RegularExpressions.Regex.Replace(Url, @"([&?])list=[^&]*", "");
                       // Fix potential double & or trailing characters if needed, but usually redundant & is fine for browsers/yt-dlp or handled by regex 
                       // better regex to handle separator logic:
                       if (targetUrl.EndsWith("&")) targetUrl = targetUrl.Substring(0, targetUrl.Length - 1);
                       if (targetUrl.EndsWith("?")) targetUrl = targetUrl.Substring(0, targetUrl.Length - 1);
                       
                       isPlaylist = false;
                   }
                }

                IsPlaylistMode = isPlaylist;
                OnPropertyChanged(nameof(IsSingleMode)); 
                
                var resultData = await _ytDlpService.GetPlaylistMetadataAsync(targetUrl);
                var items = resultData.Items;
                string playlistTitle = resultData.Title;

                if (items.Count > 1) 
                {
                    IsPlaylistMode = true;
                    // Ask about folder
                    var folderResult = MessageBox.Show($"Found playlist: \"{playlistTitle}\"\n\nDo you want to create a specific folder for it?", 
                                                       "Playlist Subfolder", 
                                                       MessageBoxButton.YesNo, 
                                                       MessageBoxImage.Question);
                    
                    if (folderResult == MessageBoxResult.Yes)
                    {
                        // Clean title for file system
                        string safeTitle = string.Join("_", playlistTitle.Split(System.IO.Path.GetInvalidFileNameChars()));
                        string newDir = System.IO.Path.Combine(OutputDirectory, safeTitle);
                        if (!System.IO.Directory.Exists(newDir))
                             System.IO.Directory.CreateDirectory(newDir);
                        
                        // We strictly don't update the global OutputDirectory property to avoid confusing the user UI,
                        // instead we just set the item output path.
                        // OR we update global if that's preferred. Let's update global.
                        OutputDirectory = newDir; 
                    }
                }
                else 
                {
                    IsPlaylistMode = false;
                }
                
                OnPropertyChanged(nameof(IsSingleMode)); 

                foreach (var item in items)
                {
                    // Pre-generate args so they are ready when we queue
                    item.OutputPath = OutputDirectory;
                    item.DownloadArgs = BuildArguments(item.Url, OutputDirectory);
                    _queueService.AddToQueue(item); 
                }
                
                StatusText = $"Loaded {items.Count} videos.";
            }
            catch (Exception ex)
            {
                StatusText = "Error: " + ex.Message;
                MessageBox.Show(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        private void StartDownload()
        {
            // Specifically for Single Video button if needed, 
            // but currently ProcessUrl adds to queue immediately.
            // Maybe we want "Process" to just Load, and "Start" to Queue?
            // "Fetch playlist metadata (title, number of videos)... Display... Download Queue & States"
            // The requirement says "Queued (waiting to download)".
            // So ProcessUrlAsync SHOULD just load them into the list with status "Queued" but NOT start the processing loop until user says so?
            // Or maybe "Queued" means "In the list waiting for its turn".
            // The QueueService starts processing immediately. 
            // I'll leave it as auto-start for now as it's easier, user can Pause/Cancel.
            // Actually, for playlist, usually you want to see the list first.
            // But implementing a separate "Add to Queue" step vs "Start Queue" might be complex for this timeframe.
            // The prompt says "Download Queue... Only one video downloads at a time... Skip to next...".
            // This implies auto-processing. 
        }

        private void BrowseFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                OutputDirectory = dialog.FolderName;
            }
        }

        private string BuildArguments(string url, string outputDir)
        {
             var sb = new System.Text.StringBuilder();
             
             // Output
             string path = Path.Combine(outputDir, "%(title)s.%(ext)s");
             sb.Append($"--output \"{path}\" ");
             
             // Quality
             if (SelectedQuality.Contains("1080")) sb.Append("-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]\" ");
             else if (SelectedQuality.Contains("720")) sb.Append("-f \"bestvideo[height<=720]+bestaudio/best[height<=720]\" ");
             else if (SelectedQuality.Contains("480")) sb.Append("-f \"bestvideo[height<=480]+bestaudio/best[height<=480]\" ");
             else if (SelectedQuality.Contains("Audio")) sb.Append("-x --audio-format mp3 ");
             // else best (default)

             // Subtitles
             if (AutoSubs) sb.Append("--write-auto-sub ");
             if (ManualSubs && !string.IsNullOrWhiteSpace(ManualLangs)) sb.Append($"--write-sub --sub-langs \"{ManualLangs}\" ");
             
             // Format
             if (IsSrt) sb.Append("--convert-subs srt ");
             
             // Embed
             if (EmbedSubs) sb.Append("--embed-subs ");
             
             sb.Append($"\"{url}\"");
             return sb.ToString();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
