using System.Diagnostics;

namespace SFTracker.Services;

public sealed class GameWatcher : IDisposable
{
    private const string ProcessName = "FactoryGame-Win64-Shipping";
    private readonly System.Threading.Timer _timer;
    private bool _wasRunning;
    private bool _disposed;

    public event Action? GameClosed;
    public event Action? GameStarted;

    public bool IsGameRunning => _wasRunning;

    public GameWatcher()
    {
        _timer = new System.Threading.Timer(Check, null,
            TimeSpan.FromSeconds(2),   // initial delay
            TimeSpan.FromSeconds(5));  // interval
    }

    private void Check(object? _)
    {
        if (_disposed) return;
        var isRunning = Process.GetProcessesByName(ProcessName).Length > 0;
        if (_wasRunning && !isRunning) GameClosed?.Invoke();
        else if (!_wasRunning && isRunning) GameStarted?.Invoke();
        _wasRunning = isRunning;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
