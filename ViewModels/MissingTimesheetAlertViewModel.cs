using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ParallelSystems.DesktopNotifier.ViewModels;

public sealed class MissingTimesheetAlertViewModel : INotifyPropertyChanged
{
    private readonly Func<DateOnly, Task<bool>> _hasSubmission;
    private readonly TimeProvider _clock;
    private int _request;
    private bool _isVisible;
    private DateOnly? _workDate;

    public MissingTimesheetAlertViewModel(Func<DateOnly, Task<bool>> hasSubmission,
        DateOnly? dismissedWorkDate = null, TimeProvider? clock = null)
    {
        _hasSubmission = hasSubmission;
        DismissedWorkDate = dismissedWorkDate;
        _clock = clock ?? TimeProvider.System;
    }

    public DateOnly? DismissedWorkDate { get; private set; }
    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }
    public string Message => _workDate is { } date
        ? $"You haven't submitted a timesheet for yesterday ({date:ddd, MMM d})."
        : string.Empty;

    private DateOnly Yesterday => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime).AddDays(-1);

    public async Task RefreshAsync()
    {
        var request = ++_request;
        var workDate = Yesterday;
        if (_workDate != workDate)
        {
            _workDate = workDate;
            IsVisible = false;
            OnPropertyChanged(nameof(Message));
        }

        if (workDate.DayOfWeek == DayOfWeek.Sunday || DismissedWorkDate == workDate)
        {
            IsVisible = false;
            return;
        }

        var hasSubmission = await _hasSubmission(workDate);
        // A dismissal, submission, newer check, or midnight can invalidate this response.
        if (request != _request || workDate != Yesterday) return;
        IsVisible = !hasSubmission;
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        DismissedWorkDate = _workDate;
        ++_request;
        IsVisible = false;
    }

    public void RecordSubmission(DateOnly workDate)
    {
        if (workDate != Yesterday) return;
        ++_request;
        IsVisible = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
