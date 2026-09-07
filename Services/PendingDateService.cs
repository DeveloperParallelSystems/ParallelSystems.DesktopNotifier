using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier.Services;

public interface IPendingDateService
{
    DateOnly? SelectPastDate(IEnumerable<DateOnly> unavailableDates);
}

public sealed class PendingDateService : IPendingDateService
{
    public DateOnly? SelectPastDate(IEnumerable<DateOnly> unavailableDates)
    {
        var dialog = new PendingDateDialog(unavailableDates)
        {
            Owner = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
        };
        return dialog.ShowDialog() == true ? dialog.SelectedPastDate : null;
    }
}
