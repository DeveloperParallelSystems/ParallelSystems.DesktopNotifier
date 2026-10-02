namespace ParallelSystems.DesktopNotifier.Services;

/// <summary>Used only on the UI thread; leases cover complete asynchronous operations and modal dialogs.</summary>
public sealed class UpdateShutdownGate
{
    private int _active;
    public bool IsQuiescing { get; private set; }
    public bool IsCommitted { get; private set; }
    public IDisposable? TryEnter()
    {
        if (IsQuiescing) return null;
        _active++;
        return new Lease(this);
    }
    public string Prepare(bool unsavedChanges, Action persist)
    {
        if (IsQuiescing || _active != 0 || unsavedChanges) return "busy";
        IsQuiescing = true;
        try { persist(); return "ready"; }
        catch { IsQuiescing = false; return "unable"; }
    }
    public void Abort() { if (!IsCommitted) IsQuiescing = false; }
    public void Commit()
    {
        if (!IsQuiescing) throw new InvalidOperationException("Shutdown was not prepared.");
        IsCommitted = true;
    }
    private sealed class Lease(UpdateShutdownGate owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (!_disposed) { _disposed = true; owner._active--; } }
    }
}
