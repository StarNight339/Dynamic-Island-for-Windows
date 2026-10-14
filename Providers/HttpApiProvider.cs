using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using DynamicIsland.Core;

namespace DynamicIsland.Providers;

/// <summary>
/// Local HTTP API so scripts / AI tools can push things onto the island. Bound to localhost only.
///   GET  /         health check
///   POST /notify   {"title", "body"?, "icon"?, "image"?, "color"?, "duration"?}
///                  icon  = Fluent glyph ("\uE73E") or emoji ("🎉"); image = http(s) URL, file path or data: URI
///   POST /claude   Claude Code hook JSON as-is, or {"event": "working|waiting|done|idle", "message"?, "project"?}
///   POST /timer    {"seconds"}  (0 cancels)
///   POST /progress {"id"?, "progress" (0-100), "title"?, "body"?, "icon"?, "image"?, "color"?, "dismiss"?}
///                  same id updates the bar in place; omitted fields keep their value; 100 finishes it
/// </summary>
public sealed class HttpApiProvider(
    int port,
    Dispatcher dispatcher,
    Notifier notifier,
    ClaudeProvider claude,
    ClockTimerProvider timer,
    ProgressProvider progress) : IDisposable
{
    /// <summary>Room for a 5 MB image sent as a base64 data: URI (~6.7 MB) plus the rest of the JSON.</summary>
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public string Prefix => $"http://localhost:{port}/";

    public void Start()
    {
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (_cts.IsCancellationRequested) { return; }
            catch (HttpListenerException) { return; }

            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
        try
        {
            // Browsers attach Origin to every cross-site POST (even mode:"no-cors", which skips the CORS preflight),
            // so this stops any web page from pushing fake notifications or making the app fetch URLs / read files.
            // curl, Python, Claude Code hooks and other local tools never send it.
            if (req.Headers["Origin"] is not null)
            {
                await Reply(ctx, 403, """{"error":"browser requests are not allowed"}""");
                return;
            }
            if (req.HttpMethod == "GET" && path == "")
            {
                await Reply(ctx, 200, """{"ok":true,"app":"DynamicIsland"}""");
                return;
            }
            if (req.HttpMethod != "POST")
            {
                await Reply(ctx, 405, """{"error":"use POST"}""");
                return;
            }

            var text = await ReadBodyAsync(req);
            if (text is null)
            {
                await Reply(ctx, 413, """{"error":"request too large"}""");
                return;
            }
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var json = doc.RootElement;

            Action? action = path switch
            {
                "/notify" => await NotifyActionAsync(json),
                "/claude" => ClaudeAction(json),
                "/timer" => TimerAction(json),
                "/progress" => await ProgressActionAsync(json),
                _ => null,
            };
            if (action is null)
            {
                await Reply(ctx, 404, """{"error":"unknown endpoint"}""");
                return;
            }

            await dispatcher.InvokeAsync(action);
            await Reply(ctx, 200, """{"ok":true}""");
        }
        catch (JsonException)
        {
            await Reply(ctx, 400, """{"error":"invalid JSON"}""");
        }
        catch (Exception ex)
        {
            await Reply(ctx, 500, JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    private async Task<Action> NotifyActionAsync(JsonElement json)
    {
        var title = Str(json, "title") ?? "Notification";
        var body = Str(json, "body") ?? Str(json, "message");
        var icon = Str(json, "icon") ?? "\uEA8F";
        var color = Str(json, "color");
        var seconds = Num(json, "duration") ?? 5;
        var image = await ImageLoader.LoadAsync(Str(json, "image"));
        return () => notifier.Show(title, body, icon, Notifier.ParseBrush(color, Notifier.Brush("AccentClaude")), seconds, image);
    }

    private Action ClaudeAction(JsonElement json)
    {
        var evt = Str(json, "hook_event_name") ?? Str(json, "event") ?? "";
        var sessionId = Str(json, "session_id");
        var cwd = Str(json, "cwd");
        var message = Str(json, "message");
        var tool = Str(json, "tool_name");
        var prompt = Str(json, "prompt");
        var project = Str(json, "project");
        return () => claude.Handle(evt, sessionId, cwd, message, tool, prompt, project);
    }

    private Action TimerAction(JsonElement json)
    {
        var seconds = Num(json, "seconds") ?? 0;
        return () =>
        {
            if (seconds <= 0) timer.Cancel();
            else timer.Start(TimeSpan.FromSeconds(seconds));
        };
    }

    private async Task<Action> ProgressActionAsync(JsonElement json)
    {
        var id = Str(json, "id") ?? "default";
        if (Bool(json, "dismiss")) return () => progress.Dismiss(id);

        var title = Str(json, "title");
        var body = Str(json, "body") ?? Str(json, "message");
        var icon = Str(json, "icon");
        var color = Str(json, "color");
        var percent = Num(json, "progress");
        var image = await ImageLoader.LoadAsync(Str(json, "image"));
        return () => progress.Update(id, title, body, icon, color, image, percent);
    }

    /// <summary>Reads the body as UTF-8, or returns null when it exceeds <see cref="MaxBodyBytes"/>.</summary>
    private static async Task<string?> ReadBodyAsync(HttpListenerRequest req)
    {
        if (req.ContentLength64 > MaxBodyBytes) return null;

        // Content-Length can be absent (chunked), so cap what is actually read too.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await req.InputStream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static bool Bool(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double? Num(JsonElement json, string name)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var d) ? d : null;
    }

    private static async Task Reply(HttpListenerContext ctx, int status, string body)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
    }
}
