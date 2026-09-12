namespace AppleMusicDesktopLyrics;

// A timed-out native read cannot be forcibly cancelled. Keep its slot occupied
// until it returns, and disable further reads for this reader's lifetime.
internal sealed class BoundedAsyncReader<T> where T : class
{
    private int _busy;
    private int _disabled;
    public bool Disabled => Volatile.Read(ref _disabled) != 0;

    public async Task<T?> ReadAsync(Func<T?> read, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Disabled || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return null;
        var task = Task.Run(() =>
        {
            try { return read(); }
            catch { return null; }
            finally { Volatile.Write(ref _busy, 0); }
        });
        try { return await task.WaitAsync(timeout, token); }
        catch (TimeoutException)
        {
            Volatile.Write(ref _disabled, 1);
            return null;
        }
    }
}
