using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

// The form uses the same secured WebView2 shell as the main timesheet.
public sealed class ManualEntryPromptWindow : MainWindow
{
    private bool _allowClose;
    public DateOnly WorkDate { get; }
    public event Action<DateOnly, IReadOnlyList<ManualEntryPromptSession>>? ContinueRequested;

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
        MinWidth = Math.Min(520, area.Width - 32);
        MinHeight = Math.Min(420, area.Height - 32);
        Width = Math.Min(600, area.Width - 32);
        Height = Math.Min(560, area.Height - 32);
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
        SourceInitialized += (_, _) => DisableWindowControls();
        bridge.ContinuePromptRequested += (date, sessions) => ContinueRequested?.Invoke(date, sessions);
        Closing += (_, args) => args.Cancel = !_allowClose;
        Closed += (_, _) => bridge.Dispose();
        // Recovery remains usable when the website or WebView2 cannot start.
        var continueButton = new System.Windows.Controls.Button {
            Content = "Continue in main window", HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        continueButton.Click += (_, _) => ContinueRequested?.Invoke(WorkDate, []);
        ((System.Windows.Controls.StackPanel)Recovery.Child).Children.Add(continueButton);
    }

    private void DisableWindowControls()
    {
        var handle = new WindowInteropHelper(this).Handle;
        const int styleIndex = -16, minimizeBox = 0x20000, maximizeBox = 0x10000;
        SetWindowLong(handle, styleIndex, GetWindowLong(handle, styleIndex) & ~(minimizeBox | maximizeBox));
        EnableMenuItem(GetSystemMenu(handle, false), 0xF060, 0x0001); // SC_CLOSE, MF_GRAYED
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // Refresh frame without moving, sizing or reordering.
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
    }

    private IntPtr WindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Also reject keyboard/system-menu commands; application closure remains explicit.
        if (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) is 0xF020 or 0xF030 or 0xF060)
            handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr handle, bool revert);
    [DllImport("user32.dll")]
    private static extern uint EnableMenuItem(IntPtr menu, uint item, uint enabled);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    public void CloseFromApplication()
    {
        _allowClose = true;
        Close();
    }
}
