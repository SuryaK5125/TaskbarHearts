namespace SurvivalHud.Core;

/// <summary>
/// Turns successive GetSystemTimes readings into a total CPU utilisation percentage and
/// smooths it with a short moving average. The first reading and any invalid interval yield
/// no new sample rather than a made-up number.
/// </summary>
public sealed class CpuUsageCalculator
{
    private readonly int _window;
    private readonly Queue<double> _samples = new();
    private ulong _prevIdle, _prevTotal;
    private bool _hasPrev;

    public CpuUsageCalculator(int window = 4) => _window = Math.Max(1, window);

    /// <summary>Most recent unsmoothed sample, if any.</summary>
    public double? Raw { get; private set; }

    /// <summary>Moving average of the last few samples, if any.</summary>
    public double? Smoothed => _samples.Count == 0 ? null : _samples.Average();

    /// <param name="idle">Cumulative idle time.</param>
    /// <param name="kernel">Cumulative kernel time (includes idle time, as reported by Windows).</param>
    /// <param name="user">Cumulative user time.</param>
    public double? Next(ulong idle, ulong kernel, ulong user)
    {
        ulong total = kernel + user;
        if (_hasPrev && total > _prevTotal && idle >= _prevIdle)
        {
            ulong dTotal = total - _prevTotal, dIdle = idle - _prevIdle;
            if (dIdle <= dTotal)
            {
                Raw = (dTotal - dIdle) * 100.0 / dTotal;
                _samples.Enqueue(Raw.Value);
                while (_samples.Count > _window) _samples.Dequeue();
            }
        }
        _prevIdle = idle;
        _prevTotal = total;
        _hasPrev = true;
        return Smoothed;
    }
}
