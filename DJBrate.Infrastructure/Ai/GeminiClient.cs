using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DJBrate.Application.Interfaces;
using DJBrate.Application.Models.Ai;
using Microsoft.Extensions.Configuration;

namespace DJBrate.Infrastructure.Ai;

public class GeminiClient : IAiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const int MaxRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _modelName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GeminiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey    = configuration["Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        _modelName = configuration["Gemini:ModelName"] ?? "gemini-2.5-flash";
    }

    public async Task<AiResponse> SendMessageAsync(
        string systemPrompt,
        List<AiMessage> conversationHistory,
        List<AiToolDefinition> tools,
        float? temperature = null,
        int? maxOutputTokens = null)
    {
        var request = BuildRequest(systemPrompt, conversationHistory, tools, temperature, maxOutputTokens);
        var url = $"{BaseUrl}/{_modelName}:generateContent?key={_apiKey}";

        var http = _httpClientFactory.CreateClient();
        HttpResponseMessage response = null!;
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            response = await http.PostAsJsonAsync(url, request, JsonOptions);
            if (!IsTransientError(response.StatusCode))
                break;
            if (attempt < MaxRetries - 1)
                await Task.Delay(1000 * (attempt + 1));
        }
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return ParseResponse(json!);
    }

    private static object BuildRequest(
        string systemPrompt,
        List<AiMessage> conversationHistory,
        List<AiToolDefinition> tools,
        float? temperature,
        int? maxOutputTokens)
    {
        var contents = new List<object>();

        foreach (var msg in conversationHistory)
        {
            if (msg.ToolResult is not null)
            {
                var parsedResult = ParseToolResult(msg.ToolResult.Result);
                contents.Add(new
                {
                    role = "function",
                    parts = new object[]
                    {
                        new
                        {
                            functionResponse = new
                            {
                                name = msg.ToolResult.ToolCallId,
                                response = parsedResult
                            }
                        }
                    }
                });
            }
            else if (msg.ToolCall is not null)
            {
                contents.Add(new
                {
                    role = "model",
                    parts = new object[]
                    {
                        new
                        {
                            functionCall = new
                            {
                                name = msg.ToolCall.Name,
                                args = msg.ToolCall.Arguments
                            }
                        }
                    }
                });
            }
            else
            {
                contents.Add(new
                {
                    role = msg.Role == "assistant" ? "model" : "user",
                    parts = new object[] { new { text = msg.Text ?? "" } }
                });
            }
        }

        var functionDeclarations = tools.Select(t => new
        {
            name        = t.Name,
            description = t.Description,
            parameters  = t.Parameters
        }).ToList();

        var generationConfig = new Dictionary<string, object>();
        if (temperature.HasValue)      generationConfig["temperature"]     = temperature.Value;
        if (maxOutputTokens.HasValue)  generationConfig["maxOutputTokens"] = maxOutputTokens.Value;

        return new
        {
            systemInstruction = new
            {
                parts = new object[] { new { text = systemPrompt } }
            },
            contents,
            tools = new object[]
            {
                new { functionDeclarations }
            },
            generationConfig
        };
    }

    private static object ParseToolResult(string json)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return element.ValueKind == JsonValueKind.Array
            ? new { results = element }
            : element;
    }

    private static bool IsTransientError(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable;

    private static AiResponse ParseResponse(JsonDocument doc)
    {
        var response = new AiResponse();
        var candidates = doc.RootElement.GetProperty("candidates");
        var parts = candidates[0].GetProperty("content").GetProperty("parts");

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("functionCall", out var fc))
            {
                response.ToolCalls.Add(new AiToolCall
                {
                    Id        = fc.GetProperty("name").GetString()!,
                    Name      = fc.GetProperty("name").GetString()!,
                    Arguments = JsonDocument.Parse(fc.GetProperty("args").GetRawText())
                });
            }
            else if (part.TryGetProperty("text", out var text))
            {
                response.Text = (response.Text ?? "") + text.GetString();
            }
        }

        return response;
    }
}
