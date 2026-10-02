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
/// </summary>
public sealed class HttpApiProvider(
    int port,
    Dispatcher dispatcher,
    Notifier notifier,
    ClaudeProvider claude,
    ClockTimerProvider timer) : IDisposable
{
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

            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var json = doc.RootElement;

            Action? action = path switch
            {
                "/notify" => await NotifyActionAsync(json),
                "/claude" => ClaudeAction(json),
                "/timer" => TimerAction(json),
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
