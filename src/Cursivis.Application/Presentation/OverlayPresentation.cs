using Cursivis.Domain.Interaction;
using Cursivis.Domain.Context;
using Cursivis.Domain.Settings;

namespace Cursivis.Application.Presentation;

public enum OrbPresentationState
{
    Idle,
    Listening,
    Thinking,
    Generating,
    Speaking,
    Guiding,
    Executing,
    Done,
    Cancelled,
    Error,
}

public enum ResultPanelStatus
{
    Streaming,
    Ready,
    Notice,
    Error,
}

public enum EffectiveOverlayTheme
{
    Light,
    Dark,
    HighContrast,
}

public readonly record struct ResultPanelLogicalSize(int Width, int Height);

public static class ResultPanelSizeCalculator
{
    public const int DefaultWidth = 780;
    public const int MinimumHeight = 340;
    public const int MaximumInitialHeight = 540;

    public static ResultPanelLogicalSize Calculate(string content, bool hasNotice)
    {
        ArgumentNullException.ThrowIfNull(content);
        int explicitLines = content.Count(static character => character == '\n') + 1;
        int wrappedLines = Math.Max(1, (content.Length + 79) / 80);
        int estimatedLines = Math.Max(explicitLines, wrappedLines);
        int height = 278 + (Math.Min(estimatedLines, 9) * 27) + (hasNotice ? 64 : 0);
        return new ResultPanelLogicalSize(
            DefaultWidth,
            Math.Clamp(height, MinimumHeight, MaximumInitialHeight));
    }
}

public sealed record ResultPanelPresentation(
    ResultPanelStatus Status,
    string OperationLabel,
    string ContextLabel,
    string Content,
    bool CanUndo,
    bool CanInsert,
    bool CanReplace,
    bool CanCopy,
    bool CanTakeAction,
    bool CanRefine,
    string? NoticeTitle = null,
    string? NoticeMessage = null)
{
    public static ResultPanelPresentation FromResult(SmartResult result, bool guidedMode)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new(
            ResultPanelStatus.Ready,
            guidedMode ? "Guided result" : ToDisplayLabel(result.Intent),
            ToDisplayLabel(result.ContextType),
            result.FinalContent,
            CanUndo: false,
            CanInsert: true,
            CanReplace: result.ContextType is not ContextKind.Image and not ContextKind.UserInterface,
            CanCopy: true,
            CanTakeAction: result.SuggestedAction.Type is not SuggestedActionType.None,
            CanRefine: true);
    }

    public static ResultPanelPresentation Failure(string operationLabel, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new(
            ResultPanelStatus.Error,
            operationLabel.Trim(),
            "System status",
            string.Empty,
            CanUndo: false,
            CanInsert: false,
            CanReplace: false,
            CanCopy: false,
            CanTakeAction: false,
            CanRefine: false,
            operationLabel.Trim(),
            message.Trim());
    }

    public ResultPanelPresentation WithNotice(
        ResultPanelStatus status,
        string title,
        string message)
    {
        if (status is not ResultPanelStatus.Notice and not ResultPanelStatus.Error)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return this with
        {
            Status = status,
            NoticeTitle = title.Trim(),
            NoticeMessage = message.Trim(),
        };
    }

    private static string ToDisplayLabel<T>(T value) where T : struct, Enum
    {
        string source = value.ToString();
        var characters = new List<char>(source.Length + 4);
        for (int index = 0; index < source.Length; index++)
        {
            char character = source[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(source[index - 1]))
            {
                characters.Add(' ');
            }

            characters.Add(index == 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
        }

        return new string([.. characters]);
    }
}

public static class OverlayThemeResolver
{
    public static EffectiveOverlayTheme Resolve(
        ApplicationTheme preference,
        bool systemUsesDark,
        bool highContrast)
    {
        if (highContrast)
        {
            return EffectiveOverlayTheme.HighContrast;
        }

        return preference switch
        {
            ApplicationTheme.Light => EffectiveOverlayTheme.Light,
            ApplicationTheme.Dark => EffectiveOverlayTheme.Dark,
            ApplicationTheme.System when systemUsesDark => EffectiveOverlayTheme.Dark,
            ApplicationTheme.System => EffectiveOverlayTheme.Light,
            _ => throw new ArgumentOutOfRangeException(nameof(preference)),
        };
    }
}
