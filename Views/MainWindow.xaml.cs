using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.ViewModels;
using ComboBox = System.Windows.Controls.ComboBox;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class MainWindow : Window
{
    private readonly System.Windows.Threading.DispatcherTimer _updateTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    public MainWindow()
    {
        InitializeComponent();
        _updateTimer.Tick+=(_,_)=>RefreshUpdatesMenu();
        Loaded+=(_,_)=>_updateTimer.Start(); Closed+=(_,_)=>_updateTimer.Stop();
    }
    private void RefreshUpdatesMenu()
    {
        var status=ParallelSystems.ProductSupport.ProductLifecycle.UpdateLabel("desktop-notifier",null);
        var action=ParallelSystems.ProductSupport.ProductLifecycle.UpdateActionLabel("desktop-notifier",null);
        ProductUpdatesButton.Content=status+" ▼";
        UpdateActionMenuItem.Header=action;
        UpdateActionMenuItem.ToolTip=status;
        UpdateActionMenuItem.Visibility=UpdateActionSeparator.Visibility=string.IsNullOrEmpty(action)?Visibility.Collapsed:Visibility.Visible;
    }
    private void UpdatesMenu_Opened(object sender, RoutedEventArgs e) => RefreshUpdatesMenu();
    private void ShowUpdatesMenu_Click(object sender, RoutedEventArgs e)
    {
        RefreshUpdatesMenu();
        ProductUpdatesButton.ContextMenu.PlacementTarget=ProductUpdatesButton;
        ProductUpdatesButton.ContextMenu.Placement=PlacementMode.Bottom;
        ProductUpdatesButton.ContextMenu.IsOpen=true;
    }
    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try { ParallelSystems.ProductSupport.ProductLifecycle.CheckForUpdates(); }
        catch { System.Windows.MessageBox.Show("Install or repair the per-user Parallel Systems Updater and try again.", "Updates"); }
    }
    private void OpenUpdater_Click(object sender, RoutedEventArgs e)
    {
        try { ParallelSystems.ProductSupport.ProductLifecycle.OpenUpdater(); }
        catch { System.Windows.MessageBox.Show("Install or repair the per-user Parallel Systems Updater and try again.", "Updates"); }
    }

    private void ExistingNameComboBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.IsKeyboardFocusWithin) return;

        var enteredName = comboBox.Text.Trim();
        var match = comboBox.Items.Cast<object>().FirstOrDefault(item =>
            string.Equals(GetName(item), enteredName, StringComparison.OrdinalIgnoreCase));

        comboBox.SetCurrentValue(ComboBox.TextProperty, match is null ? "" : GetName(match));
        comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
    }

    private static string GetName(object item) => item switch
    {
        ProjectModel project => project.Name,
        ClientModel client => client.Name,
        _ => ""
    };

    private async void WorkDatePickerCalendarOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker || DataContext is not MainViewModel viewModel) return;

        picker.ApplyTemplate();
        if (picker.Template.FindName("PART_Popup", picker) is Popup popup &&
            popup.Child is DependencyObject popupRoot &&
            FindVisualChild<Calendar>(popupRoot) is { } calendar)
        {
            calendar.DisplayDateChanged -= WorkDateCalendarDisplayDateChanged;
            calendar.DisplayDateChanged += WorkDateCalendarDisplayDateChanged;
            await viewModel.LoadCalendarIndicatorsAsync(calendar.DisplayDate);
            return;
        }

        await viewModel.LoadCalendarIndicatorsAsync(picker.DisplayDate);
    }

    private async void WorkDateCalendarDisplayDateChanged(object? sender, CalendarDateChangedEventArgs e)
    {
        if (sender is Calendar calendar && DataContext is MainViewModel viewModel)
            await viewModel.LoadCalendarIndicatorsAsync(calendar.DisplayDate);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }

        return null;
    }
}
