using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ZenoxDownloader.ViewModels;

namespace ZenoxDownloader.Models
{
    public enum DownloadStatus
    {
        Queued,
        Downloading,
        Paused,
        Canceled,
        Completed,
        Error
    }

    public class VideoItem : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _url = string.Empty;
        private string _duration = "--:--";
        private string _thumbnailUrl = string.Empty;
        private double _progress;
        private DownloadStatus _status;
        private string _statusMessage = "Waiting...";
        
        public string Title 
        { 
            get => _title; 
            set { _title = value; OnPropertyChanged(); } 
        }

        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        public string Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }

        public string ThumbnailUrl
        {
            get => _thumbnailUrl;
            set { _thumbnailUrl = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public DownloadStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }
        
        public string OutputPath { get; set; } = string.Empty;
        public string DownloadArgs { get; set; } = string.Empty;
        public string Speed { get; set; } = string.Empty;
        public string Eta { get; set; } = string.Empty;

        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand CopyUrlCommand { get; }
        public ICommand TrimVideoCommand { get; }

        public VideoItem()
        {
            OpenFileCommand = new RelayCommand(_ => {
                if (!string.IsNullOrEmpty(OutputPath) && System.IO.File.Exists(OutputPath))
                {
                    try { 
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(OutputPath) { UseShellExecute = true }); 
                    } catch { }
                }
            });

            OpenFolderCommand = new RelayCommand(_ => {
                if (!string.IsNullOrEmpty(OutputPath))
                {
                    string folder = System.IO.Path.GetDirectoryName(OutputPath);
                    if (System.IO.Directory.Exists(folder))
                    {
                        try {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
                        } catch { }
                    }
                }
            });

            CopyUrlCommand = new RelayCommand(_ => {
                if (!string.IsNullOrEmpty(Url))
                {
                    try { System.Windows.Clipboard.SetText(Url); } catch {}
                }
            });

            TrimVideoCommand = new RelayCommand(_ => {
                if (Status != DownloadStatus.Completed || string.IsNullOrEmpty(OutputPath))
                {
                    System.Windows.MessageBox.Show("Video must be completely downloaded first.");
                    return;
                }
                
                if (System.IO.Directory.Exists(OutputPath) || !System.IO.File.Exists(OutputPath))
                {
                    System.Windows.MessageBox.Show($"Could not find the downloaded video file at:\n{OutputPath}\n\nPlease try downloading it again to register the exact file path.", "File Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var dialog = new TrimVideoDialog();
                // Set owner if possible, though from here we don't easily have a reference to MainWindow. 
                // We'll just let it float center screen.
                dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                if (dialog.ShowDialog() == true)
                {
                    string startTime = dialog.StartTime;
                    string endTime = dialog.EndTime;

                    if (string.IsNullOrEmpty(startTime))
                        startTime = "00:00:00";

                    string dir = System.IO.Path.GetDirectoryName(OutputPath) ?? string.Empty;
                    string filename = System.IO.Path.GetFileNameWithoutExtension(OutputPath);
                    string ext = System.IO.Path.GetExtension(OutputPath);
                    string newFilename = $"{filename}_trimmed{ext}";
                    string newPath = System.IO.Path.Combine(dir, newFilename);

                    string args = $"-y -i \"{OutputPath}\" -ss {startTime}";
                    if (!string.IsNullOrEmpty(endTime))
                    {
                        args += $" -to {endTime}";
                    }
                    args += $" -c copy \"{newPath}\"";

                    try
                    {
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "ffmpeg",
                                Arguments = args,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0)
                        {
                            System.Windows.MessageBox.Show($"Video trimmed successfully!\nSaved to: {newPath}", "Trim Complete", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("Failed to trim video. Make sure start/end times are valid.\n(Do you have ffmpeg installed?)", "Trim Failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error running ffmpeg: {ex.Message}\nMake sure ffmpeg is installed and added to PATH.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
            }, _ => Status == DownloadStatus.Completed);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
