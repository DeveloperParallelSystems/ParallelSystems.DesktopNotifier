using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier.Services;

public interface IConfirmationService
{
    bool ConfirmAddToExistingDate(DailyStatusModel status);
    bool ConfirmDailySubmission(DateOnly workDate, string recorded, string additional, string total);
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool ConfirmAddToExistingDate(DailyStatusModel status)
    {
        var dialog = new ExistingWorkSessionDialog(status)
        {
            Owner = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
        };
        return dialog.ShowDialog() == true;
    }

    public bool ConfirmDailySubmission(DateOnly workDate, string recorded, string additional, string total)
    {
        var dialog = new ConfirmationDialog(
            workDate.ToString("MMMM d, yyyy"), recorded, additional, total);

        dialog.Owner = System.Windows.Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);

        return dialog.ShowDialog() == true;
    }
}
