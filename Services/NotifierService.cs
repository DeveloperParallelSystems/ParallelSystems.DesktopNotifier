using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.ViewModels;

namespace ParallelSystems.DesktopNotifier.Services;

// Owned by the application dispatcher, never by a browser page or a window's visibility.
public sealed class NotifierService : IDisposable
{
    private readonly DesktopApiClient _api;
    private readonly NotificationStateStore _store;
    private readonly NotificationProcessingState _state;
    private readonly CancellationTokenSource _stop = new();
    private readonly UpdateShutdownGate _gate = new();
    private Task<DeviceModel>? _device;
    private Task _poll = Task.CompletedTask;
    private DateOnly? _entryPromptRaisedDate;
    private DateOnly? _entryPromptDeferredDate;
    private DateOnly? _entryPromptSuppressedDate;
    public AppSettings Settings { get; }
    public MissingTimesheetAlertViewModel Alert { get; }
    public string Status { get; private set; } = "Starting…";
    public bool IsQuiescing => _gate.IsQuiescing;
    public event EventHandler<TimesheetNotificationEventArgs>? NotificationRequested;
    public event EventHandler? NotificationDismissRequested;
    public event Action<DateOnly>? EntryPromptRequested;
    public event Action<DateOnly>? EntryPromptDismissRequested;
    public event Action<DateOnly>? PromptSubmitted;
    public event EventHandler? Changed;

