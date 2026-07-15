using System.ClientModel;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cursivis.Application.OpenAI;
using Cursivis.Contracts.OpenAI;
using Cursivis.Domain.Models;
using OpenAI.Responses;

namespace Cursivis.Infrastructure.OpenAI;

public sealed partial class OpenAiResponsesGateway(
    IOpenAiCredentialSource credentialSource,
    TimeProvider? timeProvider = null) : IResponsesGateway
{
    private const int MaximumInstructionCharacters = 20_000;
    private const int MaximumInputCharacters = 100_000;
    private const int MaximumSchemaCharacters = 100_000;
    private static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromMinutes(2);

    private readonly IOpenAiCredentialSource _credentialSource = credentialSource
        ?? throw new ArgumentNullException(nameof(credentialSource));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<StructuredResponseResult> CreateStructuredResponseAsync(
        StructuredResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        try
        {
            return await _credentialSource.UseApiKeyAsync(
                (apiKey, keyCancellationToken) => ExecuteAsync(apiKey, request, keyCancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OpenAiCredentialUnavailableException)
        {
            return StructuredResponseResult.Failed(new OpenAiFailure(
                OpenAiFailureKind.Authentication,
                "An OpenAI API key has not been configured.",
                false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StructuredResponseResult.Failed(new OpenAiFailure(
                OpenAiFailureKind.Cancelled,
                "The OpenAI request was cancelled.",
                false));
        }
    }

    public async Task<ModelAvailabilityResult> CheckModelAvailabilityAsync(
        string model,
        CancellationToken cancellationToken = default)
    {
        CursivisModelCatalog.GetRequired(model);
        StructuredResponseRequest request = new(
            model,
            "Return the requested readiness object. Do not add commentary.",
            "Return an object whose ok property is true.",
            "cursivis_model_readiness",
            """
            {
              "type": "object",
              "properties": { "ok": { "const": true } },
              "required": ["ok"],
              "additionalProperties": false
            }
            """,
            TimeSpan.FromSeconds(20));

        StructuredResponseResult result = await CreateStructuredResponseAsync(request, cancellationToken).ConfigureAwait(false);
        return new ModelAvailabilityResult(model, result.Succeeded, result.Failure, _timeProvider.GetUtcNow());
    }

    private static async Task<StructuredResponseResult> ExecuteAsync(
        string apiKey,
        StructuredResponseRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new OpenAiCredentialUnavailableException();
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        try
        {
            ResponsesClient client = new(apiKey);
            CreateResponseOptions options = new(
                request.Model,
                [
                    ResponseItem.CreateDeveloperMessageItem(request.SystemInstruction),
                    ResponseItem.CreateUserMessageItem(request.UserContent),
                ])
            {
                StoredOutputEnabled = false,
                MaxOutputTokenCount = 8_192,
                TextOptions = new ResponseTextOptions
                {
                    TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                        request.SchemaName,
                        BinaryData.FromString(request.JsonSchema),
                        "Cursivis validated structured response",
                        jsonSchemaIsStrict: true),
                },
            };

            ClientResult<ResponseResult> result = await client.CreateResponseAsync(options, timeout.Token).ConfigureAwait(false);
            ResponseResult response = result.Value;
            string? output = response.GetOutputText();
            if (string.IsNullOrWhiteSpace(output) || !IsJsonObject(output))
            {
                return StructuredResponseResult.Failed(new OpenAiFailure(
                    OpenAiFailureKind.MalformedResponse,
                    "OpenAI returned a response that did not match the required structure.",
                    false,
                    response.Id));
            }

            return StructuredResponseResult.Success(output, response.Model, response.Id);
        }
        catch (ClientResultException exception)
        {
            return StructuredResponseResult.Failed(Classify(exception));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StructuredResponseResult.Failed(new OpenAiFailure(
                OpenAiFailureKind.Timeout,
                "The OpenAI request timed out.",
                true));
        }
        catch (HttpRequestException)
        {
            return StructuredResponseResult.Failed(new OpenAiFailure(
                OpenAiFailureKind.Network,
                "Cursivis could not reach OpenAI.",
                true));
        }
        catch (JsonException)
        {
            return StructuredResponseResult.Failed(new OpenAiFailure(
                OpenAiFailureKind.MalformedResponse,
                "OpenAI returned malformed structured data.",
                false));
        }
    }

    private static void ValidateRequest(StructuredResponseRequest request)
    {
        OpenAiModelDescriptor descriptor = CursivisModelCatalog.GetRequired(request.Model);
        if (!descriptor.Model.Capabilities.HasFlag(ModelCapabilities.StructuredOutputs))
        {
            throw new ArgumentException("The selected model does not support Structured Outputs.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SystemInstruction)
            || request.SystemInstruction.Length > MaximumInstructionCharacters)
        {
            throw new ArgumentException("The system instruction is empty or too large.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.UserContent) || request.UserContent.Length > MaximumInputCharacters)
        {
            throw new ArgumentException("The user input is empty or too large.", nameof(request));
        }

        if (!SchemaNamePattern().IsMatch(request.SchemaName))
        {
            throw new ArgumentException("The schema name is invalid.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.JsonSchema) || request.JsonSchema.Length > MaximumSchemaCharacters)
        {
            throw new ArgumentException("The JSON schema is empty or too large.", nameof(request));
        }

        using JsonDocument schema = JsonDocument.Parse(request.JsonSchema);
        if (schema.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The JSON schema root must be an object.", nameof(request));
        }

        if (request.Timeout < TimeSpan.FromSeconds(1) || request.Timeout > MaximumRequestTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The request timeout is outside the supported range.");
        }
    }

    private static OpenAiFailure Classify(ClientResultException exception)
    {
        return exception.Status switch
        {
            (int)HttpStatusCode.Unauthorized => new(
                OpenAiFailureKind.Authentication,
                "OpenAI rejected the configured API key.",
                false),
            (int)HttpStatusCode.Forbidden => new(
                OpenAiFailureKind.Permission,
                "The OpenAI project does not permit this request or model.",
                false),
            (int)HttpStatusCode.NotFound => new(
                OpenAiFailureKind.ModelUnavailable,
                "The selected OpenAI model is not available to this project.",
                false),
            (int)HttpStatusCode.RequestTimeout => new(
                OpenAiFailureKind.Timeout,
                "The OpenAI request timed out.",
                true),
            (int)HttpStatusCode.TooManyRequests => new(
                OpenAiFailureKind.RateLimit,
                "OpenAI rate or usage limits prevented this request.",
                true),
            >= 500 => new(
                OpenAiFailureKind.Network,
                "OpenAI is temporarily unavailable.",
                true),
            _ => new(
                OpenAiFailureKind.Unknown,
                "The OpenAI request failed.",
                false),
        };
    }

    private static bool IsJsonObject(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        return document.RootElement.ValueKind == JsonValueKind.Object;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaNamePattern();
}
