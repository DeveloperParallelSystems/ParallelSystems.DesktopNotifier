using System.Windows;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

// The form uses the same secured WebView2 shell as the main timesheet.
public sealed class ManualEntryPromptWindow : MainWindow
{
    private bool _allowClose;
    public DateOnly WorkDate { get; }
    public event Action<DateOnly>? ContinueRequested;

    public ManualEntryPromptWindow(NotifierService service, DesktopApiClient api, DateOnly workDate, string? userDataFolder = null)
        : this(service, new DesktopBridge(api, service, workDate), workDate, userDataFolder) { }

    private ManualEntryPromptWindow(NotifierService service, DesktopBridge bridge, DateOnly workDate, string? userDataFolder)
        : base(service.Settings, bridge, userDataFolder)
    {
        WorkDate = workDate;
        Title = "Record today's work";
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(600, area.Width - 32);
        MinHeight = Math.Min(560, area.Height - 32);
        Width = Math.Min(720, area.Width - 32);
        Height = Math.Min(760, area.Height - 32);
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
        bridge.ContinuePromptRequested += date => ContinueRequested?.Invoke(date);
        Closing += (_, args) => args.Cancel = !_allowClose;
        Closed += (_, _) => bridge.Dispose();
        // Recovery remains usable when the website or WebView2 cannot start.
        var continueButton = new System.Windows.Controls.Button {
            Content = "Continue in main window", HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        continueButton.Click += (_, _) => ContinueRequested?.Invoke(WorkDate);
        ((System.Windows.Controls.StackPanel)Recovery.Child).Children.Add(continueButton);
    }

    public void CloseFromApplication()
    {
        _allowClose = true;
        Close();
    }
}
