using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class PendingDateDialog : Window
{
    public DateOnly? SelectedPastDate { get; private set; }

    public PendingDateDialog(IEnumerable<DateOnly> unavailableDates)
    {
        InitializeComponent();
        WorkDatePicker.DisplayDateEnd = DateTime.Today.AddDays(-1);
        foreach (var date in unavailableDates.Distinct())
            WorkDatePicker.BlackoutDates.Add(new CalendarDateRange(date.ToDateTime(TimeOnly.MinValue)));
        WorkDatePicker.SelectedDate = Enumerable.Range(1, 366)
            .Select(daysAgo => DateTime.Today.AddDays(-daysAgo))
            .FirstOrDefault(date => !WorkDatePicker.BlackoutDates.Contains(date));
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void ConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (!WorkDatePicker.SelectedDate.HasValue || WorkDatePicker.SelectedDate.Value.Date >= DateTime.Today)
            return;
        SelectedPastDate = DateOnly.FromDateTime(WorkDatePicker.SelectedDate.Value);
        DialogResult = true;
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
