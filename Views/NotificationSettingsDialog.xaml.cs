using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class NotificationSettingsDialog : Window
{
    private readonly DesktopApiClient _api;
    private Guid? _deviceId;
    private bool _savingProfile;
    private bool _invalidBirthDate;
    public TimeOnly MorningTime { get; private set; }
    public TimeOnly EveningTime { get; private set; }

    public NotificationSettingsDialog(TimeOnly morning, TimeOnly evening, DesktopApiClient api)
    {
        InitializeComponent();
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height);
        _api = api;
        BirthDatePicker.DisplayDateEnd = DateTime.Today;
        BirthDatePicker.DateValidationError += (_, args) =>
        {
            args.ThrowException = false;
            _invalidBirthDate = true;
            ProfileStatusText.Text = "Enter a valid birth date that is not in the future, or clear the field.";
        };
        BirthDatePicker.SelectedDateChanged += (_, _) => _invalidBirthDate = false;
        Loaded += async (_, _) => await LoadProfileAsync();
        Closing += (_, args) => args.Cancel = _savingProfile;
        MorningTime = morning;
        EveningTime = evening;
        MorningTimeTextBox.Text = morning.ToString("HH:mm", CultureInfo.InvariantCulture);
        EveningTimeTextBox.Text = evening.ToString("HH:mm", CultureInfo.InvariantCulture);
        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private async void ReloadProfileClicked(object sender, RoutedEventArgs e) => await LoadProfileAsync();

    private async Task LoadProfileAsync()
    {
        ProfileForm.IsEnabled = false;
        ReloadProfileButton.IsEnabled = false;
        ProfileStatusText.Text = "Loading personal information…";
        _deviceId = null;
        try
        {
            var device = await _api.EnsureDeviceAsync(Environment.MachineName);
            var profile = await _api.GetPersonalInformationAsync(device.Id);
            _deviceId = device.Id;
            FirstNameTextBox.Text = profile.FirstName ?? "";
            MiddleNameTextBox.Text = profile.MiddleName ?? "";
            LastNameTextBox.Text = profile.LastName ?? "";
            SuffixTextBox.Text = profile.Suffix ?? "";
            GenderTextBox.Text = profile.Gender ?? "";
            BirthDatePicker.SelectedDate = profile.BirthDate;
            _invalidBirthDate = false;
            CivilStatusTextBox.Text = profile.CivilStatus ?? "";
            NationalityTextBox.Text = profile.Nationality ?? "";
            AddressTextBox.Text = profile.Address ?? "";
            ProfileStatusText.Text = "Changes are saved to your employee record.";
            ProfileForm.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = $"Unable to load personal information. {ex.Message}";
        }
        finally { ReloadProfileButton.IsEnabled = true; }
    }

    private async void SaveProfileClicked(object sender, RoutedEventArgs e)
    {
        if (_deviceId is not Guid deviceId || _savingProfile) return;
        if (_invalidBirthDate && !string.IsNullOrWhiteSpace(BirthDatePicker.Text))
        {
            ProfileStatusText.Text = "Re-enter a valid birth date, or clear the field before saving.";
            BirthDatePicker.Focus();
            return;
        }
        if (BirthDatePicker.SelectedDate?.Date > DateTime.Today)
        {
            ProfileStatusText.Text = "Birth date cannot be in the future.";
            return;
        }
        var profile = new EmployeePersonalInformation
        {
            FirstName = FirstNameTextBox.Text.Trim(), MiddleName = MiddleNameTextBox.Text.Trim(),
            LastName = LastNameTextBox.Text.Trim(), Suffix = SuffixTextBox.Text.Trim(),
            Gender = GenderTextBox.Text.Trim(), BirthDate = BirthDatePicker.SelectedDate,
            CivilStatus = CivilStatusTextBox.Text.Trim(), Nationality = NationalityTextBox.Text.Trim(),
            Address = AddressTextBox.Text.Trim()
        };
        _savingProfile = true;
        ProfileForm.IsEnabled = false;
        ReloadProfileButton.IsEnabled = false;
        ProfileStatusText.Text = "Saving personal information…";
        try
        {
            await _api.SavePersonalInformationAsync(deviceId, profile);
            ProfileStatusText.Text = "Personal information saved.";
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = $"Unable to save personal information. {ex.Message}";
        }
        finally
        {
            _savingProfile = false;
            ProfileForm.IsEnabled = true;
            ReloadProfileButton.IsEnabled = true;
        }
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_savingProfile) return;
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

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        if (!_savingProfile) DialogResult = false;
    }
}
