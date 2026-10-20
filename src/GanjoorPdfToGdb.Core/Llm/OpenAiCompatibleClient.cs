using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GanjoorPdfToGdb;

public sealed record ChatResult(string Content, int PromptTokens, int CompletionTokens);

/// <summary>
/// Minimal client for any OpenAI-compatible /chat/completions endpoint with image input
/// (OpenAI, Avalai, OpenRouter, Gemini's compatibility layer, vLLM, Ollama, LM Studio, ...).
/// </summary>
public sealed class OpenAiCompatibleClient : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly HttpClient _http;
    private bool _jsonModeEnabled;

    public OpenAiCompatibleClient(AppConfig cfg, HttpMessageHandler? handler = null)
    {
        _cfg = cfg;
        _jsonModeEnabled = cfg.UseJsonResponseFormat;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(10, cfg.RequestTimeoutSeconds));
        if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
    }

    public async Task<ChatResult> CompleteAsync(string systemPrompt, string userText, byte[] imagePng, CancellationToken ct = default)
    {
        string url = _cfg.BaseUrl.TrimEnd('/') + "/chat/completions";
        Exception? last = null;

        for (int attempt = 0; attempt <= _cfg.MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << attempt)), ct);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(BuildBody(systemPrompt, userText, imagePng), Encoding.UTF8, "application/json")
                };
                using var resp = await _http.SendAsync(req, ct);
                string body = await resp.Content.ReadAsStringAsync(ct);

                if (resp.IsSuccessStatusCode)
                    return Parse(body);

                // Some compatible servers reject response_format: retry once without it.
                if (resp.StatusCode == HttpStatusCode.BadRequest && _jsonModeEnabled &&
                    body.Contains("response_format", StringComparison.OrdinalIgnoreCase))
                {
                    _jsonModeEnabled = false;
                    attempt--; // not a real failure
                    continue;
                }

                bool transient = resp.StatusCode == HttpStatusCode.TooManyRequests || (int)resp.StatusCode >= 500;
                last = new HttpRequestException($"HTTP {(int)resp.StatusCode}: {Trim(body)}");
                if (!transient) throw last;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new TimeoutException("Request timed out.");
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                if (ex.Message.StartsWith("HTTP 4") && !ex.Message.StartsWith("HTTP 429")) throw;
            }
        }
        throw new InvalidOperationException("LLM request failed after retries: " + last?.Message, last);
    }

    private string BuildBody(string systemPrompt, string userText, byte[] imagePng)
    {
        var body = new JsonObject
        {
            ["model"] = _cfg.Model,
            ["temperature"] = _cfg.Temperature,
            [_cfg.MaxTokensParameter] = _cfg.MaxOutputTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = userText },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(imagePng) }
                        }
                    }
                }
            }
        };
        if (_jsonModeEnabled)
            body["response_format"] = new JsonObject { ["type"] = "json_object" };
        return body.ToJsonString();
    }

    private static ChatResult Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string content = "";
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var c))
        {
            // Content is normally a string; a few servers return an array of parts.
            if (c.ValueKind == JsonValueKind.String) content = c.GetString() ?? "";
            else if (c.ValueKind == JsonValueKind.Array)
                content = string.Concat(c.EnumerateArray().Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : ""));
        }
        int pt = 0, ct = 0;
        if (root.TryGetProperty("usage", out var u))
        {
            if (u.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv)) pt = pv;
            if (u.TryGetProperty("completion_tokens", out var q) && q.TryGetInt32(out var qv)) ct = qv;
        }
        return new ChatResult(content, pt, ct);
    }

    private static string Trim(string s) => s.Length <= 400 ? s : s[..400] + "…";

    public void Dispose() => _http.Dispose();
}
