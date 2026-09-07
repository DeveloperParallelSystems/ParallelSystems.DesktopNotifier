using System.IO;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class ReminderDismissalStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Parallel Systems", "Timesheet Notifier", "dismissed-reminder.txt");

    public DateOnly? Read()
    {
        try
        {
            return File.Exists(_path) && DateOnly.TryParseExact(
                File.ReadAllText(_path).Trim(), "yyyy-MM-dd", out var date)
                ? date
                : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Write(DateOnly date)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, date.ToString("yyyy-MM-dd"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
