using System.Windows;
using ZenoxDownloader.ViewModels;

namespace ZenoxDownloader
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            try
            {
                DataContext = new MainViewModel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing application logic: {ex.Message}\n\n{ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}