using System.Threading;
using System.Runtime.Versioning;

namespace AppleMusicDesktopLyrics;

[SupportedOSPlatform("windows")]
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly RegisteredWaitHandle? _activationRegistration;

    public SingleInstanceCoordinator(string applicationId, Action activateExistingInstance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentNullException.ThrowIfNull(activateExistingInstance);

        var objectPrefix = $@"Local\{applicationId}";
        _mutex = new Mutex(initiallyOwned: true, objectPrefix + ".Mutex", out var createdNew);
        IsPrimary = createdNew;
        if (!createdNew)
        {
            SignalPrimaryInstance(objectPrefix + ".Activate");
            return;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            objectPrefix + ".Activate");
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut) activateExistingInstance();
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public bool IsPrimary { get; }

    private static void SignalPrimaryInstance(string eventName)
    {
        // The first process may still be creating the activation event. A very short
        // retry window makes a rapid double-click reliably bring up its manager.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(eventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) when (attempt < 4)
            {
                Thread.Sleep(40);
            }
        }
    }

    public void Dispose()
    {
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
