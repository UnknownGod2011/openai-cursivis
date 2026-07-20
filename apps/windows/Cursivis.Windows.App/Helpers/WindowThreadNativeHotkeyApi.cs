using Cursivis.Windows.Platform.Hotkeys;
using Microsoft.UI.Dispatching;

namespace Cursivis.Windows.App.Helpers;

/// <summary>
/// Adapts the WinUI dispatcher to the platform-level native hotkey thread
/// adapter without leaking WinUI types into the platform project.
/// </summary>
internal sealed class DispatcherQueueWindowThreadDispatcher : IWindowThreadDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    public DispatcherQueueWindowThreadDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    public bool HasThreadAccess => _dispatcherQueue.HasThreadAccess;

    public bool TryEnqueue(Action action) => _dispatcherQueue.TryEnqueue(() => action());
}
