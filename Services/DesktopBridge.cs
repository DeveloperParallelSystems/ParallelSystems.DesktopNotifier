using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using ParallelSystems.DesktopNotifier.Models;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class DesktopPagePolicy
{
    public static bool Allows(Uri configured, string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == configured.Scheme && uri.Host == configured.Host && uri.Port == configured.Port &&
        uri.AbsolutePath.TrimEnd('/') == configured.AbsolutePath.TrimEnd('/') &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}

// All calls and events run on the application dispatcher. No generic URL/process/file operations.
public sealed class DesktopBridge : IDisposable
{
    public const int Version = 1;
    private static readonly HashSet<string> Catalogs = ["projects", "levels", "package-names", "tasks"];
    private readonly DesktopApiClient _api;
    private readonly NotifierService _service;
    private readonly DateOnly? _promptDate;
    public event Action<DateOnly, IReadOnlyList<ManualEntryPromptSession>>? ContinuePromptRequested;
    private TaskCompletionSource<bool>? _freeze;
    private string? _freezeId;
    private bool _preparing;
    private bool _hasPage;
    private bool _ready;
    private int _active;
    private DateOnly? _pendingDate;
    private IReadOnlyList<ManualEntryPromptSession>? _pendingSuggestions;
    private bool _pendingForceReload;
    public bool HasPendingEdits { get; private set; }
    public bool IsPreparing => _preparing || _service.IsQuiescing;
    public event Action<string, object?>? Event;
    public DesktopBridge(DesktopApiClient api, NotifierService service, DateOnly? promptDate = null)
    {
        _api = api; _service = service; _promptDate = promptDate;
        service.Changed += ServiceChanged;
        service.PromptSubmitted += OnPromptSubmitted;
    }
    private void ServiceChanged(object? sender, EventArgs args) => PublishStatus();
    private void OnPromptSubmitted(DateOnly workDate)
    {
        if (_promptDate is null) OpenDate(workDate, [], forceReload: true);
    }
    public void Dispose() { PageUnloaded(); _service.Changed -= ServiceChanged; _service.PromptSubmitted -= OnPromptSubmitted; }
    public void PageUnloaded()
    {
        _ready = false;
        _freeze?.TrySetResult(false);
    }
    public void PublishStatus() { if (_ready) Event?.Invoke("status", _service.Snapshot()); }
    public void OpenDate(DateOnly? date, IReadOnlyList<ManualEntryPromptSession>? suggestions = null, bool forceReload = false)
    {
        _pendingDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        _pendingSuggestions = suggestions;
        _pendingForceReload = forceReload;
        if (_ready) Event?.Invoke("navigate", new { workDate = _pendingDate, suggestions = _pendingSuggestions, forceReload = _pendingForceReload });
    }
    public async Task<string> PrepareForUpdateAsync(CancellationToken token)
    {
        if (IsPreparing || _active > 0 || HasPendingEdits || (_hasPage && !_ready)) return "busy";
        _preparing = true;
        try
        {
            if (_hasPage)
            {
                _freezeId = Guid.NewGuid().ToString("N");
                _freeze = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Event?.Invoke("prepareUpdate", new { token = _freezeId });
                if (!await _freeze.Task.WaitAsync(TimeSpan.FromSeconds(3), token)) return "busy";
            }
            token.ThrowIfCancellationRequested();
            if (HasPendingEdits || _active > 0) return "busy";
            return _service.PrepareForUpdate();
        }
        catch (TimeoutException) { return "busy"; }
        finally
        {
            _freeze = null; _freezeId = null;
            if (!_service.IsQuiescing) AbortUpdate();
        }
    }
    public void AbortUpdate()
    {
        _freeze?.TrySetResult(false);
        _preparing = false; _service.AbortUpdate();
        if (_ready) Event?.Invoke("abortUpdate", null);
    }
    public async Task<object?> InvokeAsync(string operation, JsonElement payload)
    {
        if (operation == "freezeAck")
        {
            if (_freezeId is not null && Text(payload, "token") == _freezeId)
                _freeze?.TrySetResult(!Flag(payload, "dirty") && !Flag(payload, "busy"));
            return null;
        }
        if (operation == "clientState")
        {
            HasPendingEdits = Flag(payload, "dirty");
            return null;
        }
        if (IsPreparing) throw new BridgeException("updating", "An update is being prepared. Try again shortly.");
        if (_active >= 16) throw new BridgeException("busy", "Too many requests. Wait for the current operation to finish.");
        using var lease = _service.EnterOperation();
        _active++;
        try
        {
            if (operation == "initialize")
            {
                if (payload.GetProperty("bridgeVersion").GetInt32() != Version)
                    throw new BridgeException("incompatible", "Update Desktop Notifier to use this website.");
                _hasPage = true;
                await _service.GetDeviceAsync();
                return new { bridgeVersion = Version, capabilities = new[] { "dailyWork", "profile", "catalogs", "reminders", "updates", "dashboard" },
                    applicationVersion = typeof(DesktopBridge).Assembly.GetName().Version!.ToString(3),
                    requiredHours = _service.Settings.RequiredHoursPerDay, today = DateOnly.FromDateTime(DateTime.Today),
                    mode = _promptDate.HasValue ? "manualEntryPrompt" : "timesheet",
                    workDate = _promptDate ?? _pendingDate ?? DateOnly.FromDateTime(DateTime.Today), state = _service.Snapshot() };
            }
            if (!_hasPage) throw new BridgeException("notInitialized", "Initialize the notifier first.");
            if (_promptDate is { } promptDate)
            {
                switch (operation)
                {
                    case "ready":
                        if (!payload.TryGetProperty("mode", out var mode) || mode.GetString() != "manualEntryPrompt")
                            throw new BridgeException("incompatible", "Deploy the website with manual entry prompt support, then retry.");
                        _ready = true;
                        return null;
                    case "promptData": return await _service.GetManualEntryPromptDataAsync(promptDate);
                    case "submitPrompt":
                        var sessions = payload.GetProperty("sessions").Deserialize<List<ManualEntryPromptSession>>(AppSettings.JsonOptions)
                            ?? throw new ArgumentException("Sessions are required.");
                        return await _service.SubmitPromptSessionsAsync(promptDate, sessions);
                    case "continuePrompt":
                        var suggestions = payload.GetProperty("sessions").Deserialize<List<ManualEntryPromptSession>>(AppSettings.JsonOptions)
                            ?? throw new ArgumentException("Suggested sessions are required.");
                        ContinuePromptRequested?.Invoke(promptDate, suggestions);
                        return null;
                    default: throw new BridgeException("unknownOperation", "This operation is not available in the reminder.");
                }
            }
            switch (operation)
            {
                case "dashboardRequest":
                    return await _api.DashboardRequestAsync(Text(payload, "method"), Text(payload, "path"),
                        payload.TryGetProperty("body", out var body) ? body : default);
                case "ready":
                    _ready = true;
                    if (_pendingDate is { } date) Event?.Invoke("navigate", new { workDate = date, suggestions = _pendingSuggestions, forceReload = _pendingForceReload });
                    Event?.Invoke("status", _service.Snapshot());
                    return null;
                case "navigationHandled":
                    if (payload.TryGetProperty("workDate", out var handled) && handled.GetString() == _pendingDate?.ToString("yyyy-MM-dd"))
                    {
                        _pendingDate = null;
                        _pendingSuggestions = null;
                        _pendingForceReload = false;
                    }
                    return null;
                case "options": return new { projects = await _api.GetProjectsAsync(), clients = await _api.GetClientsAsync(),
                    levels = await _api.GetLevelsAsync(), packageNames = await _api.GetPackageNamesAsync(), taskCategories = await _api.GetTaskCategoriesAsync() };
                case "daily":
                    var day = WorkDate(payload);
                    var device = await _service.GetDeviceAsync();
                    return new { status = await _api.GetStatusAsync(device.Id, day), sessions = await _api.GetManualSessionsAsync(device.Id, day) };
                case "sessionDates":
                    var start = DateOnly.ParseExact(Text(payload, "startDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var end = DateOnly.ParseExact(Text(payload, "endDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (end < start || end.DayNumber - start.DayNumber > 366) throw new ArgumentException("Choose a calendar range of up to one year.");
                    return await _api.GetSessionDatesAsync((await _service.GetDeviceAsync()).Id, start, end);
                case "submit": return await SubmitAsync(payload);
                case "profile": return await _api.GetPersonalInformationAsync(Environment.MachineName);
                case "saveProfile":
                    var profile = payload.Deserialize<EmployeePersonalInformation>(AppSettings.JsonOptions) ?? throw new ArgumentException("Personal information is required.");
                    if (string.IsNullOrWhiteSpace(profile.Email) || !new EmailAddressAttribute().IsValid(profile.Email)) throw new ArgumentException("Enter a valid email address.");
                    if (profile.BirthDate?.Date > DateTime.Today) throw new ArgumentException("Birth date cannot be in the future.");
                    // These are response-only permissions, never accepted as authority from the website.
                    profile.CanManageCatalogs = false; profile.IsAdministrator = false; profile.DisplayName = null;
                    return await _api.SavePersonalInformationAsync(Environment.MachineName, profile);
                case "catalog":
                    await CheckCatalogAccessAsync();
                    return await _api.GetCatalogAsync(Catalog(payload));
                case "saveCatalog":
                    await CheckCatalogAccessAsync();
                    var catalog = Catalog(payload);
                    var name = Text(payload, "name").Trim();
                    if (name.Length is 0 or > 500) throw new ArgumentException("Enter a name of up to 500 characters.");
                    var projectId = OptionalId(payload, "projectId"); var levelId = OptionalId(payload, "levelId");
                    if (catalog is "levels" or "package-names" && projectId is null) throw new ArgumentException("Select a project.");
                    if (catalog == "package-names" && levelId is null) throw new ArgumentException("Select a level.");
                    return await _api.SaveCatalogItemAsync(catalog, OptionalId(payload, "id"), name, projectId, levelId);
                case "saveSchedule":
                    _service.SaveSchedule(TimeOnly.ParseExact(Text(payload, "morning"), "HH:mm", CultureInfo.InvariantCulture),
                        TimeOnly.ParseExact(Text(payload, "evening"), "HH:mm", CultureInfo.InvariantCulture),
                        payload.TryGetProperty("adminDisableInAndOutNotifications", out var disableDaily)
                            ? disableDaily.GetBoolean() : null);
                    return _service.Snapshot();
                case "dismissAlert": _service.DismissAlert(); return _service.Snapshot();
                case "checkUpdates": ParallelSystems.ProductSupport.ProductLifecycle.CheckForUpdates(); return null;
                case "openUpdater": ParallelSystems.ProductSupport.ProductLifecycle.OpenUpdater(); return null;
                default: throw new BridgeException("unknownOperation", "This desktop operation is not supported.");
            }
        }
        finally { _active--; }
    }
    private async Task CheckCatalogAccessAsync()
    {
        if (!(await _api.GetPersonalInformationAsync(Environment.MachineName)).CanManageCatalogs)
            throw new BridgeException("accessDenied", "Your employee record no longer permits catalog management.");
    }
    private async Task<SubmitResponse> SubmitAsync(JsonElement payload)
    {
        var date = WorkDate(payload);
        var device = await _service.GetDeviceAsync();
        var rows = payload.GetProperty("sessions").Deserialize<List<WebSession>>(AppSettings.JsonOptions) ?? throw new ArgumentException("Sessions are required.");
        var deleted = payload.GetProperty("deletedIds").Deserialize<List<Guid>>(AppSettings.JsonOptions) ?? [];
        if (rows.Count > 200 || deleted.Count > 200 || rows.Count + deleted.Count == 0) throw new ArgumentException("Add a session or select sessions to remove (maximum 200).");
        var projects = await _api.GetProjectsAsync(); var clients = await _api.GetClientsAsync();
        var status = await _api.GetStatusAsync(device.Id, date);
        var request = new DailySubmitRequest { DeviceId = device.Id, WorkDate = date, AllowExistingDate = status.HasDailySession,
            IncludeTrackedSessions = false, DeletedManualSessionIds = deleted };
        foreach (var row in rows)
        {
            var project = projects.FirstOrDefault(p => p.Id == row.ProjectId) ?? throw new ArgumentException("Select an existing project for every session.");
            var client = clients.FirstOrDefault(c => c.Id == row.ClientId) ?? throw new ArgumentException("Select an existing client for every session.");
            if (row.EngagedSeconds <= 0 || row.EngagedSeconds > 86400) throw new ArgumentException("Hours must be greater than zero and no more than 24.");
            if (string.IsNullOrWhiteSpace(row.TaskCategory)) throw new ArgumentException("Task category is required for every session.");
            if (row.PackageName?.Trim().Length > 300) throw new ArgumentException("Package name cannot exceed 300 characters.");
            var start = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Local);
            request.ManualSessions.Add(new() { Id = row.Id, ProjectId = project.Id, ProjectName = project.Name,
                ClientId = client.Id, ClientName = client.Name, Level = row.Level?.Trim(), PackageName = row.PackageName?.Trim(),
                TaskCategory = row.TaskCategory.Trim(), Notes = row.Notes,
                StartedAtUtc = new DateTimeOffset(start.ToUniversalTime(), TimeSpan.Zero),
                EndedAtUtc = new DateTimeOffset(start.AddSeconds(row.EngagedSeconds).ToUniversalTime(), TimeSpan.Zero) });
        }
        var response = await _api.SubmitAsync(request);
        _service.RecordSubmission(date, rows.Count > 0);
        return response;
    }
    private static string Catalog(JsonElement payload)
    {
        var value = Text(payload, "catalog");
        return Catalogs.Contains(value) ? value : throw new ArgumentException("Unknown catalog.");
    }
    private static DateOnly WorkDate(JsonElement payload)
    {
        var date = DateOnly.ParseExact(Text(payload, "workDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return date <= DateOnly.FromDateTime(DateTime.Today) ? date : throw new ArgumentException("Choose today or an earlier date.");
    }
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw new ArgumentException($"{key} is required.");
    private static bool Flag(JsonElement value, string key) => value.GetProperty(key).GetBoolean();
    private static Guid? OptionalId(JsonElement value, string key) => value.TryGetProperty(key, out var id) && id.ValueKind != JsonValueKind.Null ? id.GetGuid() : null;
    private sealed class WebSession
    {
        public Guid? Id { get; set; }
        public Guid ProjectId { get; set; }
        public Guid ClientId { get; set; }
        public int EngagedSeconds { get; set; }
        public string TaskCategory { get; set; } = "";
        public string? Level { get; set; }
        public string? PackageName { get; set; }
        public string? Notes { get; set; }
    }
}
