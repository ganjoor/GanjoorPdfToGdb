using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GanjoorPdfToGdb;

public enum LinkOutcome { Created, AlreadySuggested, Failed }

public sealed record LinkResult(LinkOutcome Outcome, string? Message);

/// <summary>
/// Client for the two Naskban API calls this tool needs: login (POST api/users/login) and suggesting a link between a
/// ganjoor poem and a PDF page (POST api/pdf/ganjoor). Suggestions go to Naskban's moderation queue.
/// </summary>
public sealed class NaskbanClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api.naskban.ir";
    public const string ClientAppName = "GanjoorPdfToGdb";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _user;
    private readonly string _password;
    private string? _token;
    private string? _sessionId;

    public NaskbanClient(string username, string password, string? baseUrl = null, HttpMessageHandler? handler = null)
    {
        _user = username;
        _password = password;
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl).TrimEnd('/');
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public string? UserName { get; private set; }

    /// <summary>Logs in and returns null, or returns the server's error text.</summary>
    public async Task<string?> LoginAsync(CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { username = _user, password = _password, clientAppName = ClientAppName, language = "fa-IR" });
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/users/login")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        string text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return $"HTTP {(int)resp.StatusCode}: {Clean(text)}";
        return ReadSession(text);
    }

    private string? ReadSession(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            _token = Prop(root, "token");
            _sessionId = Prop(root, "sessionId");
            if (root.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
                UserName = Prop(u, "email") ?? Prop(u, "userName") ?? Prop(u, "name");
            if (string.IsNullOrEmpty(_token)) return "The login answer contained no token.";
            return null;
        }
        catch (JsonException ex)
        {
            return "The login answer is not JSON: " + ex.Message;
        }
    }

    private static string? Prop(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return null;
    }

    /// <summary>Renews an expired token (relogin by session id, else a fresh login).</summary>
    private async Task<string?> RenewAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_sessionId))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, _baseUrl + "/api/users/relogin/" + _sessionId);
                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode && ReadSession(await resp.Content.ReadAsStringAsync(ct)) == null) return null;
            }
            catch (HttpRequestException) { /* fall through to a full login */ }
        }
        return await LoginAsync(ct);
    }

    /// <summary>Suggests the link of one ganjoor poem to one PDF page.</summary>
    public async Task<LinkResult> SuggestLinkAsync(int ganjoorPostId, string ganjoorUrl, string ganjoorTitle, int pdfBookId, int pageNumber,
        bool isTextOriginalSource, bool suggestedByMachine, CancellationToken ct = default)
    {
        if (_token == null)
        {
            string? err = await LoginAsync(ct);
            if (err != null) return new LinkResult(LinkOutcome.Failed, "login failed: " + err);
        }

        string body = JsonSerializer.Serialize(new
        {
            ganjoorPostId,
            ganjoorUrl,
            ganjoorTitle,
            pdfBookId,
            pageNumber,
            isTextOriginalSource,
            suggestedByMachine
        });

        bool renewed = false;
        string lastError = "unknown error";
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/api/pdf/ganjoor")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var resp = await _http.SendAsync(req, ct);
                string text = await resp.Content.ReadAsStringAsync(ct);

                if (resp.IsSuccessStatusCode) return new LinkResult(LinkOutcome.Created, null);

                if (resp.StatusCode == HttpStatusCode.Unauthorized && !renewed)
                {
                    renewed = true;
                    string? err = await RenewAsync(ct);
                    if (err != null) return new LinkResult(LinkOutcome.Failed, "session expired and login failed: " + err);
                    attempt--; // the renewal is not a failed attempt
                    continue;
                }
                string msg = Clean(text);
                // the service answers 400 with this text when the same poem/page pair was suggested before
                if (resp.StatusCode == HttpStatusCode.BadRequest && msg.Contains("پیشتر پیشنهاد شده", StringComparison.Ordinal))
                    return new LinkResult(LinkOutcome.AlreadySuggested, msg);
                if ((int)resp.StatusCode >= 500) { lastError = $"HTTP {(int)resp.StatusCode}: {msg}"; continue; } // try again
                return new LinkResult(LinkOutcome.Failed, $"HTTP {(int)resp.StatusCode}: {msg}");
            }
            catch (HttpRequestException ex) { lastError = ex.Message + (ex.InnerException != null ? " (" + ex.InnerException.Message + ")" : ""); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { lastError = "timeout"; }
        }
        return new LinkResult(LinkOutcome.Failed, lastError);
    }

    private static string Clean(string s)
    {
        s = s.Trim().Trim('"');
        return s.Length > 400 ? s[..400] + "…" : s;
    }

    public void Dispose() => _http.Dispose();
}
