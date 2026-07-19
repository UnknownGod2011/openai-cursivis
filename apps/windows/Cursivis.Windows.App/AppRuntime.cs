using Cursivis.Application.Context;
using Cursivis.Application.Actions;
using Cursivis.Application.Dictation;
using Cursivis.Application.OpenAI;
using Cursivis.Application.Presentation;
using Cursivis.Application.QuickTasks;
using Cursivis.Application.Realtime;
using Cursivis.Domain.Actions;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;
using Cursivis.Domain.Models;
using Cursivis.Domain.QuickTasks;
using Cursivis.Contracts.Browser;
using Cursivis.Infrastructure.OpenAI;
using Cursivis.Infrastructure.BrowserBridge;
using Cursivis.Infrastructure.Storage.Persistence;
using Cursivis.Infrastructure.Storage.Settings;
using Cursivis.Windows.Platform.ClipboardServices;
using Cursivis.Windows.Platform.Audio;
using Cursivis.Windows.Platform.Capture;
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
    public const string QuickTaskCommand = "custom-quick-task";
    public const string DirectTakeActionCommand = "direct-take-action";
    public const string SmartDictationCommand = "smart-dictation";
    public const string RealtimeLiveModeCommand = "realtime-live-mode";
    public const string CancelCommand = "cancel-emergency-stop";
    public const string OpenSettingsCommand = "open-settings";

    private readonly Win32WindowMessageHook _messageHook;
    private readonly TransactionalHotkeyRegistrar _hotkeys;
    private readonly ScopedUnmodifiedHotkeyRegistration _escapeDismissHotkey;
    private readonly ContextOrbWindow _orbWindow;
    private readonly ContextResultWindow _resultWindow;
    private readonly BrowserBridgeService _browserBridge;
    private readonly TakeActionController _takeActionController;
    private readonly LiveModeOverlayWindow _liveModeWindow;
    private readonly IRealtimeLiveModeService _liveMode;
    private readonly ISmartDictationService _smartDictation;
    private readonly ILiveModeMemoryStore _liveModeMemory;
    private readonly NativePointerMonitor _pointerMonitor;
    private readonly VersionedJsonSettingsStore<QuickTaskDefinition> _quickTaskStore;
    private readonly IQuickTaskFinalizationService _quickTaskFinalizer;
    private readonly object _quickTaskGate = new();
    private CancellationTokenSource? _liveCompletionVisibilityCancellation;
    private CancellationTokenSource? _dictationCompletionVisibilityCancellation;
    private QuickTaskDefinition _currentQuickTask;
    private bool _disposed;

    private AppRuntime(
        WindowsOpenAiCredentialManager credentialManager,
        IResponsesGateway responsesGateway,
        Win32WindowMessageHook messageHook,
        TransactionalHotkeyRegistrar hotkeys,
        ScopedUnmodifiedHotkeyRegistration escapeDismissHotkey,
        ContextTriggerController contextTrigger,
        ContextOrbWindow orbWindow,
        ContextResultWindow resultWindow,
        BrowserBridgeService browserBridge,
        TakeActionController takeActionController,
        LiveModeOverlayWindow liveModeWindow,
        IRealtimeLiveModeService liveMode,
        ISmartDictationService smartDictation,
        ILiveModeMemoryStore liveModeMemory,
        NativePointerMonitor pointerMonitor,
        VersionedJsonSettingsStore<QuickTaskDefinition> quickTaskStore,
        IQuickTaskFinalizationService quickTaskFinalizer,
        QuickTaskDefinition currentQuickTask,
        HotkeyUpdateResult contextHotkeyStatus,
        HotkeyUpdateResult quickTaskHotkeyStatus,
        HotkeyUpdateResult directTakeActionHotkeyStatus,
        HotkeyUpdateResult smartDictationHotkeyStatus,
        HotkeyUpdateResult liveModeHotkeyStatus,
        HotkeyUpdateResult cancelHotkeyStatus,
        HotkeyUpdateResult settingsHotkeyStatus)
    {
        CredentialManager = credentialManager;
        ResponsesGateway = responsesGateway;
        _messageHook = messageHook;
        _hotkeys = hotkeys;
        _escapeDismissHotkey = escapeDismissHotkey;
        ContextTrigger = contextTrigger;
        _orbWindow = orbWindow;
        _resultWindow = resultWindow;
        _browserBridge = browserBridge;
        _takeActionController = takeActionController;
        _liveModeWindow = liveModeWindow;
        _liveMode = liveMode;
        _smartDictation = smartDictation;
        _liveModeMemory = liveModeMemory;
        _pointerMonitor = pointerMonitor;
        _quickTaskStore = quickTaskStore;
        _quickTaskFinalizer = quickTaskFinalizer;
        _currentQuickTask = currentQuickTask;
        ContextHotkeyStatus = contextHotkeyStatus;
        QuickTaskHotkeyStatus = quickTaskHotkeyStatus;
        DirectTakeActionHotkeyStatus = directTakeActionHotkeyStatus;
        SmartDictationHotkeyStatus = smartDictationHotkeyStatus;
        LiveModeHotkeyStatus = liveModeHotkeyStatus;
        CancelHotkeyStatus = cancelHotkeyStatus;
        SettingsHotkeyStatus = settingsHotkeyStatus;
        _messageHook.HotkeyPressed += OnHotkeyPressed;
        _resultWindow.SettingsRequested += OnSettingsRequested;
        _orbWindow.SettingsRequested += OnSettingsRequested;
        _orbWindow.MicrophoneRequested += OnLiveModeRequested;
        _orbWindow.CancelRequested += OnOrbCancelRequested;
        _liveModeWindow.StopRequested += OnLiveModeRequested;
        _liveMode.SnapshotChanged += OnLiveModeSnapshotChanged;
        _smartDictation.SnapshotChanged += OnSmartDictationSnapshotChanged;
        _resultWindow.ThemeRequested += OnThemeRequested;
        _resultWindow.OverlayVisibilityChanged += OnResultVisibilityChanged;
        _pointerMonitor.PointerPressed += OnPointerPressed;
    }

    public WindowsOpenAiCredentialManager CredentialManager { get; }

    public IResponsesGateway ResponsesGateway { get; }

    public ContextTriggerController ContextTrigger { get; }

    public HotkeyUpdateResult ContextHotkeyStatus { get; }

    public HotkeyUpdateResult QuickTaskHotkeyStatus { get; }

    public HotkeyUpdateResult DirectTakeActionHotkeyStatus { get; }

    public HotkeyUpdateResult SmartDictationHotkeyStatus { get; }

    public HotkeyUpdateResult LiveModeHotkeyStatus { get; }

    public HotkeyUpdateResult CancelHotkeyStatus { get; }

    public HotkeyUpdateResult SettingsHotkeyStatus { get; }

    public BrowserBridgeSnapshot BrowserBridgeStatus => _browserBridge.Snapshot;

    public ILiveModeMemoryStore LiveModeMemory => _liveModeMemory;

    public Task<BrowserFormSnapshot> TestBrowserIntegrationAsync(
        CancellationToken cancellationToken = default) =>
        _browserBridge.DiscoverFormAsync(cancellationToken);

    public QuickTaskDefinition CurrentQuickTask
    {
        get
        {
            lock (_quickTaskGate)
            {
                return _currentQuickTask;
            }
        }
    }

    public string QuickTaskActiveChord =>
        GetActiveHotkeyChord(QuickTaskCommand);

    public string GetActiveHotkeyChord(string commandName) =>
        _hotkeys.TryGetActive(commandName, out ActiveHotkeyRegistration? registration)
            ? registration!.Chord.ToString()
            : string.Empty;

    public async Task<HotkeyUpdateResult> UpdateHotkeyAsync(
        string commandName,
        string canonicalChord,
        CancellationToken cancellationToken = default)
    {
        if (commandName is not (
            ContextTriggerCommand or
            QuickTaskCommand or
            DirectTakeActionCommand or
            SmartDictationCommand or
            RealtimeLiveModeCommand or
            CancelCommand or
            OpenSettingsCommand))
        {
            throw new ArgumentOutOfRangeException(nameof(commandName));
        }

        if (!HotkeyChordParser.TryParse(canonicalChord, out Cursivis.Windows.Platform.Hotkeys.HotkeyChord chord))
        {
            throw new ArgumentException("The shortcut is invalid or reserved.", nameof(canonicalChord));
        }

        return await _hotkeys.UpdateAsync(commandName, chord, cancellationToken);
    }

    public async Task<HotkeyUpdateResult> UpdateQuickTaskHotkeyAsync(
        string canonicalChord,
        CancellationToken cancellationToken = default)
    {
        return await UpdateHotkeyAsync(QuickTaskCommand, canonicalChord, cancellationToken);
    }

    public Task<QuickTaskFinalizationResult> FinalizeQuickTaskAsync(
        string displayName,
        string roughDescription,
        CancellationToken cancellationToken = default) =>
        _quickTaskFinalizer.FinalizeAsync(
            displayName,
            roughDescription,
            ModelCatalog.Economy,
            cancellationToken);

    public async Task SaveQuickTaskAsync(
        QuickTaskDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsExplicitlyApproved)
        {
            throw new ArgumentException("The finalized Quick Task must be approved before saving.", nameof(definition));
        }

        if (QuickTaskSafetyValidator.Validate(definition.FinalizedInstruction).Count > 0)
        {
            throw new ArgumentException("The finalized Quick Task conflicts with centralized safety policy.", nameof(definition));
        }

        await _quickTaskStore.SaveAsync(definition, cancellationToken);
        lock (_quickTaskGate)
        {
            _currentQuickTask = definition;
        }
    }

    public Task<ContextTriggerExecutionResult> TestQuickTaskAsync(
        QuickTaskDefinition definition,
        string fixtureText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureText);
        ContextSnapshot context = ContextSnapshot.FromText(
            ContextKind.Text,
            ContextSource.DirectInput,
            new TargetIdentity("cursivis-settings", "quick-task-fixture"),
            fixtureText.Trim(),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5));
        return ContextTrigger.ExecuteQuickTaskFixtureAsync(
            ContextExecutionInput.FromText(context),
            definition,
            cancellationToken);
    }

    public static async Task<AppRuntime> CreateAsync(nint mainWindowHandle)
    {
        CursivisStoragePaths paths = CursivisStoragePaths.ForCurrentUser();
        var secretStore = new WindowsCurrentUserSecretStore(
            new WindowsCurrentUserSecretStoreOptions(
                paths.SecretsDirectory,
                "Cursivis.OpenAI.ApiKey.v1"));
        var credentialManager = new WindowsOpenAiCredentialManager(secretStore);
        var credentialSource = new WindowsOpenAiCredentialSource(secretStore);
        IResponsesGateway responsesGateway = new OpenAiResponsesGateway(credentialSource);
        ITranscriptionGateway transcriptionGateway = new OpenAiTranscriptionGateway(credentialSource);
        IRealtimeGateway realtimeGateway = new OpenAiRealtimeGateway(credentialSource);
        IContextTriggerService contextService = new ContextTriggerService(responsesGateway);
        IQuickTaskFinalizationService quickTaskFinalizer = new QuickTaskFinalizationService(responsesGateway);
        var quickTaskStore = new VersionedJsonSettingsStore<QuickTaskDefinition>(
            new VersionedJsonSettingsStoreOptions(
                paths.QuickTaskFile,
                QuickTaskDefinition.CurrentSchemaVersion,
                maximumFileBytes: 128 * 1024),
            new QuickTaskJsonSettingsCodec(
                static definition => definition is not null &&
                                     definition.IsExplicitlyApproved &&
                                     QuickTaskSafetyValidator.Validate(definition.FinalizedInstruction).Count == 0));
        SettingsLoadResult<QuickTaskDefinition> quickTaskLoad = await quickTaskStore.LoadAsync();
        if (quickTaskLoad.Status == SettingsLoadStatus.FirstRun)
        {
            await quickTaskStore.SaveAsync(quickTaskLoad.Value);
        }

        var foreground = new Win32ForegroundWindowIdentityProvider();
        ITextSelectionReader[] readers =
        [
            new UiAutomationTextSelectionReader(),
            new WindowsProtectedClipboardSelectionReader(),
        ];
        ISelectionCaptureService capture = new LayeredSelectionCaptureService(foreground, readers);
        IRegionContextCaptureService regionCapture = new RegionContextCaptureService(
            foreground,
            new WindowsScreenCaptureService());
        ISelectionReplacementService replacement = new WindowsSelectionReplacementService(foreground, readers);
        ITextInsertionService insertion = new WindowsTextInsertionService(foreground);
        ITextUndoService undo = new WindowsTextUndoService(foreground);
        IResultClipboardService clipboard = new WindowsResultClipboardService();
        var inputSettler = new WindowsHotkeyInputSettler();

        var orbWindow = new ContextOrbWindow();
        var resultWindow = new ContextResultWindow();
        var liveModeWindow = new LiveModeOverlayWindow();
        var pointerMonitor = new NativePointerMonitor();
        var foregroundActivator = new Win32ForegroundWindowActivator();
        var controller = new ContextTriggerController(
            capture,
            regionCapture,
            contextService,
            replacement,
            insertion,
            undo,
            clipboard,
            foregroundActivator,
            foreground,
            inputSettler,
            orbWindow,
            resultWindow,
            ModelCatalog.Balanced);
        var browserBridge = new BrowserBridgeService(
            new HashSet<string>(StringComparer.Ordinal)
            {
                BrowserExtensionIdentity.StableExtensionId,
            });
        await browserBridge.StartAsync();
        var takeActionService = new BrowserTakeActionService(
            responsesGateway,
            browserBridge);
        var takeActionController = new TakeActionController(
            browserBridge,
            takeActionService,
            capture,
            inputSettler,
            controller,
            orbWindow,
            resultWindow,
            ModelCatalog.Balanced);
        var liveModeMemory = new LiveModeMemoryStore(paths.MemoryFile);
        var liveModeCapabilities = new WindowsLiveModeCapabilityExecutor(
            clipboard,
            insertion,
            regionCapture,
            contextService,
            takeActionController,
            ModelCatalog.Balanced);
        var liveMode = new RealtimeLiveModeService(
            realtimeGateway,
            new WindowsRealtimeAudioSessionFactory(),
            new LiveModeContextProvider(inputSettler, capture, foreground),
            new LiveModeToolExecutor(liveModeMemory, liveModeCapabilities),
            ModelCatalog.Realtime.Value);
        var smartDictation = new SmartDictationService(
            new WindowsDictationAudioCaptureFactory(),
            new WindowsDictationTargetProvider(foreground),
            transcriptionGateway,
            responsesGateway,
            insertion,
            clipboard,
            ModelCatalog.EconomyTranscription,
            ModelCatalog.Balanced);

        var messageHook = new Win32WindowMessageHook(mainWindowHandle);
        var hotkeyPersister = new JsonHotkeyStatePersister(paths.HotkeysFile);
        var nativeHotkeys = new Win32NativeHotkeyApi();
        var hotkeys = new TransactionalHotkeyRegistrar(
            mainWindowHandle,
            nativeHotkeys,
            hotkeyPersister);
        var escapeDismissHotkey = new ScopedUnmodifiedHotkeyRegistration(
            mainWindowHandle,
            registrationId: 0x3FFE,
            virtualKey: 0x1B,
            nativeHotkeys);

        Cursivis.Windows.Platform.Hotkeys.HotkeyChord LoadHotkey(
            string command,
            Cursivis.Windows.Platform.Hotkeys.HotkeyChord fallback) =>
            hotkeyPersister.TryLoad(command, out Cursivis.Windows.Platform.Hotkeys.HotkeyChord persisted)
                ? persisted
                : fallback;

        HotkeyUpdateResult contextStatus = await hotkeys.RegisterInitialAsync(
            ContextTriggerCommand,
            LoadHotkey(
                ContextTriggerCommand,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4F)));
        Cursivis.Windows.Platform.Hotkeys.HotkeyChord quickTaskChord = LoadHotkey(
            QuickTaskCommand,
            new Cursivis.Windows.Platform.Hotkeys.HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                0x59));
        HotkeyUpdateResult quickTaskStatus = await hotkeys.RegisterInitialAsync(
            QuickTaskCommand,
            quickTaskChord);
        HotkeyUpdateResult directTakeActionStatus = await hotkeys.RegisterInitialAsync(
            DirectTakeActionCommand,
            LoadHotkey(
                DirectTakeActionCommand,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x49)));
        HotkeyUpdateResult smartDictationStatus = await hotkeys.RegisterInitialAsync(
            SmartDictationCommand,
            LoadHotkey(
                SmartDictationCommand,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x55)));
        HotkeyUpdateResult liveModeStatus = await hotkeys.RegisterInitialAsync(
            RealtimeLiveModeCommand,
            LoadHotkey(
                RealtimeLiveModeCommand,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x50)));
        HotkeyUpdateResult cancelStatus = await hotkeys.RegisterInitialAsync(
            CancelCommand,
            LoadHotkey(
                CancelCommand,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x1B)));
        HotkeyUpdateResult settingsStatus = await hotkeys.RegisterInitialAsync(
            OpenSettingsCommand,
            LoadHotkey(
                OpenSettingsCommand,
                new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x53)));

        return new AppRuntime(
            credentialManager,
            responsesGateway,
            messageHook,
            hotkeys,
            escapeDismissHotkey,
            controller,
            orbWindow,
            resultWindow,
            browserBridge,
            takeActionController,
            liveModeWindow,
            liveMode,
            smartDictation,
            liveModeMemory,
            pointerMonitor,
            quickTaskStore,
            quickTaskFinalizer,
            quickTaskLoad.Value,
            contextStatus,
            quickTaskStatus,
            directTakeActionStatus,
            smartDictationStatus,
            liveModeStatus,
            cancelStatus,
            settingsStatus);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _liveCompletionVisibilityCancellation?.Cancel();
        _liveCompletionVisibilityCancellation?.Dispose();
        _liveCompletionVisibilityCancellation = null;
        _dictationCompletionVisibilityCancellation?.Cancel();
        _dictationCompletionVisibilityCancellation?.Dispose();
        _dictationCompletionVisibilityCancellation = null;
        _messageHook.HotkeyPressed -= OnHotkeyPressed;
        _resultWindow.SettingsRequested -= OnSettingsRequested;
        _orbWindow.SettingsRequested -= OnSettingsRequested;
        _orbWindow.MicrophoneRequested -= OnLiveModeRequested;
        _orbWindow.CancelRequested -= OnOrbCancelRequested;
        _liveModeWindow.StopRequested -= OnLiveModeRequested;
        _liveMode.SnapshotChanged -= OnLiveModeSnapshotChanged;
        _smartDictation.SnapshotChanged -= OnSmartDictationSnapshotChanged;
        _resultWindow.ThemeRequested -= OnThemeRequested;
        _resultWindow.OverlayVisibilityChanged -= OnResultVisibilityChanged;
        _pointerMonitor.PointerPressed -= OnPointerPressed;
        _escapeDismissHotkey.Dispose();
        ContextTrigger.Dispose();
        _takeActionController.Dispose();
        await _smartDictation.DisposeAsync();
        await _liveMode.DisposeAsync();
        await _browserBridge.DisposeAsync();
        _messageHook.Dispose();
        _pointerMonitor.Dispose();
        await _hotkeys.DisposeAsync();
        _resultWindow.Close();
        _liveModeWindow.Close();
        _orbWindow.Close();
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs args)
    {
        if (_escapeDismissHotkey.Matches(args.RegistrationId))
        {
            ContextTrigger.DismissTransientResult();
            return;
        }

        if (_hotkeys.TryGetActive(ContextTriggerCommand, out ActiveHotkeyRegistration? context) &&
            context?.RegistrationId == args.RegistrationId)
        {
            ContextTrigger.Invoke();
            return;
        }

        if (_hotkeys.TryGetActive(QuickTaskCommand, out ActiveHotkeyRegistration? quickTask) &&
            quickTask?.RegistrationId == args.RegistrationId)
        {
            ContextTrigger.InvokeQuickTask(CurrentQuickTask);
            return;
        }

        if (_hotkeys.TryGetActive(DirectTakeActionCommand, out ActiveHotkeyRegistration? takeAction) &&
            takeAction?.RegistrationId == args.RegistrationId)
        {
            _takeActionController.InvokeDirect();
            return;
        }

        if (_hotkeys.TryGetActive(SmartDictationCommand, out ActiveHotkeyRegistration? smartDictation) &&
            smartDictation?.RegistrationId == args.RegistrationId)
        {
            _ = ToggleSmartDictationAsync();
            return;
        }

        if (_hotkeys.TryGetActive(RealtimeLiveModeCommand, out ActiveHotkeyRegistration? liveMode) &&
            liveMode?.RegistrationId == args.RegistrationId)
        {
            _ = ToggleLiveModeAsync();
            return;
        }

        if (_hotkeys.TryGetActive(OpenSettingsCommand, out ActiveHotkeyRegistration? settings) &&
            settings?.RegistrationId == args.RegistrationId)
        {
            OnSettingsRequested(this, EventArgs.Empty);
            return;
        }

        if (_hotkeys.TryGetActive(CancelCommand, out ActiveHotkeyRegistration? cancel) &&
            cancel?.RegistrationId == args.RegistrationId)
        {
            ContextTrigger.Cancel();
            _takeActionController.Cancel();
            _ = _smartDictation.CancelAsync();
            _ = _liveMode.StopAsync();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs args)
    {
        _ = _smartDictation.CancelAsync();
        _ = _liveMode.StopAsync();
        _resultWindow.Hide();
        _liveModeWindow.Hide();
        _orbWindow.Hide();
        App.ShowSettingsWindow();
    }

    private void OnThemeRequested(ElementTheme theme)
    {
        _resultWindow.SetRequestedTheme(theme);
        _orbWindow.SetRequestedTheme(theme);
        _liveModeWindow.SetRequestedTheme(theme);
    }

    private void OnLiveModeRequested(object? sender, EventArgs args) =>
        _ = ToggleLiveModeAsync();

    private void OnOrbCancelRequested(object? sender, EventArgs args)
    {
        if (_smartDictation.Snapshot.IsActive)
        {
            _ = _smartDictation.CancelAsync();
            return;
        }

        if (_liveMode.Snapshot.IsActive)
        {
            _ = _liveMode.StopAsync();
        }
    }

    private async Task ToggleLiveModeAsync()
    {
        try
        {
            if (!_liveMode.Snapshot.IsActive)
            {
                await _smartDictation.CancelAsync();
                ContextTrigger.DismissTransientResult();
            }

            await _liveMode.ToggleAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ToggleSmartDictationAsync()
    {
        try
        {
            if (!_smartDictation.Snapshot.IsActive)
            {
                await _liveMode.StopAsync();
                ContextTrigger.DismissTransientResult();
            }

            await _smartDictation.ToggleAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnSmartDictationSnapshotChanged(SmartDictationSnapshot snapshot)
    {
        _ = _orbWindow.DispatcherQueue.TryEnqueue(() => ApplySmartDictationSnapshot(snapshot));
    }

    private void ApplySmartDictationSnapshot(SmartDictationSnapshot snapshot)
    {
        _dictationCompletionVisibilityCancellation?.Cancel();
        _dictationCompletionVisibilityCancellation?.Dispose();
        _dictationCompletionVisibilityCancellation = null;

        OrbPresentationState orbState = snapshot.State switch
        {
            SmartDictationState.Listening => OrbPresentationState.Listening,
            SmartDictationState.Transcribing or SmartDictationState.Polishing => OrbPresentationState.Thinking,
            SmartDictationState.Inserting => OrbPresentationState.Executing,
            SmartDictationState.Done => OrbPresentationState.Done,
            SmartDictationState.Cancelled => OrbPresentationState.Cancelled,
            SmartDictationState.Error => OrbPresentationState.Error,
            _ => OrbPresentationState.Idle,
        };
        string detail = snapshot.State switch
        {
            SmartDictationState.Listening =>
                $"Speak naturally · press again to finish · level {Math.Round(snapshot.AudioLevel * 100):0}%",
            SmartDictationState.Transcribing => "Using OpenAI Audio Transcriptions",
            SmartDictationState.Polishing => "Removing false starts and restoring punctuation",
            SmartDictationState.Inserting => "Returning text to the original application",
            SmartDictationState.Done when snapshot.Inserted => "Text inserted · use the app's Undo command to revert",
            SmartDictationState.Done when snapshot.CopiedToClipboard => "The original target changed, so the text was copied",
            SmartDictationState.Cancelled => "No text was inserted or copied",
            SmartDictationState.Error => snapshot.SafeError ?? "Smart Dictation unavailable",
            _ => snapshot.Status,
        };
        _orbWindow.ShowDictationState(orbState, snapshot.Status, detail);

        if (snapshot.State is SmartDictationState.Done or SmartDictationState.Cancelled)
        {
            _dictationCompletionVisibilityCancellation = new CancellationTokenSource();
            _ = HideCompletedDictationOrbAsync(_dictationCompletionVisibilityCancellation.Token);
        }
    }

    private async Task HideCompletedDictationOrbAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
            _ = _orbWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (!_smartDictation.Snapshot.IsActive)
                {
                    _orbWindow.Hide();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnLiveModeSnapshotChanged(LiveModeSnapshot snapshot)
    {
        _ = _orbWindow.DispatcherQueue.TryEnqueue(() => ApplyLiveModeSnapshot(snapshot));
    }

    private void ApplyLiveModeSnapshot(LiveModeSnapshot snapshot)
    {
        _liveCompletionVisibilityCancellation?.Cancel();
        _liveCompletionVisibilityCancellation?.Dispose();
        _liveCompletionVisibilityCancellation = null;
        if (snapshot.State == LiveModeState.Ended)
        {
            _liveModeWindow.Hide();
            _orbWindow.ShowLiveState(
                OrbPresentationState.Done,
                "Live Mode ended",
                "Conversation closed",
                0);
            _liveCompletionVisibilityCancellation = new CancellationTokenSource();
            _ = HideCompletedLiveOrbAsync(_liveCompletionVisibilityCancellation.Token);
            return;
        }

        OrbPresentationState orbState = snapshot.State switch
        {
            LiveModeState.Connecting => OrbPresentationState.Thinking,
            LiveModeState.Listening or LiveModeState.UserSpeaking => OrbPresentationState.Listening,
            LiveModeState.Thinking => OrbPresentationState.Thinking,
            LiveModeState.Speaking => OrbPresentationState.Speaking,
            LiveModeState.ExecutingTool => OrbPresentationState.Executing,
            LiveModeState.Stopping => OrbPresentationState.Cancelled,
            LiveModeState.Error => OrbPresentationState.Error,
            _ => OrbPresentationState.Idle,
        };
        string detail = snapshot.State switch
        {
            LiveModeState.UserSpeaking when !string.IsNullOrWhiteSpace(snapshot.UserTranscript) =>
                snapshot.UserTranscript,
            LiveModeState.Speaking when !string.IsNullOrWhiteSpace(snapshot.AssistantTranscript) =>
                snapshot.AssistantTranscript,
            LiveModeState.Error => snapshot.SafeError ?? "Live Mode unavailable",
            LiveModeState.Listening => "Speak naturally · press again to end",
            LiveModeState.Connecting => "Opening a secure Realtime session",
            _ => snapshot.Status,
        };
        _orbWindow.ShowLiveState(orbState, snapshot.Status, detail, snapshot.AudioLevel);
        _liveModeWindow.ShowSnapshot(snapshot, _orbWindow.OverlayBounds);
    }

    private async Task HideCompletedLiveOrbAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
            _ = _orbWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_liveMode.Snapshot.State == LiveModeState.Ended)
                {
                    _orbWindow.Hide();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnResultVisibilityChanged(bool visible)
    {
        if (!visible)
        {
            _pointerMonitor.Stop();
            _escapeDismissHotkey.Disable();
            return;
        }

        _ = _escapeDismissHotkey.Enable();

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

}
