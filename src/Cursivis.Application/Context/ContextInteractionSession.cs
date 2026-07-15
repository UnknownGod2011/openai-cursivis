using System.Collections.Concurrent;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;

namespace Cursivis.Application.Context;

public sealed class ContextInteractionSession
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private ContextSnapshot? _context;
    private SmartResult? _latestResult;

    public ContextInteractionSession(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ContextFingerprint? Fingerprint
    {
        get
        {
            lock (_gate)
            {
                return _context?.Fingerprint;
            }
        }
    }

    public void Begin(ContextSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsExpired(_timeProvider.GetUtcNow()))
        {
            throw new ArgumentException("An interaction cannot begin with expired context.", nameof(context));
        }

        lock (_gate)
        {
            _context = context;
            _latestResult = null;
        }
    }

    public void SetResult(ContextFingerprint expectedFingerprint, SmartResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            if (_context is null || _context.Fingerprint != expectedFingerprint)
            {
                throw new InvalidOperationException("The result does not belong to the active context session.");
            }

            _latestResult = result;
        }
    }

    public ContextSessionAccess GetCurrent()
    {
        lock (_gate)
        {
            if (_context is null)
            {
                return new ContextSessionAccess(ContextSessionAccessStatus.Missing, null, null);
            }

            if (_context.IsExpired(_timeProvider.GetUtcNow()))
            {
                _context = null;
                _latestResult = null;
                return new ContextSessionAccess(ContextSessionAccessStatus.Expired, null, null);
            }

            return new ContextSessionAccess(
                ContextSessionAccessStatus.Available,
                _context,
                _latestResult);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _context = null;
            _latestResult = null;
        }
    }
}

public sealed class ResultAutoCopyCoordinator
{
    private readonly IResultClipboardService _clipboard;
    private readonly ConcurrentDictionary<OperationId, Task<ResultClipboardWriteResult>> _writes = new();

    public ResultAutoCopyCoordinator(IResultClipboardService clipboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public Task<ResultClipboardWriteResult> CopyFinalOnceAsync(
        OperationId operationId,
        SmartResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return _writes.GetOrAdd(
            operationId,
            _ => _clipboard.CopyTextAsync(result.FinalContent, cancellationToken));
    }
}
