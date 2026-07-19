using System.Text.Json;
using Cursivis.Application.OpenAI;

namespace Cursivis.Application.Realtime;

/// <summary>
/// Small allowlist for the context tools available in the first Live Mode vertical slice.
/// Arguments are treated as untrusted and no arbitrary tool name can cross this boundary.
/// </summary>
public sealed class LiveModeToolExecutor : ILiveModeToolExecutor
{
    private const string EmptyObjectSchema = """
        {
          "type": "object",
          "properties": {},
          "required": [],
          "additionalProperties": false
        }
        """;

    private static readonly RealtimeToolDefinition[] ToolDefinitions =
    [
        new(
            "get_selected_text",
            "Return the text selected when this Live Mode session began, if any.",
            EmptyObjectSchema),
        new(
            "get_active_application",
            "Return the application and window that were active when this Live Mode session began.",
            EmptyObjectSchema),
        new(
            "cancel_current_operation",
            "End the current Cursivis Live Mode session immediately when the user asks to stop.",
            EmptyObjectSchema),
    ];

    public IReadOnlyList<RealtimeToolDefinition> Definitions => ToolDefinitions;

    public ValueTask<LiveModeToolExecutionResult> ExecuteAsync(
        LiveModeContext context,
        string toolName,
        string untrustedArgumentsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEmptyArguments(untrustedArgumentsJson);

        LiveModeToolExecutionResult result = toolName switch
        {
            "get_selected_text" => new(JsonSerializer.Serialize(new
            {
                available = !string.IsNullOrWhiteSpace(context.SelectedText),
                text = context.SelectedText,
                contextFingerprint = context.ContextFingerprint,
            })),
            "get_active_application" => new(JsonSerializer.Serialize(new
            {
                available = !string.IsNullOrWhiteSpace(context.ActiveApplication),
                application = context.ActiveApplication,
                windowTitle = context.ActiveWindowTitle,
            })),
            "cancel_current_operation" => new("{\"cancelled\":true}", StopSession: true),
            _ => throw new InvalidOperationException("The requested Live Mode tool is not allowlisted."),
        };

        return ValueTask.FromResult(result);
    }

    private static void ValidateEmptyArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            json = "{}";
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            document.RootElement.EnumerateObject().Any())
        {
            throw new InvalidOperationException("The Live Mode tool received unsupported arguments.");
        }
    }
}
