namespace ImpiousBonum.Core.Metrics;

/// <summary>Fixed-size buffer of doubles that overwrites the oldest value. Not thread-safe.</summary>
internal sealed class RingBuffer(int capacity)
{
    private readonly double[] _items = new double[capacity];
    private int _next;
    private int _count;

    public int Count => _count;

    public void Add(double value)
    {
        _items[_next] = value;
        _next = (_next + 1) % _items.Length;
        _count = Math.Min(_count + 1, _items.Length);
    }

    /// <summary>The most recent <paramref name="count"/> values, oldest first.</summary>
    public double[] Latest(int count)
    {
        count = Math.Clamp(count, 0, _count);
        var result = new double[count];
        var start = (_next - count + _items.Length) % _items.Length;
        for (var i = 0; i < count; i++)
            result[i] = _items[(start + i) % _items.Length];
        return result;
    }
}
