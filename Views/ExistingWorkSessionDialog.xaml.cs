using ParallelSystems.DesktopNotifier.Models;
using System.Windows;
using System.Windows.Input;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class ExistingWorkSessionDialog : Window
{
    public ExistingWorkSessionDialog(DailyStatusModel status)
    {
        InitializeComponent();
        DataContext = new DialogModel
        {
            WorkDate = status.WorkDate.ToString("MMMM d, yyyy"),
            SessionCount = status.ExistingSessionCount,
            Recorded = FormatDuration(status.RecordedSeconds),
            Additional = FormatDuration(status.AdditionalSeconds),
            Total = FormatDuration(status.TotalSeconds)
        };
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void ConfirmClicked(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
    private static string FormatDuration(long seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}";
    }

    private sealed class DialogModel
    {
        public string WorkDate { get; set; } = string.Empty;
        public int SessionCount { get; set; }
        public string Recorded { get; set; } = string.Empty;
        public string Additional { get; set; } = string.Empty;
        public string Total { get; set; } = string.Empty;
    }
}
