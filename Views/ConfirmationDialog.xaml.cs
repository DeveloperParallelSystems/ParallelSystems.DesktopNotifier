using System.Windows;
using System.Windows.Input;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string workDate, string recorded, string additional, string total)
    {
        InitializeComponent();
        DataContext = new ConfirmationDialogModel
        {
            WorkDate = workDate,
            Recorded = recorded,
            Additional = additional,
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
        public string Recorded { get; set; } = string.Empty;
        public string Additional { get; set; } = string.Empty;
        public string Total { get; set; } = string.Empty;
    }
}
