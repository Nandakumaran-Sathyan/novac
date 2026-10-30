using System.Collections.Concurrent;

namespace SmartPOS.Application.AI;

/// <summary>
/// One AI question at a time per user. A local model answers one request at a time anyway, and a second click
/// while waiting would only queue duplicate work. Singleton; the key is the user id from the token.
/// </summary>
public sealed class AIConcurrencyGate
{
    private readonly ConcurrentDictionary<int, byte> _busy = new();

    /// <summary>Returns a lease to dispose when the answer is done, or null if this user already has one in progress.</summary>
    public IDisposable? TryEnter(int userId) => _busy.TryAdd(userId, 0) ? new Lease(this, userId) : null;

    private sealed class Lease(AIConcurrencyGate gate, int userId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate._busy.TryRemove(userId, out _);
        }
    }
}
