using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ParallelSystems.DesktopNotifier.Models;

public sealed class DeviceModel { public Guid Id { get; set; } public string MachineName { get; set; } = ""; }
public sealed class DailyStatusModel
{
    public bool HasDailySession { get; set; }
    public long AdditionalSeconds { get; set; }
}
public sealed class ProjectModel { public Guid Id { get; set; } public string Name { get; set; } = ""; }
public sealed class ClientModel { public Guid Id { get; set; } public string Name { get; set; } = ""; }

public sealed class ManualSessionModel : INotifyPropertyChanged
{
    public Guid? Id { get; set; }
    private string _projectName = "";
    private TimeSpan _engagedTime = TimeSpan.FromHours(1);
    private string _taskCategory = "";
    private string _clientName = "";
    private string? _level;
    private string? _notes;
    public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }
    public TimeSpan EngagedTime { get => _engagedTime; set => Set(ref _engagedTime, value); }
    public string TaskCategory { get => _taskCategory; set => Set(ref _taskCategory, value); }
    public string ClientName { get => _clientName; set => Set(ref _clientName, value); }
    public string? Level { get => _level; set => Set(ref _level, value); }
    public string? Notes { get => _notes; set => Set(ref _notes, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
}

public sealed class DailySubmitRequest
{
    public Guid DeviceId { get; set; }
    public DateOnly WorkDate { get; set; }
    public bool AllowExistingDate { get; set; }
    public bool IncludeTrackedSessions { get; set; }
    public List<ManualSubmitModel> ManualSessions { get; set; } = [];
    public List<Guid> DeletedManualSessionIds { get; set; } = [];
}
public sealed class ManualSubmitModel { public Guid? Id { get; set; } public Guid? ProjectId { get; set; } public string ProjectName { get; set; } = ""; public Guid? ClientId { get; set; } public string ClientName { get; set; } = ""; public string? Level { get; set; } public DateTimeOffset StartedAtUtc { get; set; } public DateTimeOffset EndedAtUtc { get; set; } public string TaskCategory { get; set; } = ""; public string? Notes { get; set; } }
public sealed class ExistingManualSessionModel { public Guid Id { get; set; } public Guid ProjectId { get; set; } public string ProjectName { get; set; } = ""; public Guid? ClientId { get; set; } public string ClientName { get; set; } = ""; public string? Level { get; set; } public int EngagedSeconds { get; set; } public string TaskCategory { get; set; } = ""; public string? Notes { get; set; } }
public sealed class SubmitResponse { public int CreatedManualSessions { get; set; } public int UpdatedManualSessions { get; set; } public int DeletedManualSessions { get; set; } }
