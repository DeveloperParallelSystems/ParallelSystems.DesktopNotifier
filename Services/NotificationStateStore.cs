using System.IO;
using System.Text.Json;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class NotificationStateStore
{
    private readonly string _path;
    private readonly string? _legacyPath;

    public NotificationStateStore(string? path = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _path = path ?? Path.Combine(localAppData,
            "Parallel Systems", "Desktop Notifier", "notification-state.json");
        _legacyPath = path is null
            ? Path.Combine(localAppData, "Parallel Systems", "Timesheet Notifier", "notification-state.json")
            : null;
    }

    public NotificationProcessingState Read()
    {
        try
        {
            var readPath = _path;
            if (!File.Exists(readPath) && _legacyPath is not null && File.Exists(_legacyPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.Copy(_legacyPath, _path, false);
                }
                catch (IOException) { readPath = _legacyPath; }
                catch (UnauthorizedAccessException) { readPath = _legacyPath; }
            }

            return File.Exists(readPath)
                ? JsonSerializer.Deserialize<NotificationProcessingState>(
                    File.ReadAllText(readPath), AppSettings.JsonOptions) ?? new NotificationProcessingState()
                : new NotificationProcessingState();
        }
        catch (IOException) { return new NotificationProcessingState(); }
        catch (UnauthorizedAccessException) { return new NotificationProcessingState(); }
        catch (JsonException) { return new NotificationProcessingState(); }
    }

    public void Write(NotificationProcessingState state)
    {
        try { WriteRequired(state); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void WriteRequired(NotificationProcessingState state)
    {
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, AppSettings.JsonOptions));
                file.Write(bytes); file.Flush(true);
            }
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed class NotificationProcessingState
{
    public DateOnly? DismissedMissingTimesheetWorkDate { get; set; }
    public DateOnly? MorningProcessedDate { get; set; }
    public DateOnly? AfternoonProcessedDate { get; set; }
}
