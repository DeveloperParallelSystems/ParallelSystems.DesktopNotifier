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
    public AppSettings Settings { get; }
    public MissingTimesheetAlertViewModel Alert { get; }
    public string Status { get; private set; } = "Starting…";
    public bool IsQuiescing => _gate.IsQuiescing;
    public event EventHandler<TimesheetNotificationEventArgs>? NotificationRequested;
    public event EventHandler? NotificationDismissRequested;
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
        NotificationDismissRequested?.Invoke(this, EventArgs.Empty);
    }
    public void SaveSchedule(TimeOnly morning, TimeOnly evening)
    {
        if (morning.Hour >= 12 || evening.Hour < 12) throw new ArgumentException("Morning must be before 12:00; evening must be 12:00 or later.");
        Settings.SaveNotificationSchedule(morning, evening);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task CheckAsync(DateTime now)
    {
        using var lease = EnterOperation();
        var date = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        // The evening reminder is local and must still run when the API is unavailable.
        if (_state.AfternoonProcessedDate != date && time >= Settings.AfternoonNotificationAt)
        {
            _state.AfternoonProcessedDate = date; _store.Write(_state);
            NotificationRequested?.Invoke(this, new() {
                WorkDate = date, Title = "Today's timesheet reminder",
                Message = "Don't forget to fill in your timesheet for today. Click here to open it."
            });
        }
        var device = await GetDeviceAsync();
        await Alert.RefreshAsync();
        if (_state.MorningProcessedDate != date && time >= Settings.MorningNotificationAt)
        {
            var status = await _api.GetStatusAsync(device.Id, date.AddDays(-1));
            _state.MorningProcessedDate = date; _store.Write(_state);
            if (status.AdditionalSeconds <= 0) NotificationRequested?.Invoke(this, new() {
                WorkDate = date.AddDays(-1), Title = "Morning timesheet reminder",
                Message = "No timesheet exists for yesterday. Click to add one."
            });
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
        return delay;
    }
    public string PrepareForUpdate() => _gate.Prepare(false, () => _store.WriteRequired(_state));
    public void AbortUpdate() => _gate.Abort();
    public async Task CompleteUpdateAsync() { _gate.Commit(); _stop.Cancel(); await _poll; }
    public void Dispose() { _stop.Cancel(); _api.Dispose(); }
}
