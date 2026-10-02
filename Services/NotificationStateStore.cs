using System.IO;
using System.Text.Json;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class NotificationStateStore
{
    private readonly string _path;
    public NotificationStateStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Parallel Systems", "Timesheet Notifier", "notification-state.json");

    public NotificationProcessingState Read()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<NotificationProcessingState>(
                    File.ReadAllText(_path), AppSettings.JsonOptions) ?? new NotificationProcessingState()
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
