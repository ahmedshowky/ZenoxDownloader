using System;
using System.Windows;

namespace ZenoxDownloader
{
    public partial class TrimVideoDialog : Window
    {
        public string StartTime { get; private set; } = string.Empty;
        public string EndTime { get; private set; } = string.Empty;

        public TrimVideoDialog()
        {
            InitializeComponent();
        }

        private void TrimButton_Click(object sender, RoutedEventArgs e)
        {
            StartTime = StartTimeTextBox.Text.Trim();
            EndTime = EndTimeTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
