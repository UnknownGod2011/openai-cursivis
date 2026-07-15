using Cursivis.Application.Context;
using Cursivis.Application.OpenAI;
using Cursivis.Application.Presentation;
using Cursivis.Domain.Actions;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;
using Cursivis.Domain.Models;
#if DEBUG
using Cursivis.Windows.App.Testing;
#endif
using Cursivis.Infrastructure.OpenAI;
using Cursivis.Infrastructure.Storage.Persistence;
using Cursivis.Windows.Platform.ClipboardServices;
using Cursivis.Windows.Platform.Foreground;
using Cursivis.Windows.Platform.Hotkeys;
using Cursivis.Windows.Platform.Overlays;
using Cursivis.Windows.Platform.Security;
using Cursivis.Windows.Platform.Selection;
using Microsoft.UI.Xaml;

namespace Cursivis.Windows.App;

public sealed class AppRuntime : IAsyncDisposable
{
    public const string ContextTriggerCommand = "context-trigger";
    public const string CancelCommand = "cancel-emergency-stop";

    private readonly Win32WindowMessageHook _messageHook;
    private readonly TransactionalHotkeyRegistrar _hotkeys;
    private readonly ContextOrbWindow _orbWindow;
    private readonly ContextResultWindow _resultWindow;
    private readonly NativePointerMonitor _pointerMonitor;
    private readonly IForegroundWindowActivator _foregroundActivator;
    private readonly nint _mainWindowHandle;
    private bool _disposed;

    private AppRuntime(
        WindowsOpenAiCredentialManager credentialManager,
        IResponsesGateway responsesGateway,
        Win32WindowMessageHook messageHook,
        TransactionalHotkeyRegistrar hotkeys,
        ContextTriggerController contextTrigger,
        ContextOrbWindow orbWindow,
        ContextResultWindow resultWindow,
        NativePointerMonitor pointerMonitor,
        IForegroundWindowActivator foregroundActivator,
        nint mainWindowHandle,
        HotkeyUpdateResult contextHotkeyStatus,
        HotkeyUpdateResult cancelHotkeyStatus)
    {
        CredentialManager = credentialManager;
        ResponsesGateway = responsesGateway;
        _messageHook = messageHook;
        _hotkeys = hotkeys;
        ContextTrigger = contextTrigger;
        _orbWindow = orbWindow;
        _resultWindow = resultWindow;
        _pointerMonitor = pointerMonitor;
        _foregroundActivator = foregroundActivator;
        _mainWindowHandle = mainWindowHandle;
        ContextHotkeyStatus = contextHotkeyStatus;
        CancelHotkeyStatus = cancelHotkeyStatus;
        _messageHook.HotkeyPressed += OnHotkeyPressed;
        _resultWindow.SettingsRequested += OnSettingsRequested;
        _orbWindow.SettingsRequested += OnSettingsRequested;
        _resultWindow.ThemeRequested += OnThemeRequested;
        _resultWindow.OverlayVisibilityChanged += OnResultVisibilityChanged;
        _pointerMonitor.PointerPressed += OnPointerPressed;
    }

    public WindowsOpenAiCredentialManager CredentialManager { get; }

    public IResponsesGateway ResponsesGateway { get; }

    public ContextTriggerController ContextTrigger { get; }

    public HotkeyUpdateResult ContextHotkeyStatus { get; }

