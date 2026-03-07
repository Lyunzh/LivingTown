namespace StardewModdingAPI;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error,
    Alert
}

public interface IMonitor
{
    void Log(string message, LogLevel level = LogLevel.Debug);
}

public sealed class NullMonitor : IMonitor
{
    public void Log(string message, LogLevel level = LogLevel.Debug)
    {
    }
}
