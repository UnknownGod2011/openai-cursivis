#if DEBUG
using System.Text.Json;
using Cursivis.Application.Context;
using Cursivis.Application.OpenAI;
using Cursivis.Contracts.OpenAI;

namespace Cursivis.Windows.App.Testing;

/// <summary>
/// Enables repeatable real-window interaction checks without using a credential or network.
/// It is compiled only into Debug builds and must be explicitly enabled by environment variable.
/// </summary>
internal sealed class DeterministicResponsesGateway : IResponsesGateway
{
    public async Task<StructuredResponseResult> CreateStructuredResponseAsync(
        StructuredResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
        string operation = ReadOperation(request.UserContent);
        string selectedText = ReadSelectedText(request.UserContent);
        string finalContent = operation == "Smart"
            ? $"Mock Smart Mode result for: {selectedText}"
            : $"Mock Guided Mode {operation} result for the same context: {selectedText}";

        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            contextType = TextContextKindClassifier.Classify(selectedText)
                .ToString()
                .ToLowerInvariant(),
            intent = GetIntent(operation),
            confidence = 0.97,
            finalContent,
            suggestedAction = new
            {
                type = "replace_selection",
                summary = "Replace the selected text",
            },
            riskHint = "low",
            confirmationHint = "context_dependent",
        });

        return StructuredResponseResult.Success(
            json,
            request.Model,
            $"debug-{request.OperationId}");
    }

    public Task<ModelAvailabilityResult> CheckModelAvailabilityAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ModelAvailabilityResult(model, true, null, DateTimeOffset.UtcNow));
    }

    private static string ReadOperation(string userContent)
    {
        const string marker = "Requested operation: ";
        int start = userContent.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return "Smart";
        }

        start += marker.Length;
        int end = userContent.IndexOfAny(['\r', '\n'], start);
        return (end < 0 ? userContent[start..] : userContent[start..end]).Trim();
    }

    private static string ReadSelectedText(string userContent)
    {
        const string opening = "<selected_content>";
        const string closing = "</selected_content>";
        int start = userContent.IndexOf(opening, StringComparison.Ordinal);
        if (start < 0)
        {
            return Normalize(userContent);
        }

        start += opening.Length;
        int end = userContent.IndexOf(closing, start, StringComparison.Ordinal);
        return Normalize(end < 0 ? userContent[start..] : userContent[start..end]);
    }

    private static string Normalize(string value)
    {
        string normalized = value.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= 180 ? normalized : $"{normalized[..180]}...";
    }

    private static string GetIntent(string operation) => operation switch
    {
        "Summarize" => "summarize",
        "Explain" => "explain",
        "Translate" => "translate",
        "DraftReply" => "reply",
        "ExtractKeyPoints" or "TurnIntoTasks" => "extract",
        "AskQuestion" => "answer",
        "Debug" => "debug",
        "Describe" or "IdentifyObjects" => "identify",
        _ => "rewrite",
    };
}
#endif
