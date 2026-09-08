using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier.Services;

public interface IConfirmationService
{
    bool ConfirmDailySubmission(DateOnly workDate, int sessionCount, string total);
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool ConfirmDailySubmission(DateOnly workDate, int sessionCount, string total)
    {
        var dialog = new ConfirmationDialog(
            workDate.ToString("MMMM d, yyyy"), sessionCount, total);

        dialog.Owner = System.Windows.Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);

        return dialog.ShowDialog() == true;
    }
}
