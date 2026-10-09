using System.Drawing;
using System.Windows.Forms;
using ParallelSystems.DesktopNotifier.Models;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private bool _updateBalloon;
    private readonly HashSet<string> _shownUpdates=new();
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
        var updates=new ToolStripMenuItem("Updates");
        menu.Items.Add(updates);
        EventHandler openUpdates=(_,_)=>{try{ParallelSystems.ProductSupport.ProductLifecycle.OpenUpdater();}catch{System.Windows.MessageBox.Show("Install or repair Parallel Systems Updater.","Updates");}};
        updates.DropDownItems.Add("Check for Updates",null,(_,_)=>{try{ParallelSystems.ProductSupport.ProductLifecycle.CheckForUpdates();}catch{System.Windows.MessageBox.Show("Install or repair Parallel Systems Updater.","Updates");}});
        var updateSeparator=new ToolStripSeparator{Visible=false};
        updates.DropDownItems.Add(updateSeparator);
        var updateAction=updates.DropDownItems.Add("",null,openUpdates);
        updateAction.Visible=false;
        void RefreshUpdateAction()
        {
            var action=ParallelSystems.ProductSupport.ProductLifecycle.UpdateActionLabel("desktop-notifier",null);
            updateAction.Text=action;
            updateAction.ToolTipText=ParallelSystems.ProductSupport.ProductLifecycle.UpdateLabel("desktop-notifier",null);
            updateAction.Visible=updateSeparator.Visible=!string.IsNullOrEmpty(action);
        }
        updates.DropDownOpening+=(_,_)=>RefreshUpdateAction();
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
        _updateTimer=new System.Windows.Forms.Timer{Interval=2000};
        _updateTimer.Tick+=(_,_)=>
        {
            var label=ParallelSystems.ProductSupport.ProductLifecycle.UpdateLabel("desktop-notifier",null);updates.Text=label;RefreshUpdateAction();
            if(_activeNotification is null && (label.StartsWith("New update ")||label.StartsWith("Ready to install ")) && _shownUpdates.Add(label))
            { _updateBalloon=true; _icon.ShowBalloonTip(10000,"Parallel Systems update",label+". Click to review.",ToolTipIcon.Info); }
        };
        _updateTimer.Start();
        _icon.BalloonTipClicked += (_, _) =>
        {
            if(_updateBalloon) { try{ParallelSystems.ProductSupport.ProductLifecycle.OpenUpdater();}catch{System.Windows.MessageBox.Show("Install or repair Parallel Systems Updater.","Updates");} }
            else OpenRequested?.Invoke(this,new NotificationOpenRequestedEventArgs{WorkDate=_activeNotification?.WorkDate});
        };
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
        _updateBalloon=false;
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
    public void Dispose() { _updateTimer.Dispose(); _advanceTimer.Dispose(); _icon.Visible = false; _icon.Dispose(); _applicationIcon.Dispose(); }
}
