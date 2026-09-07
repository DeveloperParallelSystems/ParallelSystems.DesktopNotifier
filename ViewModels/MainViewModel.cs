using ParallelSystems.DesktopNotifier.Commands;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ParallelSystems.DesktopNotifier.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DesktopApiClient _api;
    private readonly AppSettings _settings;
    private readonly IConfirmationService _confirmation;
    private readonly ReminderDismissalStore _dismissalStore;
    private readonly CancellationTokenSource _stop = new();
    private DeviceModel? _device;
    private DateOnly? _selectedDate;
    private string _selectedDateStatus = "Select a date to review its work sessions.";
    private bool _selectedDateHasSessions;
    private int _dateStatusRequest;
    private int _existingDatesVersion;
    private string _status = "Starting...";
    private bool _hasError;
    private bool _busy;
    private DateOnly? _dismissedReminderDate;
    private readonly HashSet<DateOnly> _existingDateOverrides = [];
    private readonly HashSet<DateOnly> _existingWorkDates = [];
    private readonly HashSet<Guid> _deletedManualSessionIds = [];

    public ObservableCollection<DailyDraftModel> TrackedSessions { get; } = [];
    public ObservableCollection<ProjectModel> Projects { get; } = [];
    public ObservableCollection<string> TaskCategories { get; } = [];
    public ObservableCollection<ManualSessionModel> ManualSessions { get; } = [];
    public IReadOnlyList<TimeSpan> TimeOptions { get; } =
        Enumerable.Range(0, 96).Select(index => TimeSpan.FromMinutes(index * 15)).ToList();
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SubmitCommand { get; }
    public RelayCommand AddManualCommand { get; }
    public RelayCommand RemoveManualCommand { get; }
    public event EventHandler<string>? NotificationRequested;
    public event EventHandler? NotificationDismissRequested;

    public DateOnly? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (Set(ref _selectedDate, value))
            {
                ShowSelectedDate();
                ManualSessions.Clear();
                _deletedManualSessionIds.Clear();
                AddManualCommand.RaiseCanExecuteChanged();
                SubmitCommand.RaiseCanExecuteChanged();
                RaiseSummary();
                OnPropertyChanged(nameof(SelectedWorkDate));
                _ = RefreshSelectedDateStatusAsync(value);
            }
        }
    }
    public DateTime? SelectedWorkDate
    {
        get => SelectedDate?.ToDateTime(TimeOnly.MinValue);
        set => SelectedDate = value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
    }
    public DateTime LatestWorkDate => DateTime.Today;
    public string SelectedDateStatus { get => _selectedDateStatus; private set => Set(ref _selectedDateStatus, value); }
    public bool SelectedDateHasSessions { get => _selectedDateHasSessions; private set => Set(ref _selectedDateHasSessions, value); }
    public int ExistingDatesVersion { get => _existingDatesVersion; private set => Set(ref _existingDatesVersion, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) SubmitCommand.RaiseCanExecuteChanged(); } }
    public double RequiredHours => _settings.RequiredHoursPerDay;
    public double TrackedHours => TrackedSessions.Sum(x => x.EngagedSeconds) / 3600d;
    public double ManualHours => ManualSessions.Sum(x => Math.Max(0, x.EngagedTime.TotalHours));
    public double RemainingHours => Math.Max(0, RequiredHours - TrackedHours - ManualHours);
    public double TotalHours => TrackedHours + ManualHours;
    public string RequiredDuration => FormatHours(RequiredHours);
    public string TrackedDuration => FormatHours(TrackedHours);
    public string ManualDuration => FormatHours(ManualHours);
    public string RemainingDuration => FormatHours(RemainingHours);
    public string TotalDuration => FormatHours(TotalHours);

    public MainViewModel(DesktopApiClient api, AppSettings settings, IConfirmationService confirmation,
        ReminderDismissalStore dismissalStore)
    {
        _api = api; _settings = settings; _confirmation = confirmation;
        _dismissalStore = dismissalStore;
        _dismissedReminderDate = _dismissalStore.Read();
        RefreshCommand = new AsyncRelayCommand(LoadDraftsAsync);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () =>
            !IsBusy && SelectedDate.HasValue &&
            (TrackedSessions.Count > 0 || ManualSessions.Count > 0 || _deletedManualSessionIds.Count > 0));
        AddManualCommand = new RelayCommand(_ => AddManual(), _ => SelectedDate.HasValue);
        RemoveManualCommand = new RelayCommand(value =>
        {
            if (value is not ManualSessionModel row) return;
            if (row.Id.HasValue) _deletedManualSessionIds.Add(row.Id.Value);
            ManualSessions.Remove(row);
            RaiseSummary();
        });
        ManualSessions.CollectionChanged += (_, _) => { RaiseSummary(); SubmitCommand.RaiseCanExecuteChanged(); };
    }

    public async Task InitializeAsync()
    {
        _device = await _api.EnsureDeviceAsync(Environment.MachineName);
        var projectsTask = _api.GetProjectsAsync();
        var categoriesTask = _api.GetTaskCategoriesAsync();
        foreach (var project in await projectsTask) Projects.Add(project);
        foreach (var category in await categoriesTask) TaskCategories.Add(category);
        await CheckYesterdayAsync();
        _ = PollAsync(_stop.Token);
        SetStatus($"Monitoring {Environment.MachineName} every {_settings.CheckIntervalMinutes} minutes.");
    }

    public async Task OpenFromNotificationAsync()
    {
        if (_device is null) return;
        await LoadDraftsAsync();
    }

    public void DismissCurrentReminder()
    {
        _dismissedReminderDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        _dismissalStore.Write(_dismissedReminderDate.Value);
        SetStatus("Today's reminder was dismissed. The next daily reminder will be tomorrow.");
    }

    private async Task CheckYesterdayAsync()
    {
        if (_device is null) return;
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        // Re-read on every poll so deleting the dismissal file re-enables the
        // reminder without requiring the notifier to be restarted.
        _dismissedReminderDate = _dismissalStore.Read();
        var result = await _api.GetStatusAsync(_device.Id, yesterday);
        if (_dismissedReminderDate == yesterday) return;
        if (!result.HasDailySession)
            NotificationRequested?.Invoke(this, "No daily work session exists for yesterday. Click to review, or close (X) to dismiss for today.");
        else if (result.HasDraft)
            NotificationRequested?.Invoke(this, $"You have {result.DraftCount} pending work session(s) for yesterday. Click to review, or close (X) to dismiss for today.");
        else
            //NotificationRequested?.Invoke(this, "No daily work session exists for yesterday. Click to review, or close (X) to dismiss for today.");
        NotificationDismissRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task PollAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.CheckIntervalMinutes));
        try { while (await timer.WaitForNextTickAsync(token)) await CheckYesterdayAsync(); }
        catch (OperationCanceledException) { }
        catch { }
    }

    private List<DailyDraftModel> _allDrafts = [];
    private async Task LoadDraftsAsync()
    {
        if (_device is null) return;
        IsBusy = true;
        try
        {
            _allDrafts = await _api.GetDraftsAsync(_device.Id);
            var oldDate = SelectedDate;
            SelectedDate = oldDate ?? DateOnly.FromDateTime(DateTime.Today);
            ShowSelectedDate();
            await RefreshSelectedDateStatusAsync(SelectedDate);
            SetStatus(_allDrafts.Count == 0 ? "No pending daily work sessions." : $"{_allDrafts.Count} pending tracked item(s).");
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { IsBusy = false; }
    }

    private void ShowSelectedDate()
    {
        TrackedSessions.Clear();
        if (SelectedDate.HasValue)
            foreach (var row in _allDrafts.Where(x => x.WorkDate == SelectedDate.Value)) TrackedSessions.Add(row);
        SubmitCommand.RaiseCanExecuteChanged(); RaiseSummary();
    }

    private void AddManual()
    {
        if (!SelectedDate.HasValue) return;
        var duration = TimeSpan.FromHours(Math.Clamp(Math.Max(1, RemainingHours), 0.25, 23.75));
        var row = new ManualSessionModel { EngagedTime = duration };
        row.PropertyChanged += (_, _) => RaiseSummary();
        ManualSessions.Add(row);
    }

    private async Task RefreshSelectedDateStatusAsync(DateOnly? date)
    {
        var request = ++_dateStatusRequest;
        SelectedDateHasSessions = false;
        if (!date.HasValue || _device is null)
        {
            SelectedDateStatus = "Select a date to review its work sessions.";
            return;
        }
        if (date.Value > DateOnly.FromDateTime(DateTime.Today))
        {
            SelectedDateStatus = "Choose today or an earlier date.";
            return;
        }
        try
        {
            var statusTask = _api.GetStatusAsync(_device.Id, date.Value);
            var manualSessionsTask = _api.GetManualSessionsAsync(_device.Id, date.Value);
            await Task.WhenAll(statusTask, manualSessionsTask);
            if (request != _dateStatusRequest) return;
            var status = await statusTask;
            SelectedDateHasSessions = status.HasDailySession;
            SelectedDateStatus = status.HasDailySession
                ? $"Already has {status.ExistingSessionCount} work session(s) ({FormatHours(status.TotalSeconds / 3600d)})."
                : "No work sessions have been added for this date.";
            if (status.HasDailySession) _existingDateOverrides.Add(date.Value);
            else _existingDateOverrides.Remove(date.Value);
            ManualSessions.Clear();
            foreach (var existing in await manualSessionsTask)
            {
                var row = new ManualSessionModel
                {
                    Id = existing.Id,
                    ProjectName = existing.ProjectName,
                    EngagedTime = TimeSpan.FromSeconds(existing.EngagedSeconds),
                    TaskCategory = existing.TaskCategory,
                    Notes = existing.Notes
                };
                row.PropertyChanged += (_, _) => RaiseSummary();
                ManualSessions.Add(row);
            }
        }
        catch (Exception ex)
        {
            if (request == _dateStatusRequest)
                SelectedDateStatus = $"Could not check existing sessions: {ex.Message}";
        }
    }

    public bool HasExistingWorkSessions(DateTime date) =>
        _existingWorkDates.Contains(DateOnly.FromDateTime(date));

    public async Task LoadCalendarIndicatorsAsync(DateTime displayDate)
    {
        if (_device is null) return;
        var monthStart = new DateOnly(displayDate.Year, displayDate.Month, 1);
        var start = monthStart.AddDays(-7);
        var end = monthStart.AddMonths(1).AddDays(7);
        try
        {
            var dates = await _api.GetSessionDatesAsync(_device.Id, start, end);
            foreach (var date in dates) _existingWorkDates.Add(date);
            ExistingDatesVersion++;
        }
        catch (Exception ex) { SetStatus($"Could not load calendar indicators: {ex.Message}", true); }
    }

    private async Task SubmitAsync()
    {
        if (_device is null || !SelectedDate.HasValue) return;
        IsBusy = true;
        try
        {
            var selectedDateStatus = await _api.GetStatusAsync(_device.Id, SelectedDate.Value);
            if (selectedDateStatus.HasDailySession) _existingDateOverrides.Add(SelectedDate.Value);
            else _existingDateOverrides.Remove(SelectedDate.Value);

            var uncategorizedTracked = TrackedSessions
                .Where(item => item.ProjectId.HasValue && string.IsNullOrWhiteSpace(item.TaskCategory))
                .ToList();
            if (uncategorizedTracked.Count > 0)
                throw new InvalidOperationException(
                    $"Task category is required for all recorded sessions. " +
                    $"Categorize the remaining {uncategorizedTracked.Count} session(s) before submitting.");

            foreach (var item in ManualSessions)
            {
                if (string.IsNullOrWhiteSpace(item.ProjectName)) throw new InvalidOperationException("Project is required for every added session.");
                if (item.EngagedTime <= TimeSpan.Zero || item.EngagedTime > TimeSpan.FromHours(24))
                    throw new InvalidOperationException("Engaged time must be greater than zero and no more than 24 hours.");
                if (string.IsNullOrWhiteSpace(item.TaskCategory)) throw new InvalidOperationException("Task category is required for every added session.");
            }
            if (!_confirmation.ConfirmDailySubmission(
                    SelectedDate.Value, TrackedDuration, ManualDuration, TotalDuration))
            {
                SetStatus("Submission cancelled.");
                return;
            }
            var request = new DailySubmitRequest
            {
                DeviceId = _device.Id, WorkDate = SelectedDate.Value,
                AllowExistingDate = _existingDateOverrides.Contains(SelectedDate.Value),
                DeletedManualSessionIds = _deletedManualSessionIds.ToList(),
                TrackedSessions = TrackedSessions.Select(x => new TrackedClassificationModel
                {
                    DailyWorkSessionId = x.Id,
                    Scope = string.IsNullOrWhiteSpace(x.DetectedScope) ? null : x.DetectedScope.Trim(),
                    TaskCategory = string.IsNullOrWhiteSpace(x.TaskCategory) ? null : x.TaskCategory.Trim(),
                    Notes = string.IsNullOrWhiteSpace(x.Notes) ? null : x.Notes.Trim()
                }).ToList(),
                ManualSessions = ManualSessions.Select(x =>
                {
                    var day = SelectedDate.Value.ToDateTime(TimeOnly.MinValue);
                    var start = DateTime.SpecifyKind(day.AddHours(9), DateTimeKind.Local);
                    var end = start.Add(x.EngagedTime);
                    return new ManualSubmitModel
                    {
                        Id = x.Id,
                        ProjectId = Projects.FirstOrDefault(project =>
                            string.Equals(project.Name, x.ProjectName.Trim(), StringComparison.OrdinalIgnoreCase))?.Id,
                        ProjectName = x.ProjectName.Trim(),
                        StartedAtUtc = new DateTimeOffset(start.ToUniversalTime(), TimeSpan.Zero),
                        EndedAtUtc = new DateTimeOffset(end.ToUniversalTime(), TimeSpan.Zero),
                        TaskCategory = x.TaskCategory.Trim(), Notes = x.Notes
                    };
                }).ToList()
            };
            var result = await _api.SubmitAsync(request);
            NotificationDismissRequested?.Invoke(this, EventArgs.Empty);
            SetStatus($"Submitted {result.SubmittedTrackedSessions} tracked; manual sessions: {result.CreatedManualSessions} added, {result.UpdatedManualSessions} updated, {result.DeletedManualSessions} removed.");
            _existingDateOverrides.Remove(SelectedDate.Value);
            _deletedManualSessionIds.Clear();
            ManualSessions.Clear();
            await LoadDraftsAsync();
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { IsBusy = false; }
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(TrackedHours));
        OnPropertyChanged(nameof(ManualHours));
        OnPropertyChanged(nameof(RemainingHours));
        OnPropertyChanged(nameof(TotalHours));
        OnPropertyChanged(nameof(RequiredDuration));
        OnPropertyChanged(nameof(TrackedDuration));
        OnPropertyChanged(nameof(ManualDuration));
        OnPropertyChanged(nameof(RemainingDuration));
        OnPropertyChanged(nameof(TotalDuration));
    }
    private static string FormatHours(double hours) =>
        TimeSpan.FromHours(Math.Max(0, hours)).ToString(@"hh\:mm\:ss");
    private void SetStatus(string message, bool isError = false)
    {
        Status = message;
        HasError = isError;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
