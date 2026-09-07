using System.Drawing;
using System.Windows.Forms;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ManualDismissRequested;
    public NotificationService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Dismiss today's reminder", null, (_, _) =>
        {
            ManualDismissRequested?.Invoke(this, EventArgs.Empty);
            Dismiss();
        });
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
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _icon.BalloonTipClosed += (_, _) => ManualDismissRequested?.Invoke(this, EventArgs.Empty);
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }
    public void Show(string message)
    {
        _icon.BalloonTipTitle = "Daily timesheet needs your review";
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(10000);
    }
    public void Dismiss()
    {
        // NotifyIcon does not expose a direct close operation. Cycling visibility
        // dismisses its active balloon while preserving the tray application.
        _icon.Visible = false;
        _icon.Visible = true;
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _applicationIcon.Dispose(); }
}
