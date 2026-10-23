using System;

namespace BEN_TOOLBOX_2_0.Services;

/// <summary>Fixed-size usage history; null denotes a gap in sensor readings.</summary>
public sealed class TelemetryHistory
{
    private readonly double?[] _values;
    private int _next;

    public TelemetryHistory(int capacity = 60)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _values = new double?[capacity];
    }

    public int Capacity => _values.Length;
    public int Count { get; private set; }
    public int ValidCount { get; private set; }

    public void Add(double? sample)
    {
        double? value = sample.HasValue && double.IsFinite(sample.Value)
            ? Math.Clamp(sample.Value, 0d, 100d) : null;
        if (Count == Capacity)
        {
            if (_values[_next].HasValue) ValidCount--;
        }
        else Count++;

        _values[_next] = value;
        if (value.HasValue) ValidCount++;
        _next = (_next + 1) % Capacity;
    }

    public double? GetOldestFirst(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        int oldest = Count == Capacity ? _next : 0;
        return _values[(oldest + index) % Capacity];
    }

    public void Clear()
    {
        Array.Clear(_values);
        _next = Count = ValidCount = 0;
    }
}