    public HotkeyUpdateResult CancelHotkeyStatus { get; }

#if DEBUG
    internal void ShowVisualValidation(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string normalized = target.Trim().ToLowerInvariant();
        ElementTheme theme = normalized.EndsWith("-dark", StringComparison.Ordinal)
            ? ElementTheme.Dark
            : ElementTheme.Light;
        _resultWindow.SetRequestedTheme(theme);
        _orbWindow.SetRequestedTheme(theme);

        if (normalized.StartsWith("result", StringComparison.Ordinal))
        {
            if (normalized.StartsWith("result-error", StringComparison.Ordinal))
            {
                _resultWindow.ShowFailure(
                    "OpenAI request did not complete",
                    "Cursivis could not reach the configured model. No content was changed; copy and replacement remain unavailable.");
                return;
            }

            string content = normalized.StartsWith("result-long", StringComparison.Ordinal)
                ? string.Join(
                    Environment.NewLine + Environment.NewLine,
                    Enumerable.Repeat(
                        "Cursivis keeps long results readable inside a quiet scrolling surface. The command row remains compact, the output label stays visible, and the panel never grows into a heavy document window.",
                        10))
                : "The selected text has been rewritten with clearer structure, a more direct tone, and the original meaning preserved.";
            var result = new SmartResult(
                ContextKind.Text,
                SmartIntent.Rewrite,
                0.97,
                content,
                new SuggestedAction(
                    SuggestedActionType.ReplaceSelection,
                    "Replace the selected text"),
                RiskLevel.Low,
                ConfirmationHint.ContextDependent);
            _resultWindow.ShowResult(result, guidedMode: false);
            return;
        }

        if (normalized.StartsWith("orb-guided-panel", StringComparison.Ordinal))
        {
            _orbWindow.ShowGuidedOptions(ContextKind.Text);
            return;
        }

        string stateName = normalized
            .Replace("orb-", string.Empty, StringComparison.Ordinal)
            .Replace("-dark", string.Empty, StringComparison.Ordinal)
            .Replace("-light", string.Empty, StringComparison.Ordinal);
        if (!Enum.TryParse(stateName, ignoreCase: true, out OrbPresentationState state))
        {
            throw new ArgumentException("Unknown visual-validation target.", nameof(target));
        }

        (string status, string detail) = state switch
        {
            OrbPresentationState.Idle => ("Ready", "Drag to place · microphone available"),
            OrbPresentationState.Listening => ("Listening", "Speak naturally · cancel anytime"),
            OrbPresentationState.Thinking => ("Reading context", "Keeping focus in the source app"),
            OrbPresentationState.Generating => ("Generating", "Streaming a grounded response"),
            OrbPresentationState.Speaking => ("Speaking", "Playback active · cancel anytime"),
            OrbPresentationState.Guiding => ("Guiding", "Waiting for the next visible change"),
            OrbPresentationState.Executing => ("Taking action", "Verifying the approved target"),
            OrbPresentationState.Done => ("Done", "The result is ready"),
            OrbPresentationState.Cancelled => ("Cancelled", "No changes were made"),
            OrbPresentationState.Error => ("Needs attention", "The operation stopped safely"),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        _orbWindow.ShowState(state, status, detail);
    }
#endif

    public static async Task<AppRuntime> CreateAsync(nint mainWindowHandle)
    {
        CursivisStoragePaths paths = CursivisStoragePaths.ForCurrentUser();
        var secretStore = new WindowsCurrentUserSecretStore(
            new WindowsCurrentUserSecretStoreOptions(
                paths.SecretsDirectory,
                "Cursivis.OpenAI.ApiKey.v1"));
        var credentialManager = new WindowsOpenAiCredentialManager(secretStore);
        var credentialSource = new WindowsOpenAiCredentialSource(secretStore);
        IResponsesGateway responsesGateway;
#if DEBUG
        responsesGateway = string.Equals(
            Environment.GetEnvironmentVariable("CURSIVIS_MOCK_OPENAI"),
            "1",
            StringComparison.Ordinal)
                ? new DeterministicResponsesGateway()
                : new OpenAiResponsesGateway(credentialSource);
#else
        responsesGateway = new OpenAiResponsesGateway(credentialSource);
#endif
        IContextTriggerService contextService = new ContextTriggerService(responsesGateway);

        var foreground = new Win32ForegroundWindowIdentityProvider();
        ITextSelectionReader[] readers =
        [
            new UiAutomationTextSelectionReader(),
            new WindowsProtectedClipboardSelectionReader(),
        ];
        ISelectionCaptureService capture = new LayeredSelectionCaptureService(foreground, readers);
        ISelectionReplacementService replacement = new WindowsSelectionReplacementService(foreground, readers);
        ITextInsertionService insertion = new WindowsTextInsertionService(foreground);
        IResultClipboardService clipboard = new WindowsResultClipboardService();

        var orbWindow = new ContextOrbWindow();
        var resultWindow = new ContextResultWindow();
        var pointerMonitor = new NativePointerMonitor();
        var foregroundActivator = new Win32ForegroundWindowActivator();
        var controller = new ContextTriggerController(
            capture,
            contextService,
            replacement,
            insertion,
            clipboard,
            foregroundActivator,
            orbWindow,
            resultWindow,
            ModelCatalog.Balanced);

        var messageHook = new Win32WindowMessageHook(mainWindowHandle);
        var hotkeys = new TransactionalHotkeyRegistrar(
            mainWindowHandle,
            new Win32NativeHotkeyApi(),
            new StartupOnlyHotkeyPersister());

        HotkeyUpdateResult contextStatus = await hotkeys.RegisterInitialAsync(
            ContextTriggerCommand,
            new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4F));
        HotkeyUpdateResult cancelStatus = await hotkeys.RegisterInitialAsync(
            CancelCommand,
            new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x1B));

