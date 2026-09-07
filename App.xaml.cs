using ParallelSystems.DesktopNotifier.Services;
using ParallelSystems.DesktopNotifier.ViewModels;
using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier;

public partial class App : System.Windows.Application
{
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private NotificationService? _notifications;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settings = AppSettings.Load();
            var api = new DesktopApiClient(settings);
            _notifications = new NotificationService();
            _viewModel = new MainViewModel(api, settings, new ConfirmationService(),
                new ReminderDismissalStore());
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closing += (_, args) => { args.Cancel = true; _window.Hide(); };
            _notifications.OpenRequested += async (_, _) =>
            {
                await _viewModel.OpenFromNotificationAsync();
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            };
            _notifications.ExitRequested += (_, _) => Shutdown();
            _notifications.ManualDismissRequested += (_, _) => _viewModel.DismissCurrentReminder();
            _viewModel.NotificationRequested += (_, message) =>
            {
                if (_window?.IsVisible != true)
                    _notifications.Show(message);
            };
            _viewModel.NotificationDismissRequested += (_, _) => _notifications.Dismiss();
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Parallel Systems Notifier", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _notifications?.Dispose();
        base.OnExit(e);
    }
}
