using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace CareerConnect.Api.Services;

/// <summary>
/// Config resolution and client construction shared by the Claude-backed
/// services, so they can't drift on how the API key/model is read.
/// </summary>
public static class AnthropicClientFactory
{
    public static string ResolveModel(IConfiguration configuration) =>
        configuration["Anthropic:Model"] ?? "claude-opus-5";

    /// <summary>Null when no API key is configured — callers treat that as "feature disabled".</summary>
    public static AnthropicClient? CreateClient(IConfiguration configuration)
    {
        var apiKey = configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(apiKey) ? null : new AnthropicClient { ApiKey = apiKey };
    }
}

/// <summary>Helpers for reading a structured-output response shared by the Claude-backed services.</summary>
public static class AnthropicResponse
{
    public static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>The first text block's content, or null if the response has none.</summary>
    public static string? ExtractText(Message response) =>
        response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .FirstOrDefault();
}
