using ParallelSystems.DesktopNotifier.Services;
using ParallelSystems.DesktopNotifier.ViewModels;
using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\ParallelSystems.DesktopNotifier.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private NotificationService? _notifications;
    private UpdateShutdownServer? _updateServer;
    private bool _exitingForUpdate;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        try
        {
            ParallelSystems.ProductSupport.ProductLifecycle.Report("desktop-notifier", null, Environment.ProcessPath!, "started");
            var settings = AppSettings.Load();
            var api = new DesktopApiClient(settings);
            _notifications = new NotificationService();
            _viewModel = new MainViewModel(api, settings, new ConfirmationService(),
                new NotificationSettingsService(), new NotificationStateStore());
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closing += (_, args) => { if (!_exitingForUpdate) { args.Cancel = true; _window.Hide(); } };
            _notifications.OpenRequested += (_, request) =>
            {
                if (_viewModel.IsUpdateShutdownPending) return;
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
                _viewModel.OpenFromNotification(request.WorkDate);
            };
            _notifications.ExitRequested += (_, _) => Shutdown();
            _viewModel.NotificationRequested += (_, notification) => _notifications.Show(notification);
            _viewModel.NotificationDismissRequested += (_, _) => _notifications.Dismiss();
            await _viewModel.InitializeAsync();
            ParallelSystems.ProductSupport.ProductLifecycle.Report("desktop-notifier", null, Environment.ProcessPath!, "ready");
            ParallelSystems.ProductSupport.ProductLifecycle.EnsureUpdaterBackground();
            _updateServer = new UpdateShutdownServer("ParallelSystems.DesktopNotifier",
                token => Dispatcher.InvokeAsync(() => _viewModel.PrepareForUpdate(), System.Windows.Threading.DispatcherPriority.Normal, token).Task,
                async () => await await Dispatcher.InvokeAsync(async () => {
                    await _viewModel.CompleteUpdateShutdownAsync();
                    _exitingForUpdate = true;
                    Shutdown();
                }),
                () => Dispatcher.HasShutdownStarted ? Task.CompletedTask : Dispatcher.InvokeAsync(() => _viewModel.AbortUpdateShutdown()).Task);
        }
        catch (Exception ex)
        {
            ParallelSystems.ProductSupport.ProductLifecycle.Report("desktop-notifier", null, Environment.ProcessPath!, "failed");
            System.Windows.MessageBox.Show(ex.Message, "Parallel Systems Notifier", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateServer?.Stop();
        _viewModel?.Dispose();
        _notifications?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }
}
