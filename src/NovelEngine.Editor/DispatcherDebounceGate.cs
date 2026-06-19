using System.Threading;

namespace NovelEngine.Editor;

internal sealed class DispatcherDebounceGate
{
    private int _pending;

    public bool IsPending => Volatile.Read(ref _pending) != 0;

    public bool TryRequest() =>
        Interlocked.Exchange(ref _pending, 1) == 0;

    public void Complete() =>
        Volatile.Write(ref _pending, 0);
}
