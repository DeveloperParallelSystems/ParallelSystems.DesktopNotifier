using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier.Services;

public interface INotificationSettingsService
{
    NotificationSchedule? Edit(TimeOnly morning, TimeOnly evening);
}

public sealed class NotificationSettingsService : INotificationSettingsService
{
    private readonly DesktopApiClient _api;

    public NotificationSettingsService(DesktopApiClient api) => _api = api;

    public NotificationSchedule? Edit(TimeOnly morning, TimeOnly evening)
    {
        var dialog = new NotificationSettingsDialog(morning, evening, _api)
        {
            Owner = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
        };

        return dialog.ShowDialog() == true
            ? new NotificationSchedule(dialog.MorningTime, dialog.EveningTime)
            : null;
    }
}

public sealed record NotificationSchedule(TimeOnly Morning, TimeOnly Evening);
