using ParallelSystems.DesktopNotifier.Services;
using ParallelSystems.DesktopNotifier.Views;
using System.Windows;

namespace ParallelSystems.DesktopNotifier;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\ParallelSystems.DesktopNotifier.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showMain;
    private RegisteredWaitHandle? _showMainRegistration;
    private NotifierService? _service;
    private DesktopBridge? _bridge;
    private MainWindow? _window;
    private ManualEntryPromptWindow? _entryPrompt;
    private NotificationService? _notifications;
    private UpdateShutdownServer? _updateServer;
    private bool _exitingForUpdate;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        _showMain = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ParallelSystems.DesktopNotifier.ShowMain");
        if (!isFirstInstance)
        {
            if (e.Args.Contains("--show-main")) _showMain.Set();
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
            _service = new NotifierService(api, settings, new NotificationStateStore());
            _bridge = new DesktopBridge(api, _service);
            _window = new MainWindow(settings, _bridge);
            _window.Closing += (_, args) => { if (!_exitingForUpdate) { args.Cancel = true; _window.Hide(); } };
            _window.IsVisibleChanged += (_, _) =>
            {
                if (!_window.IsVisible) _service.ResumeEntryPrompt(DateOnly.FromDateTime(DateTime.Today));
            };
            _service.EntryPromptRequested += workDate => Dispatcher.BeginInvoke(() =>
            {
                if (_entryPrompt is { IsVisible: true }) return;
                _entryPrompt = new ManualEntryPromptWindow(_service, workDate);
                _entryPrompt.ContinueRequested += date =>
                {
                    _service.DeferEntryPrompt(date);
                    ShowMainWindow();
                    _bridge.OpenDate(date);
                };
                _entryPrompt.Closed += (_, _) => _entryPrompt = null;
                _entryPrompt.Show();
            });
            _service.EntryPromptDismissRequested += workDate => Dispatcher.BeginInvoke(() =>
            {
                if (_entryPrompt?.WorkDate == workDate) _entryPrompt.CloseFromApplication();
            });
            _notifications.OpenRequested += (_, request) =>
            {
                if (_bridge.IsPreparing) return;
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
                _bridge.OpenDate(request.WorkDate);
            };
            _notifications.ExitRequested += (_, _) => Shutdown();
            _service.NotificationRequested += (_, notification) => _notifications.Show(notification);
            _service.NotificationDismissRequested += (_, _) => _notifications.Dismiss();
            _service.Start();
            ParallelSystems.ProductSupport.ProductLifecycle.Report("desktop-notifier", null, Environment.ProcessPath!, "ready");
            ParallelSystems.ProductSupport.ProductLifecycle.EnsureUpdaterBackground();
            _showMainRegistration = ThreadPool.RegisterWaitForSingleObject(_showMain,
                (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, false);
            if (e.Args.Contains("--show-main")) ShowMainWindow();
            _updateServer = new UpdateShutdownServer("ParallelSystems.DesktopNotifier",
                async token => await await Dispatcher.InvokeAsync(() => _bridge.PrepareForUpdateAsync(token), System.Windows.Threading.DispatcherPriority.Normal, token),
                async () => await await Dispatcher.InvokeAsync(async () => {
                    await _service.CompleteUpdateAsync();
                    _exitingForUpdate = true;
                    Shutdown();
                }),
                () => Dispatcher.HasShutdownStarted ? Task.CompletedTask : Dispatcher.InvokeAsync(() => _bridge.AbortUpdate()).Task);
        }
        catch (Exception ex)
        {
            ParallelSystems.ProductSupport.ProductLifecycle.Report("desktop-notifier", null, Environment.ProcessPath!, "failed");
            System.Windows.MessageBox.Show(ex.Message, "Parallel Systems Notifier", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowMainWindow()
    {
        if (_window is null || _bridge is null || _bridge.IsPreparing) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _entryPrompt?.CloseFromApplication();
        _showMainRegistration?.Unregister(null);
        _showMain?.Dispose();
        _updateServer?.Stop();
        _service?.Dispose();
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
