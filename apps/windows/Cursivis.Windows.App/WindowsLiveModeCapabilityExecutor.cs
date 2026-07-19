using Cursivis.Application.Context;
using Cursivis.Application.Realtime;
using Cursivis.Domain.Interaction;
using Cursivis.Domain.Models;

namespace Cursivis.Windows.App;

internal sealed class WindowsLiveModeCapabilityExecutor(
    IResultClipboardService clipboard,
    ITextInsertionService insertion,
    IRegionContextCaptureService regionCapture,
    IContextTriggerService contextService,
    TakeActionController takeAction,
    ModelIdentifier model) : ILiveModeCapabilityExecutor
{
    private readonly IResultClipboardService _clipboard = clipboard
        ?? throw new ArgumentNullException(nameof(clipboard));
    private readonly ITextInsertionService _insertion = insertion
        ?? throw new ArgumentNullException(nameof(insertion));
    private readonly IRegionContextCaptureService _regionCapture = regionCapture
        ?? throw new ArgumentNullException(nameof(regionCapture));
    private readonly IContextTriggerService _contextService = contextService
        ?? throw new ArgumentNullException(nameof(contextService));
    private readonly TakeActionController _takeAction = takeAction
        ?? throw new ArgumentNullException(nameof(takeAction));
    private readonly ModelIdentifier _model = model;

    public async Task<LiveModeCapabilityResult> CopyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ResultClipboardWriteResult result = await _clipboard.CopyTextAsync(text, cancellationToken);
        return result.Status == ResultClipboardWriteStatus.Copied
            ? new(true, "Copied to the clipboard.")
            : new(false, result.SafeDetail);
    }

    public async Task<LiveModeCapabilityResult> InsertAsync(
        LiveModeContext context,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (context.CapturedContext is null)
        {
            return new(false, "No original selected-text target is available for insertion.");
        }

        TextInsertionResult result = await _insertion.InsertAsync(
            context.CapturedContext,
            text,
            cancellationToken);
        return result.Status == TextInsertionStatus.Inserted
            ? new(true, "Inserted into the original application.")
            : new(false, result.SafeDetail);
    }

    public async Task<LiveModeCapabilityResult> AnalyzeScreenAsync(
        string instruction,
        CancellationToken cancellationToken = default)
    {
        RegionContextCaptureResult capture = await _regionCapture.CaptureAsync(
            TimeSpan.FromMinutes(5),
            cancellationToken);
        if (capture.Status == RegionContextCaptureStatus.Cancelled)
        {
            return new(false, "Screen-region selection was cancelled.");
        }

        if (capture.Status == RegionContextCaptureStatus.ColorDetected && capture.Color is not null)
        {
            string color = $"{capture.Color.Hex} — RGB({capture.Color.Red}, {capture.Color.Green}, {capture.Color.Blue}), {capture.Color.ApproximateName}";
            return new(true, "The selected color was detected.", color);
        }

        if (capture.Status != RegionContextCaptureStatus.ImageCaptured || capture.Input is null)
        {
            return new(false, capture.SafeDetail);
        }

        var option = new GuidedOption(
            "live_screen_analysis",
            "Analyze screen",
            $"Analyze only the user-selected screen region for this explicit request: {instruction.Trim()}");
        ContextTriggerExecutionResult result = await _contextService.ExecuteGuidedInstructionAsync(
            capture.Input,
            option,
            _model,
            cancellationToken);
        if (result.Status != ContextTriggerExecutionStatus.Completed || result.Result is null)
        {
            return new(
                false,
                result.Failure?.SafeMessage ?? "The selected screen region could not be analyzed.");
        }

        return new(true, "Screen-region analysis completed.", result.Result.FinalContent);
    }

    public async Task<LiveModeCapabilityResult> TakeBrowserActionAsync(
        string instruction,
        CancellationToken cancellationToken = default)
    {
        string output = await _takeAction.ExecuteFromLiveAsync(instruction, cancellationToken);
        return new(true, "The shared browser action workflow completed.", output);
    }
}
