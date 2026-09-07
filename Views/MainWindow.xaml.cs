using System.Windows;
using System.Windows.Controls;
using ParallelSystems.DesktopNotifier.ViewModels;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void WorkDatePickerCalendarOpened(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker picker && DataContext is MainViewModel viewModel)
            await viewModel.LoadCalendarIndicatorsAsync(picker.DisplayDate);
    }
}
