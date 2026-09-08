using System.Windows;
using System.Windows.Input;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string workDate, int sessionCount, string total)
    {
        InitializeComponent();
        DataContext = new ConfirmationDialogModel
        {
            WorkDate = workDate,
            SessionCount = sessionCount,
            Total = total
        };
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void ConfirmClicked(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class ConfirmationDialogModel
    {
        public string WorkDate { get; set; } = string.Empty;
        public int SessionCount { get; set; }
        public string Total { get; set; } = string.Empty;
    }
}
