using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class ManualEntryPromptWindow : Window, INotifyPropertyChanged
{
    private readonly NotifierService _service;
    private bool _allowClose;
    private bool _isBusy;

    public DateOnly WorkDate { get; }
    public ObservableCollection<ProjectModel> Projects { get; } = [];
    public ObservableCollection<ClientModel> Clients { get; } = [];
    public ObservableCollection<string> LevelNames { get; } = [];
    public ObservableCollection<string> PackageNames { get; } = [];
    public ObservableCollection<string> TaskCategories { get; } = [];
    public ObservableCollection<ManualEntryPromptSession> Sessions { get; } = [];
    public ObservableCollection<HourOption> HourOptions { get; } =
        new(Enumerable.Range(1, 96).Select(index => new HourOption(index * 900, FormatDuration(index * 900))));
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditingEnabled));
        }
    }
    public bool IsEditingEnabled => !IsBusy;

    public event Action<DateOnly>? ContinueRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ManualEntryPromptWindow(NotifierService service, DateOnly workDate)
    {
        InitializeComponent();
        _service = service;
        WorkDate = workDate;
        DataContext = this;
        var workArea = SystemParameters.WorkArea;
        var width = Math.Min(Width, workArea.Width - 32);
        var height = Math.Min(Height, workArea.Height - 32);
        MinWidth = Math.Min(MinWidth, width);
        MinHeight = Math.Min(MinHeight, height);
        Width = width;
        Height = height;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPredictionAsync();
    }

    private async Task LoadPredictionAsync()
    {
        SetBusy(true);
        try
        {
            var data = await _service.GetManualEntryPromptDataAsync(WorkDate);
            Projects.Clear();
            foreach (var project in data.Projects) Projects.Add(project);
            Clients.Clear();
            foreach (var client in data.Clients) Clients.Add(client);
            ReplaceValues(LevelNames, data.Levels.Select(level => level.Name));
            ReplaceValues(PackageNames, data.PackageNames.Select(item => item.Name));
            ReplaceValues(TaskCategories, data.TaskCategories);
            Sessions.Clear();
            foreach (var prior in data.PreviousSessions)
            {
                AddHourOption(prior.EngagedSeconds);
                Sessions.Add(new ManualEntryPromptSession
                {
                    ProjectId = prior.ProjectId,
                    ClientId = prior.ClientId,
                    EngagedSeconds = prior.EngagedSeconds,
                    Level = prior.Level ?? "",
                    PackageName = prior.PackageName ?? "",
                    TaskCategory = prior.TaskCategory,
                    Notes = prior.Notes ?? ""
                });
            }
            if (Sessions.Count == 0) Sessions.Add(new ManualEntryPromptSession());
            StatusText.Text = data.PreviousSessions.Count > 0
                ? $"Prefilled {data.PreviousSessions.Count} session(s) from yesterday. Review them before submitting."
                : "No work sessions were found for yesterday. Add today's work below.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Yesterday's sessions could not be loaded: {ex.Message} Continue in the main window to enter today's work.";
        }
        finally { SetBusy(false); }
    }

    private void AddSession_Click(object sender, RoutedEventArgs e) =>
        Sessions.Add(new ManualEntryPromptSession());

    private void RemoveSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ManualEntryPromptSession session })
            Sessions.Remove(session);
    }

    private void Continue_Click(object sender, RoutedEventArgs e) =>
        ContinueRequested?.Invoke(WorkDate);

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            var result = await _service.SubmitPromptSessionsAsync(WorkDate, Sessions.ToList());
            StatusText.Text = $"Timesheet submitted: {result.CreatedManualSessions} session(s) added.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally { SetBusy(false); }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose) e.Cancel = true;
    }

    public void CloseFromApplication()
    {
        _allowClose = true;
        Close();
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        SubmitButton.IsEnabled = !value;
    }

    private void AddHourOption(int seconds)
    {
        if (HourOptions.Any(option => option.Seconds == seconds)) return;
        HourOptions.Add(new HourOption(seconds, FormatDuration(seconds)));
    }

    private static void ReplaceValues(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase)) target.Add(value);
    }

    private static string FormatDuration(int seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public sealed record HourOption(int Seconds, string Label);
}
