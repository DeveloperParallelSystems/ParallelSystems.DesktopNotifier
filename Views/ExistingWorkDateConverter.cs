using ParallelSystems.DesktopNotifier.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ParallelSystems.DesktopNotifier.Views;

public sealed class ExistingWorkDateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is DateTime date && values[1] is MainViewModel viewModel &&
        viewModel.HasExistingWorkSessions(date);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();
}
