using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class NotificationSettingsDialog : Window
{
    public TimeOnly MorningTime { get; private set; }
    public TimeOnly EveningTime { get; private set; }

    public NotificationSettingsDialog(TimeOnly morning, TimeOnly evening)
    {
        InitializeComponent();
        MorningTime = morning;
        EveningTime = evening;
        MorningTimeTextBox.Text = morning.ToString("HH:mm", CultureInfo.InvariantCulture);
        EveningTimeTextBox.Text = evening.ToString("HH:mm", CultureInfo.InvariantCulture);
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryParseTime(MorningTimeTextBox.Text, out var morning))
        {
            ShowValidationMessage("Enter a valid morning time in HH:mm format.", MorningTimeTextBox);
            return;
        }
        if (morning.Hour >= 12)
        {
            ShowValidationMessage("Morning notification time must be before 12:00.", MorningTimeTextBox);
            return;
        }
        if (!TryParseTime(EveningTimeTextBox.Text, out var evening))
        {
            ShowValidationMessage("Enter a valid evening time in HH:mm format.", EveningTimeTextBox);
            return;
        }
        if (evening.Hour < 12)
        {
            ShowValidationMessage("Evening notification time must be 12:00 or later.", EveningTimeTextBox);
            return;
        }

        MorningTime = morning;
        EveningTime = evening;
        DialogResult = true;
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static void ShowValidationMessage(string message, System.Windows.Controls.TextBox textBox)
    {
        System.Windows.MessageBox.Show(message, "Notification schedule", MessageBoxButton.OK, MessageBoxImage.Warning);
        textBox.Focus();
        textBox.SelectAll();
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
