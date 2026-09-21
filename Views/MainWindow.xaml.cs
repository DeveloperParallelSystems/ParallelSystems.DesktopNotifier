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
    public MainWindow() => InitializeComponent();

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