        return new AppRuntime(
            credentialManager,
            responsesGateway,
            messageHook,
            hotkeys,
            controller,
            orbWindow,
            resultWindow,
            pointerMonitor,
            foregroundActivator,
            mainWindowHandle,
            contextStatus,
            cancelStatus);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messageHook.HotkeyPressed -= OnHotkeyPressed;
        _resultWindow.SettingsRequested -= OnSettingsRequested;
        _orbWindow.SettingsRequested -= OnSettingsRequested;
        _resultWindow.ThemeRequested -= OnThemeRequested;
        _resultWindow.OverlayVisibilityChanged -= OnResultVisibilityChanged;
        _pointerMonitor.PointerPressed -= OnPointerPressed;
        ContextTrigger.Dispose();
        _messageHook.Dispose();
        _pointerMonitor.Dispose();
        await _hotkeys.DisposeAsync();
        _resultWindow.Close();
        _orbWindow.Close();
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs args)
    {
        if (_hotkeys.TryGetActive(ContextTriggerCommand, out ActiveHotkeyRegistration? context) &&
            context?.RegistrationId == args.RegistrationId)
        {
            ContextTrigger.Invoke();
            return;
        }

        if (_hotkeys.TryGetActive(CancelCommand, out ActiveHotkeyRegistration? cancel) &&
            cancel?.RegistrationId == args.RegistrationId)
        {
            ContextTrigger.Cancel();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs args)
    {
        _resultWindow.Hide();
        _orbWindow.Hide();
        _ = _foregroundActivator.TryActivate(_mainWindowHandle);
    }

    private void OnThemeRequested(ElementTheme theme)
    {
        _resultWindow.SetRequestedTheme(theme);
        _orbWindow.SetRequestedTheme(theme);
    }

    private void OnResultVisibilityChanged(bool visible)
    {
#if DEBUG
        string? visualTarget = Environment.GetEnvironmentVariable("CURSIVIS_VISUAL_VALIDATION");
        if (!string.IsNullOrWhiteSpace(visualTarget) &&
            !visualTarget.Trim().StartsWith("result-outside", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
#endif
        if (!visible)
        {
            _pointerMonitor.Stop();
            return;
        }

        try
        {
            _pointerMonitor.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _pointerMonitor.Stop();
        }
    }

    private void OnPointerPressed(object? sender, NativePointerPressedEventArgs args)
    {
        if (!_resultWindow.IsVisible)
        {
            return;
        }

        OverlayRectangle[] children = _orbWindow.IsVisible
            ? [_orbWindow.OverlayBounds]
            : [];
        var context = new OverlayDismissalContext(
            IsResultPanelVisible: true,
            _resultWindow.OverlayBounds,
            children,
            _resultWindow.IsInteracting);
        if (OverlayDismissalPolicy.ShouldDismiss(args.Position, context))
        {
            ContextTrigger.DismissTransientResult();
        }
    }

    private sealed class StartupOnlyHotkeyPersister : IHotkeyStatePersister
    {
        public Task PersistAsync(
            string commandName,
            HotkeyChord chord,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Runtime hotkey mutation is unavailable until the Settings page supplies durable persistence.");
    }
}
