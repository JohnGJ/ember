using System.Management;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ember;

// Claude Code keeps its OAuth tokens in ~/.claude/.credentials.json and refreshes
// them while it runs. The access token only lasts a few hours, so when Claude Code
// isn't running we refresh it ourselves and write the result back for it to use.
static class Credentials
{
    const string TokenEndpoint = "https://platform.claude.com/v1/oauth/token";
    const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // A refresh that couldn't be saved is kept here so the rotated token isn't lost.
    static (string Spent, Tokens Fresh)? unsaved;

    sealed record Tokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    static string FilePath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            return Path.Combine(dir, ".credentials.json");
        }
    }

    public static async Task<string> AccessTokenAsync(HttpClient http)
    {
        var path = FilePath;
        if (!File.Exists(path))
            throw new UsageException("No Claude Code sign-in found. Run claude and sign in first.");

        var oauth = await ReadAsync(path);
        var token = Text(oauth, "accessToken")
            ?? throw new UsageException("Claude Code's credentials file has no access token.");

        var expiresAt = oauth["expiresAt"] is JsonValue value && value.TryGetValue<long>(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : (DateTimeOffset?)null;
        if (expiresAt is null || expiresAt > DateTimeOffset.UtcNow + RefreshMargin) return token;

        // Two refreshes racing could leave Claude Code signed out, so leave it to
        // Claude Code while it's running.
        if (await Task.Run(ClaudeCodeRunning))
        {
            if (expiresAt > DateTimeOffset.UtcNow) return token;
            throw new UsageException("Claude Code's sign-in has expired. It will refresh it when you next use it.");
        }

        var refreshToken = Text(oauth, "refreshToken")
            ?? throw new UsageException("Claude Code's sign-in has expired. Run claude to sign in again.");

        var fresh = unsaved?.Spent == refreshToken ? unsaved.Value.Fresh : await RefreshAsync(http, refreshToken);
        unsaved = (refreshToken, fresh);

        // Claude Code may have started and refreshed in the meantime; its tokens win.
        var latest = await ReadAsync(path);
        if (Text(latest, "refreshToken") != refreshToken)
        {
            unsaved = null;
            return Text(latest, "accessToken") ?? fresh.AccessToken;
        }

        latest["accessToken"] = fresh.AccessToken;
        latest["refreshToken"] = fresh.RefreshToken;
        latest["expiresAt"] = fresh.ExpiresAt.ToUnixTimeMilliseconds();
        await SaveAsync(path, latest.Root);
        unsaved = null;
        return fresh.AccessToken;
    }

    static async Task<JsonObject> ReadAsync(string path)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(await File.ReadAllBytesAsync(path));
        }
        catch (JsonException)
        {
            throw new UsageException("Claude Code's credentials file couldn't be read.");
        }
        return root?["claudeAiOauth"] as JsonObject
            ?? throw new UsageException("Claude Code isn't signed in to Claude.ai. Run claude and sign in first.");
    }

    static string? Text(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    static async Task<Tokens> RefreshAsync(HttpClient http, string refreshToken)
    {
        var body = new { grant_type = "refresh_token", refresh_token = refreshToken, client_id = ClientId };
        using var response = await http.PostAsJsonAsync(TokenEndpoint, body);
        if (!response.IsSuccessStatusCode)
            throw new UsageException("Couldn't refresh Claude Code's sign-in. Run claude to sign in again.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var json = doc.RootElement;
        var access = json.GetProperty("access_token").GetString()!;
        var refresh = json.TryGetProperty("refresh_token", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()!
            : refreshToken;
        var expiresIn = json.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : 3600;
        return new Tokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    static async Task SaveAsync(string path, JsonNode root)
    {
        var temp = path + ".tmp";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await File.WriteAllTextAsync(temp, root.ToJsonString());
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(200);
            }
        }
    }

    // The native build is claude.exe, usually in ~\.local\bin; the npm build is
    // node.exe running @anthropic-ai\claude-code. The desktop app is also called
    // Claude.exe but lives under WindowsApps or AnthropicClaude.
    static bool ClaudeCodeRunning()
    {
        using var query = new ManagementObjectSearcher(
            "SELECT Name, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'claude.exe' OR Name = 'node.exe'");
        using var processes = query.Get();

        foreach (var process in processes)
        {
            var exe = process["ExecutablePath"] as string ?? "";
            var command = process["CommandLine"] as string ?? "";
            var isClaudeCode = string.Equals(process["Name"] as string, "claude.exe", StringComparison.OrdinalIgnoreCase)
                ? !exe.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                    && !exe.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase)
                : command.Contains("claude-code", StringComparison.OrdinalIgnoreCase);
            if (isClaudeCode) return true;
        }
        return false;
    }
}
