namespace SmartGuard.Configuration;

/// <summary>
/// 崩溃环形缓冲：固定容量内存环形队列，仅接收高优先级行（Warn/Error/Critical）。
/// 进程崩溃时由 GlobalExceptionHandlers 触发 FlushToSync 同步落盘。
/// 代码极简：无异步、无 LINQ、无第三方依赖。
/// </summary>
public sealed class CrashRingBuffer
{
  private readonly string[] _slots;
  private readonly int _capacity;
  private int _head;   // 下一个写入位置
  private int _count;  // 当前有效条目数
  private readonly object _lock = new();

  public CrashRingBuffer(int capacity = 500)
  {
    _capacity = Math.Max(1, capacity);
    _slots = new string[_capacity];
  }

  public int Capacity => _capacity;

  public void Add(string line)
  {
    lock (_lock)
    {
      _slots[_head] = line;
      _head = (_head + 1) % _capacity;
      if (_count < _capacity) _count++;
    }
  }

  public IReadOnlyList<string> Drain()
  {
    lock (_lock)
    {
      if (_count == 0) return Array.Empty<string>();
      var result = new string[_count];
      var start = (_head - _count + _capacity) % _capacity;
      for (var i = 0; i < _count; i++)
        result[i] = _slots[(start + i) % _capacity];
      return result;
    }
  }

  public bool FlushToSync(EmergencySyncWriter writer, string path)
  {
    var snapshot = Drain();
    if (snapshot.Count == 0) return true;
    return EmergencySyncWriter.TryWrite(path, snapshot);
  }
}
