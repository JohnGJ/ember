using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Ember;

sealed class UsageException(string message) : Exception(message);

sealed class UsageClient
{
    const string Endpoint = "https://api.anthropic.com/api/oauth/usage";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly string UserAgent = $"Ember/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(2)}";

    public async Task<Usage> FetchAsync()
    {
        var token = await Credentials.AccessTokenAsync(Http);

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await Http.SendAsync(request);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                throw new UsageException("Claude Code's sign-in was rejected. Run claude to sign in again.");
            case HttpStatusCode.TooManyRequests:
                throw new UsageException("The usage API is rate limiting Ember. Backing off.");
        }
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return Parse(doc.RootElement);
    }

    static Usage Parse(JsonElement root)
    {
        Limit? week = null, session = null;
        var scoped = new List<Limit>();

        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in limits.EnumerateArray())
            {
                if (!entry.TryGetProperty("percent", out var percent) || percent.ValueKind != JsonValueKind.Number
                    || !entry.TryGetProperty("resets_at", out var resets) || resets.ValueKind != JsonValueKind.String
                    || !resets.TryGetDateTimeOffset(out var resetsAt))
                    continue;

                switch (Text(entry, "kind"))
                {
                    case "session": session = new("Session", percent.GetDouble(), resetsAt); break;
                    case "weekly_all": week = new("Week", percent.GetDouble(), resetsAt); break;
                    case "weekly_scoped": scoped.Add(new(ScopeName(entry), percent.GetDouble(), resetsAt)); break;
                }
            }
        }

        if (week is null && session is null && scoped.Count == 0)
            throw new UsageException("The usage API sent something unexpected.");

        return new Usage(week, scoped, session, DateTimeOffset.Now);
    }

    static string ScopeName(JsonElement entry)
    {
        if (entry.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object)
        {
            if (scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
                && Text(model, "display_name") is { } name)
                return name;
            if (Text(scope, "surface") is { } surface)
                return surface;
        }
        return "Scoped";
    }

    static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
