using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class MainWindow : Window
{
    private readonly DesktopBridge _bridge;
    private readonly AppSettings _settings;
    private readonly string? _userDataFolder;
    private WebView2? _browser;
    private Uri? _url;
    private int _generation;
    private bool _loading;
    private bool _closed;
    private bool _allowNavigation;
    private readonly HashSet<string> _requests = [];
    private readonly System.Windows.Threading.DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly System.Windows.Threading.DispatcherTimer _loadTimeout = new() { Interval = TimeSpan.FromSeconds(45) };
    public MainWindow(AppSettings settings, DesktopBridge bridge, string? userDataFolder = null)
    {
        InitializeComponent(); _settings = settings; _bridge = bridge; _userDataFolder = userDataFolder;
        bridge.Event += SendEvent;
        Loaded += async (_, _) => { _statusTimer.Start(); if (_browser is null) await LoadAsync(); };
        Closed += (_, _) => { _closed = true; _generation++; _loadTimeout.Stop(); _statusTimer.Stop(); bridge.Event -= SendEvent; _browser?.Dispose(); };
        _statusTimer.Tick += (_, _) => bridge.PublishStatus();
        _loadTimeout.Tick += (_, _) => { _loadTimeout.Stop(); ShowRecovery("The website did not start Desktop Notifier. Ensure the hosted /desktop-notifier page is deployed, then retry. Reminders continue in the background."); };
    }
    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge.IsPreparing) return;
        if (_bridge.HasPendingEdits && System.Windows.MessageBox.Show("Reloading will discard unsaved changes. Reload the website?", "Reload timesheet", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await LoadAsync();
    }
    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true; RetryButton.IsEnabled = false;
        ShowRecovery("Loading your timesheet…");
        _generation++; _bridge.PageUnloaded(); _requests.Clear();
        try
        {
            _url = _settings.GetWebAppUri();
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            _browser?.Dispose(); BrowserHost.Children.Clear();
            // WebView2 owns a native child window, so a WPF overlay alone cannot hide it.
            // Keep unverified website content hidden until the notifier bridge is ready.
            _browser = new WebView2 { Visibility = Visibility.Hidden }; BrowserHost.Children.Add(_browser);
            var profile = _userDataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parallel Systems", "Desktop Notifier", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (_closed) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_closed) return;
            var core = _browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDevToolsEnabled = _url.IsLoopback;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NavigationStarting += (_, args) => {
                if (!_allowNavigation || !DesktopPagePolicy.Allows(_url, args.Uri)) { args.Cancel = true; return; }
                _allowNavigation = false;
            };
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.SourceChanged += (_, _) => {
                // SPA history changes do not necessarily raise NavigationStarting.
                if (DesktopPagePolicy.Allows(_url, core.Source)) return;
                _loadTimeout.Stop(); _generation++; _bridge.PageUnloaded();
                Log("navigation", "unexpected-route");
                ShowRecovery("The website redirected away from Desktop Notifier. Deploy the hosted /desktop-notifier page and check WebAppUrl, then retry. Reminders continue in the background.");
            };
            core.NavigationCompleted += (_, args) => {
                if (!args.IsSuccess) { Log("navigation", args.WebErrorStatus.ToString()); ShowRecovery("The timesheet website could not be loaded. Check your connection and retry. Reminders continue in the background."); }
            };
            core.ProcessFailed += (_, args) => {
                _generation++; _bridge.PageUnloaded(); Log("renderer", args.ProcessFailedKind.ToString());
                ShowRecovery("The timesheet window stopped responding. Retry to reopen it. Unsaved changes may be lost.");
            };
            core.WebMessageReceived += MessageReceived;
            var request = environment.CreateWebResourceRequest(_url.AbsoluteUri, "GET", null, "Cache-Control: no-cache\r\n");
            _allowNavigation = true; core.NavigateWithWebResourceRequest(request);
            _loadTimeout.Start();
        }
        catch (WebView2RuntimeNotFoundException) { ShowRecovery("Microsoft Edge WebView2 Runtime is missing. Run the Desktop Notifier installer to install it, then retry."); }
        catch (Exception ex) { Log("initialization", ex.GetType().Name); ShowRecovery("Unable to open the timesheet website. Check WebAppUrl and the WebView2 Runtime installation, then retry."); }
        finally { _loading = false; RetryButton.IsEnabled = true; }
    }
    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_url is null || !ReferenceEquals(sender, _browser?.CoreWebView2) || !DesktopPagePolicy.Allows(_url, args.Source)) return;
        var generation = _generation;
        string? id = null;
        string? operation = null;
        var ownsRequest = false;
        try
        {
            var json = args.WebMessageAsJson;
            if (json.Length > 262144) throw new BridgeException("invalidRequest", "Request is too large.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var root = document.RootElement;
            id = root.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(id) || id.Length > 80) { id = null; throw new BridgeException("invalidRequest", "Invalid request ID."); }
            if (!_requests.Add(id)) { id = null; throw new BridgeException("invalidRequest", "Duplicate request ID."); }
            ownsRequest = true;
            if (root.GetProperty("version").GetInt32() != DesktopBridge.Version) throw new BridgeException("incompatible", "Update Desktop Notifier to use this website.");
            operation = root.GetProperty("operation").GetString() ?? "";
            var result = await _bridge.InvokeAsync(operation, root.GetProperty("payload").Clone());
            if (generation != _generation) return;
            Send(new { version = DesktopBridge.Version, id, result });
            if (operation == "ready") { _loadTimeout.Stop(); Recovery.Visibility = Visibility.Collapsed; if (_browser is not null) _browser.Visibility = Visibility.Visible; }
        }
        catch (Exception ex)
        {
            Log("bridge", ex is System.Net.Http.HttpRequestException http ? $"HttpRequestException/{http.HttpRequestError}" : ex.GetType().Name);
            var code = ex is BridgeException error ? error.Code : ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException ? "requestFailed" : "unavailable";
            var message = ex is BridgeException or ArgumentException or InvalidOperationException ? ex.Message : "The operation could not complete. Check your connection and try again.";
            if (generation == _generation && id is not null) Send(new { version = DesktopBridge.Version, id, error = new { code, message } });
            if (generation == _generation && (code == "incompatible" || operation == "initialize"))
            {
                _loadTimeout.Stop();
                ShowRecovery(code == "incompatible" ? message :
                    "The website loaded, but Desktop Notifier could not initialize. Check that ApiBaseUrl uses the correct HTTP or HTTPS address and that the API server is running, then retry. Reminders continue in the background.");
            }
        }
        finally { if (ownsRequest && generation == _generation && id is not null) _requests.Remove(id); }
    }
    private void SendEvent(string name, object? payload) => Send(new { version = DesktopBridge.Version, @event = name, payload });
    private void Send(object value)
    {
        if (_browser?.CoreWebView2 is { } core && _url is not null && DesktopPagePolicy.Allows(_url, core.Source))
            try { core.PostWebMessageAsJson(JsonSerializer.Serialize(value, AppSettings.JsonOptions)); }
            catch (Exception ex) { Log("send", ex.GetType().Name); }
    }
    private void ShowRecovery(string message)
    {
        if (_browser is not null) _browser.Visibility = Visibility.Hidden;
        RecoveryMessage.Text = message; Recovery.Visibility = Visibility.Visible;
    }
    private static void Log(string area, string detail)
    {
        try {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parallel Systems", "Desktop Notifier", "webview.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1048576) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {area}: {detail}\n");
        } catch (Exception) { }
    }
}
