using System.Drawing;
using System.Windows.Forms;
using ParallelSystems.DesktopNotifier.Models;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    private readonly Queue<TimesheetNotificationEventArgs> _pending = new();
    private readonly System.Windows.Forms.Timer _advanceTimer;
    private TimesheetNotificationEventArgs? _activeNotification;
    public event EventHandler<NotificationOpenRequestedEventArgs>? OpenRequested;
    public event EventHandler? ExitRequested;
    public NotificationService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke(
            this, new NotificationOpenRequestedEventArgs()));
        menu.Items.Add("Dismiss current notification", null, (_, _) => Dismiss());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/logo-mark.ico"));
        if (resource?.Stream is not null)
        {
            using (resource.Stream)
            using (var sourceIcon = new Icon(resource.Stream))
                _applicationIcon = (Icon)sourceIcon.Clone();
        }
        else
        {
            _applicationIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        }
        _icon = new NotifyIcon { Icon = _applicationIcon, Text = "Parallel Systems Timesheet", Visible = true, ContextMenuStrip = menu };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke(
            this, new NotificationOpenRequestedEventArgs { WorkDate = _activeNotification?.WorkDate });
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(
            this, new NotificationOpenRequestedEventArgs());
        _advanceTimer = new System.Windows.Forms.Timer { Interval = 11000 };
        _advanceTimer.Tick += (_, _) => CompleteCurrentNotification();
    }

    public void Show(TimesheetNotificationEventArgs notification)
    {
        _pending.Enqueue(notification);
        ShowNextNotification();
    }

    private void ShowNextNotification()
    {
        if (_activeNotification is not null || _pending.Count == 0) return;
        _activeNotification = _pending.Dequeue();
        _icon.BalloonTipTitle = _activeNotification.Title;
        _icon.BalloonTipText = _activeNotification.Message;
        _icon.ShowBalloonTip(10000);
        _advanceTimer.Start();
    }

    private void CompleteCurrentNotification()
    {
        _advanceTimer.Stop();
        _activeNotification = null;
        ShowNextNotification();
    }

    public void Dismiss()
    {
        // NotifyIcon does not expose a direct close operation. Cycling visibility
        // dismisses its active balloon while preserving the tray application.
        _advanceTimer.Stop();
        _icon.Visible = false;
        _icon.Visible = true;
        _activeNotification = null;
        ShowNextNotification();
    }
    public void Dispose() { _advanceTimer.Dispose(); _icon.Visible = false; _icon.Dispose(); _applicationIcon.Dispose(); }
}