    public NotifierService(DesktopApiClient api, AppSettings settings, NotificationStateStore store)
    {
        _api = api; Settings = settings; _store = store; _state = store.Read();
        Alert = new MissingTimesheetAlertViewModel(async date =>
            (await api.GetStatusAsync((await GetDeviceAsync()).Id, date)).AdditionalSeconds > 0,
            _state.DismissedMissingTimesheetWorkDate);
        Alert.PropertyChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public IDisposable EnterOperation() => _gate.TryEnter() ?? throw new InvalidOperationException("An update is being installed. Try again when it finishes.");
    public async Task<DeviceModel> GetDeviceAsync()
    {
        var pending = _device ??= _api.EnsureDeviceAsync(Environment.MachineName);
        try { return await pending; }
        catch { if (ReferenceEquals(_device, pending)) _device = null; throw; }
    }
    public object Snapshot() => new {
        status = Status, today = DateOnly.FromDateTime(DateTime.Today), alert = new { visible = Alert.IsVisible, message = Alert.Message },
        morning = Settings.MorningNotificationAt.ToString("HH:mm"), evening = Settings.AfternoonNotificationAt.ToString("HH:mm"),
        adminDisableInAndOutNotifications = Settings.AdminDisableInAndOutNotifications,
        updateLabel = ParallelSystems.ProductSupport.ProductLifecycle.UpdateLabel("desktop-notifier", null),
        updateAction = ParallelSystems.ProductSupport.ProductLifecycle.UpdateActionLabel("desktop-notifier", null)
    };
    public void Start() => _poll = PollAsync();
    public void DismissAlert()
    {
        Alert.Dismiss(); _state.DismissedMissingTimesheetWorkDate = Alert.DismissedWorkDate; _store.Write(_state);
    }
    public void RecordSubmission(DateOnly date, bool hasRows)
    {
        if (hasRows) Alert.RecordSubmission(date);
        if (hasRows && date == DateOnly.FromDateTime(DateTime.Today))
            EntryPromptDismissRequested?.Invoke(date);
        NotificationDismissRequested?.Invoke(this, EventArgs.Empty);
    }
    public void DeferEntryPrompt(DateOnly date)
    {
        if (date != DateOnly.FromDateTime(DateTime.Today)) return;
        _entryPromptDeferredDate = date;
        EntryPromptDismissRequested?.Invoke(date);
    }
    public void ResumeEntryPrompt(DateOnly date)
    {
        if (_entryPromptDeferredDate != date) return;
        _entryPromptDeferredDate = null;
        if (_entryPromptRaisedDate == date) _entryPromptRaisedDate = null;
    }
    public async Task<ManualEntryPromptData> GetManualEntryPromptDataAsync(DateOnly workDate)
    {
        using var lease = EnterOperation();
        var device = await GetDeviceAsync();
        var projectsTask = _api.GetProjectsAsync();
        var clientsTask = _api.GetClientsAsync();
        var levelsTask = _api.GetLevelsAsync();
        var packageNamesTask = _api.GetPackageNamesAsync();
        var taskCategoriesTask = _api.GetTaskCategoriesAsync();
        var recentSessionDatesTask = _api.GetSessionDatesAsync(device.Id, workDate.AddDays(-366), workDate.AddDays(-1));
        await Task.WhenAll(projectsTask, clientsTask, levelsTask, packageNamesTask, taskCategoriesTask, recentSessionDatesTask);
        var previousSessions = new List<ExistingManualSessionModel>();
        foreach (var recentDate in (await recentSessionDatesTask).OrderByDescending(date => date))
        {
            previousSessions = await _api.GetManualSessionsAsync(device.Id, recentDate);
            if (previousSessions.Count > 0) break;
        }
        return new ManualEntryPromptData
        {
            Projects = await projectsTask,
            Clients = await clientsTask,
            Levels = await levelsTask,
            PackageNames = await packageNamesTask,
            TaskCategories = await taskCategoriesTask,
            PreviousSessions = previousSessions
        };
    }
    public async Task<SubmitResponse> SubmitPromptSessionsAsync(DateOnly workDate, IReadOnlyList<ManualEntryPromptSession> sessions)
    {
        using var lease = EnterOperation();
        if (workDate != DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException("The reminder can only submit today's work.");
        if (sessions.Count is 0 or > 200)
            throw new ArgumentException("Add between 1 and 200 work sessions.");

        var projects = await _api.GetProjectsAsync();
        var clients = await _api.GetClientsAsync();
        var device = await GetDeviceAsync();
        var status = await _api.GetStatusAsync(device.Id, workDate);
        if (status.AdditionalSeconds > 0)
        {
            EntryPromptDismissRequested?.Invoke(workDate);
            throw new InvalidOperationException("A timesheet entry has already been recorded for today. The reminder has closed.");
        }
        var start = DateTime.SpecifyKind(workDate.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Local);
        var request = new DailySubmitRequest { DeviceId = device.Id, WorkDate = workDate, AllowExistingDate = status.HasDailySession };
        foreach (var session in sessions)
        {
            var project = projects.FirstOrDefault(item => item.Id == session.ProjectId)
                ?? throw new ArgumentException("Select an existing project for every session.");
            var client = clients.FirstOrDefault(item => item.Id == session.ClientId)
                ?? throw new ArgumentException("Select an existing client for every session.");
            if (session.EngagedSeconds is <= 0 or > 86400)
                throw new ArgumentException("Hours must be greater than zero and no more than 24.");
            if (string.IsNullOrWhiteSpace(session.TaskCategory))
                throw new ArgumentException("Task category is required for every session.");
            if (session.PackageName?.Trim().Length > 300)
                throw new ArgumentException("Package name cannot exceed 300 characters.");

            request.ManualSessions.Add(new ManualSubmitModel
            {
                ProjectId = project.Id,
                ProjectName = project.Name,
                ClientId = client.Id,
                ClientName = client.Name,
                Level = session.Level?.Trim(),
                PackageName = session.PackageName?.Trim(),
                TaskCategory = session.TaskCategory.Trim(),
                Notes = session.Notes?.Trim(),
                StartedAtUtc = new DateTimeOffset(start.ToUniversalTime(), TimeSpan.Zero),
                EndedAtUtc = new DateTimeOffset(start.AddSeconds(session.EngagedSeconds).ToUniversalTime(), TimeSpan.Zero)
            });
        }
        var response = await _api.SubmitAsync(request);
        RecordSubmission(workDate, true);
        PromptSubmitted?.Invoke(workDate);
        return response;
    }
    public void SaveSchedule(TimeOnly morning, TimeOnly evening, bool? adminDisableInAndOutNotifications = null)
    {
        if (morning.Hour >= 12 || evening.Hour < 12) throw new ArgumentException("Morning must be before 12:00; evening must be 12:00 or later.");
        Settings.SaveNotificationSchedule(morning, evening, adminDisableInAndOutNotifications);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task CheckAsync(DateTime now)
    {
        using var lease = EnterOperation();
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var suppressDailyNotifications = await _api.ShouldSuppressDailyNotificationsAsync(Settings);
        // The evening reminder is local and must still run when the API is unavailable.
        if (!suppressDailyNotifications && _state.AfternoonProcessedDate != date && time >= Settings.AfternoonNotificationAt)
        {
            _state.AfternoonProcessedDate = date; _store.Write(_state);
            NotificationRequested?.Invoke(this, new() {
                WorkDate = date, Title = "Today's timesheet reminder",
                Message = "Don't forget to fill in your timesheet for today. Click here to open it."
            });
        }
        var device = await GetDeviceAsync();
        await Alert.RefreshAsync();
        if (!suppressDailyNotifications && _state.MorningProcessedDate != date && time >= Settings.MorningNotificationAt)
        {
            var status = await _api.GetStatusAsync(device.Id, date.AddDays(-1));
            _state.MorningProcessedDate = date; _store.Write(_state);
            if (status.AdditionalSeconds <= 0) NotificationRequested?.Invoke(this, new() {
                WorkDate = date.AddDays(-1), Title = "Morning timesheet reminder",
                Message = "No timesheet exists for yesterday. Click to add one."
            });
        }
        if (time >= new TimeOnly(1, 0))
        {
            var information = await _api.GetPersonalInformationAsync(Environment.MachineName);
            if (information.TimesheetNotificationsEnabled != true)
            {
                _entryPromptSuppressedDate = date;
                EntryPromptDismissRequested?.Invoke(date);
            }
            else if (_entryPromptSuppressedDate == date)
            {
                _entryPromptSuppressedDate = null;
            }
            if (information.TimesheetNotificationsEnabled == true)
            {
                var status = await _api.GetStatusAsync(device.Id, date);
                if (status.AdditionalSeconds > 0)
                    EntryPromptDismissRequested?.Invoke(date);
                else if (_entryPromptRaisedDate != date && _entryPromptDeferredDate != date)
                {
                    _entryPromptRaisedDate = date;
                    EntryPromptRequested?.Invoke(date);
                }
            }
        }
        Status = $"Monitoring {Environment.MachineName}. Morning: {Settings.MorningNotificationAt:HH:mm}; evening: {Settings.AfternoonNotificationAt:HH:mm}.";
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private async Task PollAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (!IsQuiescing)
            {
                try { await CheckAsync(DateTime.Now); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    Status = "Notification check failed. Retrying automatically.";
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            try { await Task.Delay(NextDelay(DateTime.Now), _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }
    private TimeSpan NextDelay(DateTime now)
    {
        var date = DateOnly.FromDateTime(now);
        var candidates = new[] { (Settings.MorningNotificationAt, _state.MorningProcessedDate), (Settings.AfternoonNotificationAt, _state.AfternoonProcessedDate) };
        var delay = TimeSpan.FromMinutes(Math.Clamp(Settings.CheckIntervalMinutes, 1, 1440));
        foreach (var (time, processed) in candidates)
        {
            var next = now.Date.Add(time.ToTimeSpan());
            var remaining = next > now ? next - now : processed == date ? next.AddDays(1) - now : TimeSpan.FromMinutes(1);
            if (remaining < delay) delay = remaining;
        }
        if (_entryPromptRaisedDate != date && _entryPromptDeferredDate != date && _entryPromptSuppressedDate != date)
        {
            var prompt = now.Date.Add(new TimeOnly(13, 0).ToTimeSpan());
            var remaining = prompt > now ? prompt - now : TimeSpan.FromMinutes(1);
            if (remaining < delay) delay = remaining;
        }
        return delay;
    }
    public string PrepareForUpdate() => _gate.Prepare(false, () => _store.WriteRequired(_state));
    public void AbortUpdate() => _gate.Abort();
    public async Task CompleteUpdateAsync() { _gate.Commit(); _stop.Cancel(); await _poll; }
    public void Dispose() { _stop.Cancel(); _api.Dispose(); }
}
