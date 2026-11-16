using System.Text.Json;
using System.Text.RegularExpressions;

namespace Amplifier.Core;

/// Optional bounded display enrichment. Host observations alone decide membership and priority.
public sealed class NoticeDetails
{
    private readonly Dictionary<string, string> previews = new();
    private readonly Dictionary<string, string> pending = new();
    private readonly HashSet<string> attempted = new();
    private readonly Dictionary<string, (string Text, string State)> work = new();
    private int nextWork;
    private static string Key(Notice n) => n.Id + ":" + n.Fingerprint;
    public void Reconcile(Observation observation)
    {
        var current = observation.Notices.Where(n => n.Kind == "completion" && !observation.Sessions.Any(s => s.Id == n.Target.SessionId && (s.Execution || s.Preparing)))
            .Select(Key).ToHashSet();
        foreach (var key in previews.Keys.Where(k => !current.Contains(k)).ToArray()) previews.Remove(key);
        attempted.IntersectWith(current);
        foreach (var (key, text) in pending) if (current.Contains(key)) previews[key] = text;
        pending.Clear();
        var active = observation.Sessions.Where(s => s.Execution || s.Preparing).Select(s => s.Id).ToHashSet();
        foreach (var key in work.Keys.Where(k => !active.Contains(k)).ToArray()) work.Remove(key);
    }
    public NoticeRow Enrich(NoticeRow row, Observation observation)
    {
        if (row.State == AvatarState.Working && work.TryGetValue(row.SessionId, out var action))
            return row with { Detail = (row.Attention ? "Unread · " : "") + action.Text, Fingerprint = row.Attention || row.Dismissal != null ? row.Fingerprint : row.Fingerprint + action.State };
        var notice = observation.Notices.FirstOrDefault(n => n.Target.SessionId == row.SessionId && row.Key.EndsWith(":" + n.Id, StringComparison.Ordinal));
        return notice != null && previews.TryGetValue(Key(notice), out var text) ? row with { Detail = text } : row;
    }
    public async Task FetchAsync(HostTransport transport, Observation observation, IReadOnlyList<NoticeRow> visible, CancellationToken cancellation)
    {
        // At most two detail requests per service/poll, with an eight-second shared deadline.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var ids = visible.Select(x => x.SessionId).ToHashSet();
        var active = observation.Sessions.Where(s => ids.Contains(s.Id) && (s.Execution || s.Preparing)).ToArray();
        var budget = 2;
        if (active.Length > 0)
        {
            var session = active[nextWork++ % active.Length];budget--;
            var nodes = await ReadAsync(transport, session.Id, "nodes", deadline.Token);
            var label = Work(nodes);
            if (label != null) work[session.Id] = label.Value;else work.Remove(session.Id);
        }
        foreach (var notice in observation.Notices.Where(n => n.Kind == "completion" && ids.Contains(n.Target.SessionId) && !active.Any(s => s.Id == n.Target.SessionId)))
        {
            if (budget <= 0) break;
            var key = Key(notice);if (attempted.Contains(key)) continue;
            budget--;
            var messages = await ReadAsync(transport, notice.Target.SessionId, "messages", deadline.Token);
            attempted.Add(key);
            var text = Response(messages);
            // A subsequent complete observation must still contain this exact fingerprint,
            // with no active turn, before the candidate text becomes visible.
            if (text != null) pending[key] = text;
        }
    }
    private static async Task<JsonElement> ReadAsync(HostTransport transport, string session, string part, CancellationToken cancellation)
    {
        var response = await transport.RequestAsync("/api/conversation/detail?sessionId=" + Uri.EscapeDataString(session) + "&part=" + part, null, cancellation);
        var items = response.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 2000) throw new InvalidDataException("Invalid detail collection.");
        return items;
    }
    private static string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? Presentation.Plain(value.GetString()!) : "";
    public static string? Response(JsonElement messages)
    {
        foreach (var row in messages.EnumerateArray().Reverse())
        {
            if (Text(row,"role") == "user") return null;
            var text = Text(row,"text");
            if (Text(row,"role") == "assistant" && text.Length > 0) return text;
        }
        return null;
    }
    public static (string Text, string State)? Work(JsonElement nodes)
    {
        string[] live = ["running","working","streaming","pending","started","starting","queued","waiting","retrying"];
        var active = nodes.EnumerateArray().Where(n => (!n.TryGetProperty("endedAt",out var end) || end.ValueKind == JsonValueKind.Null) && live.Contains(Text(n,"phase") is { Length: >0 } phase ? phase : Text(n,"status"))).ToArray();
        var tools = active.Where(n => Text(n,"kind") == "tool").ToArray();
        if (active.Length == 0) return null;
        var row = tools.LastOrDefault();if (row.ValueKind == JsonValueKind.Undefined) row = active.Last();
        var kind = Text(row,"kind");var label = Text(row,"label");var tool = Text(row,"tool");if (tool.Length == 0) tool = label;
        var summary = Text(row,"summary");string result;
        string Words(string value) => Regex.Replace(value, "[_.:/-]+", " ");
        bool Identifier(string value) => Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_.:/-]{0,119}$");
        var name = tool.Split(':').Last();
        var hints = row.TryGetProperty("inputSummary",out var h) ? h : default;
        var operation = Text(hints,"action");if (!Identifier(operation)) operation = Text(hints,"operation");
        if (new[]{"llm","model","model_call","provider"}.Contains(kind)) result = "Thinking…";
        else if (summary.Length > 0 && !summary.Equals(label,StringComparison.OrdinalIgnoreCase) && summary != "Running " + tool && summary != "Completed " + tool)
            result = label.Length == 0 || label == tool || summary.StartsWith(label,StringComparison.OrdinalIgnoreCase) ? summary : label + " · " + summary;
        else if (label.Length > 0 && label != tool) result = label;
        else if (name == "app_control")
        {
            var path = Text(row,"actionPath");
            result = path.StartsWith("dispatch → ") && Identifier(path[11..]) ? "App · " + Words(path[11..]) : Identifier(operation) ? "App · " + Words(operation) : "Using app controls…";
        }
        else if (Identifier(operation)) result = Words(tool) + " · " + Words(operation);
        else result = name switch {
            "bash" or "shell" or "exec_command" => "Running a command…", "apply_patch" => "Editing files…",
            "grep" => "Searching files…", "glob" => "Finding files…", "web_search" => "Searching the web…",
            "web_fetch" => "Fetching a page…", "delegate" => "Delegating work…", "image_generate" => "Generating an image…",
            "model call" or "working" or "" => "Thinking…", _ => "Using " + Words(tool) + "…" };
        if (tools.Length > 1) result += " · +" + (tools.Length-1) + " tool" + (tools.Length>2 ? "s" : "");
        return (result, Text(row,"id") + "|" + kind + "|" + Text(row,"phase"));
    }
}
