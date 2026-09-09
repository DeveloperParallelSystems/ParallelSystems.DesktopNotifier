using System.IO;
using System.Text.Json;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class NotificationStateStore
{
    private readonly string _path = Path.Combine(
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
        var temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, AppSettings.JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
    public DateOnly? MorningProcessedDate { get; set; }
    public DateOnly? AfternoonProcessedDate { get; set; }
}
