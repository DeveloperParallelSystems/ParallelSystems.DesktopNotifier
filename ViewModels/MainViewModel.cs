using ParallelSystems.DesktopNotifier.Commands;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ParallelSystems.DesktopNotifier.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DesktopApiClient _api;
    private readonly AppSettings _settings;
    private readonly IConfirmationService _confirmation;
    private readonly NotificationStateStore _notificationStateStore;
    private readonly CancellationTokenSource _stop = new();
    private readonly NotificationProcessingState _notificationState;
    private DeviceModel? _device;
    private DateOnly? _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private string _selectedDateStatus = "Select a date to review its work sessions.";
    private bool _selectedDateHasSessions;
    private int _dateStatusRequest;
    private int _existingDatesVersion;
    private int _dataLoadCount;
    private string _status = "Starting...";
    private bool _hasError;
    private bool _busy;
    private readonly HashSet<DateOnly> _existingDateOverrides = [];
    private readonly HashSet<DateOnly> _existingWorkDates = [];
    private readonly HashSet<Guid> _deletedManualSessionIds = [];

    public ObservableCollection<ProjectModel> Projects { get; } = [];
    public ObservableCollection<ClientModel> Clients { get; } = [];
    public ObservableCollection<string> TaskCategories { get; } = [];
    public ObservableCollection<ManualSessionModel> ManualSessions { get; } = [];
    public IReadOnlyList<TimeSpan> TimeOptions { get; } =
        Enumerable.Range(0, 96).Select(index => TimeSpan.FromMinutes(index * 15)).ToList();
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SubmitCommand { get; }
    public RelayCommand AddManualCommand { get; }
    public RelayCommand RemoveManualCommand { get; }
    public event EventHandler<TimesheetNotificationEventArgs>? NotificationRequested;
    public event EventHandler? NotificationDismissRequested;

    public DateOnly? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (Set(ref _selectedDate, value))
            {
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
    public bool IsLoadingData => _dataLoadCount > 0;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { OnPropertyChanged(nameof(IsEditingEnabled)); SubmitCommand.RaiseCanExecuteChanged(); AddManualCommand.RaiseCanExecuteChanged(); } } }
    public bool IsEditingEnabled => !IsBusy;
    public double RequiredHours => _settings.RequiredHoursPerDay;
    public double ManualHours => ManualSessions.Sum(x => Math.Max(0, x.EngagedTime.TotalHours));
    public double RemainingHours => Math.Max(0, RequiredHours - ManualHours);
    public double TotalHours => ManualHours;
    public string ApplicationVersion { get; } =
        $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.1.0"}";
    public string RequiredDuration => FormatHours(RequiredHours);
    public string ManualDuration => FormatHours(ManualHours);
    public string RemainingDuration => FormatHours(RemainingHours);
    public string TotalDuration => FormatHours(TotalHours);

    public MainViewModel(DesktopApiClient api, AppSettings settings, IConfirmationService confirmation,
        NotificationStateStore notificationStateStore)
    {
        _api = api; _settings = settings; _confirmation = confirmation;
        _notificationStateStore = notificationStateStore;
        _notificationState = _notificationStateStore.Read();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () =>
            !IsBusy && SelectedDate.HasValue &&
            (ManualSessions.Count > 0 || _deletedManualSessionIds.Count > 0));
        AddManualCommand = new RelayCommand(_ => AddManual(), _ => SelectedDate.HasValue && !IsBusy);
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
        BeginDataLoad();
        try
        {
            _device = await _api.EnsureDeviceAsync(Environment.MachineName);
            var projectsTask = _api.GetProjectsAsync();
            var clientsTask = _api.GetClientsAsync();
            var categoriesTask = _api.GetTaskCategoriesAsync();
            try { await ProcessDueNotificationsAsync(DateTime.Now); }
            catch (Exception ex) { SetStatus($"Notification check failed: {ex.Message}", true); }
            foreach (var project in await projectsTask) Projects.Add(project);
            foreach (var client in await clientsTask) Clients.Add(client);
            foreach (var category in await categoriesTask) TaskCategories.Add(category);
            await RefreshSelectedDateStatusAsync(SelectedDate);
            _ = PollAsync(_stop.Token);
            if (!HasError)
                SetStatus($"Monitoring {Environment.MachineName}. Morning: {_settings.MorningNotificationAt:HH:mm}; afternoon: {_settings.AfternoonNotificationAt:HH:mm}.");
        }
        finally { EndDataLoad(); }
    }

    public void OpenFromNotification(DateOnly? workDate = null)
    {
        if (_device is null) return;
        var targetDate = workDate ?? DateOnly.FromDateTime(DateTime.Today);
        if (SelectedDate == targetDate)
            _ = RefreshSelectedDateStatusAsync(targetDate);
        else
            SelectedDate = targetDate;
    }

    private async Task ProcessDueNotificationsAsync(DateTime now)
    {
        if (_device is null) return;
        var processingDate = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        if (_notificationState.MorningProcessedDate != processingDate &&
            currentTime >= _settings.MorningNotificationAt)
        {
            await ProcessMorningNotificationAsync(
                processingDate,
                processingDate.AddDays(-1),
                "Morning timesheet reminder",
                "No manual timesheet exists for yesterday. Click to add one.");
        }

        if (_notificationState.AfternoonProcessedDate != processingDate &&
            currentTime >= _settings.AfternoonNotificationAt)
        {
            ProcessAfternoonReminder(processingDate);
        }
    }

    private async Task ProcessMorningNotificationAsync(
        DateOnly processingDate, DateOnly workDate, string title, string message)
    {
        var result = await _api.GetStatusAsync(_device!.Id, workDate);
        _notificationState.MorningProcessedDate = processingDate;
        _notificationStateStore.Write(_notificationState);

        if (result.AdditionalSeconds <= 0)
        {
            NotificationRequested?.Invoke(this, new TimesheetNotificationEventArgs
            {
                WorkDate = workDate,
                Title = title,
                Message = message
            });
        }
    }

    private void ProcessAfternoonReminder(DateOnly processingDate)
    {
        _notificationState.AfternoonProcessedDate = processingDate;
        _notificationStateStore.Write(_notificationState);
        NotificationRequested?.Invoke(this, new TimesheetNotificationEventArgs
        {
            WorkDate = processingDate,
            Title = "Today's timesheet reminder",
            Message = "Don't forget to fill in your timesheet for today. Click here to open it."
        });
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(GetNextNotificationCheckDelay(DateTime.Now), token);
                try { await ProcessDueNotificationsAsync(DateTime.Now); }
                catch (HttpRequestException ex) { SetStatus($"Notification check failed: {ex.Message}", true); }
                catch (TaskCanceledException) when (!token.IsCancellationRequested) { SetStatus("Notification check timed out. Retrying automatically.", true); }
                catch (Exception ex) { SetStatus($"Notification check failed: {ex.Message}", true); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private TimeSpan GetNextNotificationCheckDelay(DateTime now)
    {
        var processingDate = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);
        var pollingDelay = TimeSpan.FromMinutes(_settings.CheckIntervalMinutes);
        var retryDelay = TimeSpan.FromMinutes(1);

        var morningIsOverdue = _notificationState.MorningProcessedDate != processingDate &&
                               currentTime >= _settings.MorningNotificationAt;
        var afternoonIsOverdue = _notificationState.AfternoonProcessedDate != processingDate &&
                                 currentTime >= _settings.AfternoonNotificationAt;
        if (morningIsOverdue || afternoonIsOverdue)
            return pollingDelay < retryDelay ? pollingDelay : retryDelay;

        var morning = now.Date.Add(_settings.MorningNotificationAt.ToTimeSpan());
        if (morning <= now || _notificationState.MorningProcessedDate == processingDate)
            morning = morning.AddDays(1);

        var afternoon = now.Date.Add(_settings.AfternoonNotificationAt.ToTimeSpan());
        if (afternoon <= now || _notificationState.AfternoonProcessedDate == processingDate)
            afternoon = afternoon.AddDays(1);

        var scheduledDelay = (morning < afternoon ? morning : afternoon) - now;
        return scheduledDelay < pollingDelay ? scheduledDelay : pollingDelay;
    }

    private async Task RefreshAsync()
    {
        if (_device is null) return;
        IsBusy = true;
        BeginDataLoad();
        try
        {
            SelectedDate ??= DateOnly.FromDateTime(DateTime.Today);
            var clientsTask = _api.GetClientsAsync();
            await RefreshSelectedDateStatusAsync(SelectedDate);
            var clients = await clientsTask;
            Clients.Clear();
            foreach (var client in clients) Clients.Add(client);
            SetStatus(ManualSessions.Count == 0
                ? "No manual sessions for the selected date."
                : $"Loaded {ManualSessions.Count} manual session(s).");
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { EndDataLoad(); IsBusy = false; }
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
        BeginDataLoad();
        try
        {
            var statusTask = _api.GetStatusAsync(_device.Id, date.Value);
            var manualSessionsTask = _api.GetManualSessionsAsync(_device.Id, date.Value);
            await Task.WhenAll(statusTask, manualSessionsTask);
            if (request != _dateStatusRequest) return;
            var status = await statusTask;
            if (status.HasDailySession) _existingDateOverrides.Add(date.Value);
            else _existingDateOverrides.Remove(date.Value);
            ManualSessions.Clear();
            var existingManualSessions = await manualSessionsTask;
            foreach (var existing in existingManualSessions)
            {
                var row = new ManualSessionModel
                {
                    Id = existing.Id,
                    ProjectName = existing.ProjectName,
                    EngagedTime = TimeSpan.FromSeconds(existing.EngagedSeconds),
                    TaskCategory = existing.TaskCategory,
                    ClientName = existing.ClientName,
                    Level = existing.Level,
                    Notes = existing.Notes
                };
                row.PropertyChanged += (_, _) => RaiseSummary();
                ManualSessions.Add(row);
            }
            SelectedDateHasSessions = existingManualSessions.Count > 0;
            SelectedDateStatus = existingManualSessions.Count > 0
                ? $"{existingManualSessions.Count} manual session(s) already saved for this date."
                : "No manual sessions have been saved for this date.";
        }
        catch (Exception ex)
        {
            if (request == _dateStatusRequest)
                SelectedDateStatus = $"Could not check existing sessions: {ex.Message}";
        }
        finally { EndDataLoad(); }
    }

    public bool HasExistingWorkSessions(DateTime date) =>
        _existingWorkDates.Contains(DateOnly.FromDateTime(date));

    public async Task LoadCalendarIndicatorsAsync(DateTime displayDate)
    {
        if (_device is null) return;
        var monthStart = new DateOnly(displayDate.Year, displayDate.Month, 1);
        var start = monthStart.AddDays(-7);
        var end = monthStart.AddMonths(1).AddDays(7);
        BeginDataLoad();
        try
        {
            var dates = await _api.GetSessionDatesAsync(_device.Id, start, end);
            foreach (var date in dates) _existingWorkDates.Add(date);
            ExistingDatesVersion++;
        }
        catch (Exception ex) { SetStatus($"Could not load calendar indicators: {ex.Message}", true); }
        finally { EndDataLoad(); }
    }

    private async Task SubmitAsync()
    {
        if (_device is null || !SelectedDate.HasValue) return;
        var workDate = SelectedDate.Value;
        IsBusy = true;
        try
        {
            var selectedDateStatus = await _api.GetStatusAsync(_device.Id, workDate);
            if (selectedDateStatus.HasDailySession) _existingDateOverrides.Add(workDate);
            else _existingDateOverrides.Remove(workDate);

            foreach (var item in ManualSessions)
            {
                if (string.IsNullOrWhiteSpace(item.ProjectName)) throw new InvalidOperationException("Project is required for every added session.");
                if (item.EngagedTime <= TimeSpan.Zero || item.EngagedTime > TimeSpan.FromHours(24))
                    throw new InvalidOperationException("Engaged time must be greater than zero and no more than 24 hours.");
                if (string.IsNullOrWhiteSpace(item.TaskCategory)) throw new InvalidOperationException("Task category is required for every added session.");
                if (string.IsNullOrWhiteSpace(item.ClientName)) throw new InvalidOperationException("Client is required for every added session.");
            }
            if (!_confirmation.ConfirmDailySubmission(
                    workDate, ManualSessions.Count, TotalDuration))
            {
                SetStatus("Submission cancelled.");
                return;
            }
            var request = new DailySubmitRequest
            {
                DeviceId = _device.Id, WorkDate = workDate,
                AllowExistingDate = _existingDateOverrides.Contains(workDate),
                IncludeTrackedSessions = false,
                DeletedManualSessionIds = _deletedManualSessionIds.ToList(),
                ManualSessions = ManualSessions.Select(x =>
                {
                    var day = workDate.ToDateTime(TimeOnly.MinValue);
                    var start = DateTime.SpecifyKind(day.AddHours(9), DateTimeKind.Local);
                    var end = start.Add(x.EngagedTime);
                    return new ManualSubmitModel
                    {
                        Id = x.Id,
                        ProjectId = Projects.FirstOrDefault(project =>
                            string.Equals(project.Name, x.ProjectName.Trim(), StringComparison.OrdinalIgnoreCase))?.Id,
                        ProjectName = x.ProjectName.Trim(),
                        ClientId = Clients.FirstOrDefault(client =>
                            string.Equals(client.Name, x.ClientName.Trim(), StringComparison.OrdinalIgnoreCase))?.Id,
                        ClientName = x.ClientName.Trim(),
                        Level = string.IsNullOrWhiteSpace(x.Level) ? null : x.Level.Trim(),
                        StartedAtUtc = new DateTimeOffset(start.ToUniversalTime(), TimeSpan.Zero),
                        EndedAtUtc = new DateTimeOffset(end.ToUniversalTime(), TimeSpan.Zero),
                        TaskCategory = x.TaskCategory.Trim(), Notes = x.Notes
                    };
                }).ToList()
            };
            var result = await _api.SubmitAsync(request);
            NotificationDismissRequested?.Invoke(this, EventArgs.Empty);
            SetStatus($"Manual sessions saved: {result.CreatedManualSessions} added, {result.UpdatedManualSessions} updated, {result.DeletedManualSessions} removed.");
            _existingDateOverrides.Remove(workDate);
            _deletedManualSessionIds.Clear();
            ManualSessions.Clear();
            await RefreshAsync();
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
        finally { IsBusy = false; }
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ManualHours));
        OnPropertyChanged(nameof(RemainingHours));
        OnPropertyChanged(nameof(TotalHours));
        OnPropertyChanged(nameof(RequiredDuration));
        OnPropertyChanged(nameof(ManualDuration));
        OnPropertyChanged(nameof(RemainingDuration));
        OnPropertyChanged(nameof(TotalDuration));
    }
    private static string FormatHours(double hours)
    {
        var duration = TimeSpan.FromHours(Math.Max(0, hours));
        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
    private void SetStatus(string message, bool isError = false)
    {
        Status = message;
        HasError = isError;
    }
    private void BeginDataLoad()
    {
        if (_dataLoadCount++ == 0) OnPropertyChanged(nameof(IsLoadingData));
    }
    private void EndDataLoad()
    {
        if (_dataLoadCount > 0 && --_dataLoadCount == 0)
            OnPropertyChanged(nameof(IsLoadingData));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { _stop.Cancel(); _stop.Dispose(); }
}
