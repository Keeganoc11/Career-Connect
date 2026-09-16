using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>Thrown when any step of layout tailoring can't get a usable answer from the model.</summary>
public class ResumeTailoringException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// One structured-output request and its failure handling, shared by the
/// layout tailoring steps. The older Claude services each carry their own copy
/// of this; these three are close enough in shape to share one.
/// </summary>
public sealed class ClaudeStructuredCaller
{
    private readonly AnthropicClient? _client;
    private readonly string _model;
    private readonly Effort _effort;

    public ClaudeStructuredCaller(IConfiguration configuration)
    {
        _model = AnthropicClientFactory.ResolveModel(configuration);
        _effort = AnthropicClientFactory.ResolveEffort(configuration);
        _client = AnthropicClientFactory.CreateClient(configuration);
    }

    public bool IsConfigured => _client is not null;

    /// <param name="step">What this call is doing, for error messages: "rewriting your resume".</param>
    public async Task<T> CallAsync<T>(
        string step,
        string systemPrompt,
        string userPrompt,
        object schema,
        CancellationToken cancellationToken,
        int maxTokens = 16000)
    {
        if (_client is null)
        {
            throw new ResumeTailoringException("No Anthropic API key is configured.");
        }

        Message response;
        try
        {
            response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = maxTokens,
                System = systemPrompt,
                OutputConfig = new OutputConfig
                {
                    Effort = _effort,
                    Format = new JsonOutputFormat { Schema = ToSchema(schema) },
                },
                Messages = [new() { Role = Role.User, Content = userPrompt }],
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ResumeTailoringException($"The request to Claude failed while {step}.", ex);
        }

        if (response.StopReason == "refusal")
        {
            throw new ResumeTailoringException(
                $"Claude declined while {step}. Check the job description for anything unexpected.");
        }

        if (response.StopReason == "max_tokens")
        {
            throw new ResumeTailoringException($"Claude's answer was cut off while {step}. Try a shorter job description.");
        }

        var json = AnthropicResponse.ExtractText(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ResumeTailoringException($"Claude returned an empty answer while {step}.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, AnthropicResponse.SnakeCaseJsonOptions)
                ?? throw new ResumeTailoringException($"Claude returned an empty answer while {step}.");
        }
        catch (JsonException ex)
        {
            throw new ResumeTailoringException($"Claude's answer couldn't be read while {step}.", ex);
        }
    }

    private static Dictionary<string, JsonElement> ToSchema(object schema) =>
        JsonSerializer.SerializeToElement(schema)
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

    /// <summary>A JSON-schema object with every property required and nothing extra — what structured outputs expect.</summary>
    public static object SchemaObject(object properties, params string[] required) => new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = required,
        ["additionalProperties"] = false,
    };

    public static object SchemaArray(object items, string description) => new { type = "array", items, description };

    public static object SchemaString(string description) => new { type = "string", description };

    public static object SchemaEnum(string description, params string[] values) => new { type = "string", @enum = values, description };
}
