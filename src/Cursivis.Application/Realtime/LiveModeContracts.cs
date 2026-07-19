using Cursivis.Application.OpenAI;
using Cursivis.Contracts.OpenAI;

namespace Cursivis.Application.Realtime;

public enum LiveModeState
{
    Idle,
    Connecting,
    Listening,
    UserSpeaking,
    Thinking,
    Speaking,
    ExecutingTool,
    Stopping,
    Ended,
    Error,
}

public sealed record LiveModeSnapshot(
    LiveModeState State,
    string UserTranscript,
    string AssistantTranscript,
    float AudioLevel,
    string Status,
    string? SafeError = null)
{
    public static LiveModeSnapshot Initial { get; } = new(
        LiveModeState.Idle,
        string.Empty,
        string.Empty,
        0,
        "Ready");

    public bool IsActive => State is not LiveModeState.Idle
        and not LiveModeState.Ended
        and not LiveModeState.Error;
}

public sealed record LiveModeContext(
    string? SelectedText,
    string? ContextFingerprint,
    string? ActiveApplication,
    string? ActiveWindowTitle)
{
    public static LiveModeContext Empty { get; } = new(null, null, null, null);
}

public interface ILiveModeContextProvider
{
    Task<LiveModeContext> CaptureAsync(CancellationToken cancellationToken = default);
}

public readonly record struct RealtimeAudioFrame(
    ReadOnlyMemory<byte> Pcm16Mono24Khz,
    float Level);

public interface IRealtimeAudioSession : IAsyncDisposable
{
    IAsyncEnumerable<RealtimeAudioFrame> ReadCapturedAudioAsync(
        CancellationToken cancellationToken = default);

    ValueTask QueuePlaybackAsync(
        ReadOnlyMemory<byte> pcm16Mono24Khz,
        CancellationToken cancellationToken = default);

    ValueTask ClearPlaybackAsync(CancellationToken cancellationToken = default);
}

public interface IRealtimeAudioSessionFactory
{
    Task<IRealtimeAudioSession> OpenAsync(CancellationToken cancellationToken = default);
}

public sealed record LiveModeToolExecutionResult(
    string Json,
    bool StopSession = false);

public interface ILiveModeToolExecutor
{
    IReadOnlyList<RealtimeToolDefinition> Definitions { get; }

    ValueTask<LiveModeToolExecutionResult> ExecuteAsync(
        LiveModeContext context,
        string toolName,
        string untrustedArgumentsJson,
        CancellationToken cancellationToken = default);
}

public interface IRealtimeLiveModeService : IAsyncDisposable
{
    LiveModeSnapshot Snapshot { get; }

    event Action<LiveModeSnapshot>? SnapshotChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task ToggleAsync(CancellationToken cancellationToken = default);
}
