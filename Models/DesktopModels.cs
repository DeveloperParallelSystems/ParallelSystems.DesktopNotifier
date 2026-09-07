using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ParallelSystems.DesktopNotifier.Models;

public sealed class DeviceModel { public Guid Id { get; set; } public string MachineName { get; set; } = ""; }
public sealed class DailyStatusModel
{
    public Guid DeviceId { get; set; }
    public DateOnly WorkDate { get; set; }
    public bool HasDailySession { get; set; }
    public bool HasDraft { get; set; }
    public int DraftCount { get; set; }
    public int ExistingSessionCount { get; set; }
    public long RecordedSeconds { get; set; }
    public long AdditionalSeconds { get; set; }
    public long TotalSeconds { get; set; }
}
public sealed class DailyDraftModel : INotifyPropertyChanged
{
    public Guid Id { get; set; }
    public DateOnly WorkDate { get; set; }
    public Guid? ProjectId { get; set; }
    public string ProjectName { get; set; } = "Unassigned";
    private string? _detectedScope;
    public string? DetectedScope { get => _detectedScope; set { if (_detectedScope == value) return; _detectedScope = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetectedScope))); } }
    public int MeasuredActiveSeconds { get; set; }
    public int EngagedSeconds { get; set; }
    public int ForegroundSeconds { get; set; }
    public int InactiveSeconds { get; set; }
    public string Duration => TimeSpan.FromSeconds(EngagedSeconds).ToString(@"hh\:mm\:ss");
    private string? _taskCategory;
    private string? _notes;
    public string? TaskCategory { get => _taskCategory; set { if (_taskCategory == value) return; _taskCategory = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TaskCategory))); } }
    public string? Notes { get => _notes; set { if (_notes == value) return; _notes = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class ProjectModel { public Guid Id { get; set; } public string Name { get; set; } = ""; }

public sealed class ManualSessionModel : INotifyPropertyChanged
{
    public Guid? Id { get; set; }
    private string _projectName = "";
    private TimeSpan _engagedTime = TimeSpan.FromHours(1);
    private string _taskCategory = "";
    private string? _notes;
    public string ProjectName { get => _projectName; set => Set(ref _projectName, value); }
    public TimeSpan EngagedTime { get => _engagedTime; set => Set(ref _engagedTime, value); }
    public string TaskCategory { get => _taskCategory; set => Set(ref _taskCategory, value); }
    public string? Notes { get => _notes; set => Set(ref _notes, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
}

public sealed class DailySubmitRequest
{
    public Guid DeviceId { get; set; }
    public DateOnly WorkDate { get; set; }
    public bool AllowExistingDate { get; set; }
    public List<TrackedClassificationModel> TrackedSessions { get; set; } = [];
    public List<ManualSubmitModel> ManualSessions { get; set; } = [];
    public List<Guid> DeletedManualSessionIds { get; set; } = [];
}
public sealed class TrackedClassificationModel { public Guid DailyWorkSessionId { get; set; } public string? Scope { get; set; } public string? TaskCategory { get; set; } public string? Notes { get; set; } }
public sealed class ManualSubmitModel { public Guid? Id { get; set; } public Guid? ProjectId { get; set; } public string ProjectName { get; set; } = ""; public DateTimeOffset StartedAtUtc { get; set; } public DateTimeOffset EndedAtUtc { get; set; } public string TaskCategory { get; set; } = ""; public string? Notes { get; set; } }
public sealed class ExistingManualSessionModel { public Guid Id { get; set; } public Guid ProjectId { get; set; } public string ProjectName { get; set; } = ""; public int EngagedSeconds { get; set; } public string TaskCategory { get; set; } = ""; public string? Notes { get; set; } }
public sealed class SubmitResponse { public int SubmittedTrackedSessions { get; set; } public int CreatedManualSessions { get; set; } public int UpdatedManualSessions { get; set; } public int DeletedManualSessions { get; set; } }
