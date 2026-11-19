using System.Text.Json;

namespace Amplifier.Core;

public sealed class ObservationClient(HostTransport transport)
{
    private bool supportsChatStatus;
    private string? hostIdentity, instanceId;
    private readonly Dictionary<string, string> resources = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string> AttachAsync(string? expectedHost, CancellationToken cancellation)
    {
        var result = await transport.RequestAsync("/api/clients/attach",
            new { clientId = transport.ClientId, kind = "native", protocolVersion = 1, includeState = false }, cancellation);
        if (result.GetProperty("protocolVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported client protocol.");
        var identity = result.RequiredText("hostIdentity");
        if (expectedHost != null && expectedHost != identity)
            throw new InvalidDataException("This address now identifies a different host. Reapprove explicitly.");
        if (!result.TryGetProperty("capabilities", out var capabilities) ||
            !capabilities.TryGetProperty("observations", out var observations) ||
            observations.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("This host does not advertise supported observations.");
        if (!observations.TryGetProperty("dismissals", out var dismissals) ||
            dismissals.GetProperty("version").GetInt32() != 1 || dismissals.RequiredText("action") != "presentation.dismiss" ||
            !observations.TryGetProperty("identity", out var identityFlag) || !identityFlag.GetBoolean())
            throw new InvalidDataException("Update this service to support shared updates across devices.");
        foreach (var name in new[] { "sessions", "attention", "activity" })
        {
            var resource = observations.RequiredText(name);
            _ = transport.Endpoint.Resource(resource);
            // Restrict even same-origin advertisements to the public observation routes.
            var expected = name == "sessions" ? "/api/sessions?view=observation" : "/api/" + name;
            if (resource != expected) throw new InvalidDataException("Unsupported observation resource.");
            resources[name] = resource;
        }
        supportsChatStatus = observations.TryGetProperty("chatStatus", out var status) && status.ValueKind == JsonValueKind.True;
        hostIdentity = identity;
        instanceId = result.RequiredText("hostInstanceId");
        return identity;
    }

    public async Task<Observation> PollAsync(CancellationToken cancellation)
    {
        if (hostIdentity == null || instanceId == null) throw new InvalidOperationException("Attach first.");
        await gate.WaitAsync(cancellation);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            budget.CancelAfter(TimeSpan.FromSeconds(45));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var sessions = await CollectionAsync<Session>("sessions", budget.Token);
                    var notices = await CollectionAsync<Notice>("attention", budget.Token);
                    var activity = await ActivityAsync(budget.Token);
                    return new(hostIdentity, instanceId, sessions, notices, activity);
                }
                catch (HostException e) when (e.Status is 400 or 409 && attempt < 2)
                { if (e.Status == 400) await AttachAsync(hostIdentity, budget.Token); }
            }
        }
        finally { gate.Release(); }
    }

    private void VerifyIdentity(JsonElement value)
    {
        if (value.RequiredText("hostIdentity") != hostIdentity) throw new InvalidDataException("The observed host changed. Reapprove explicitly.");
        if (value.RequiredText("hostInstanceId") != instanceId) throw new HostException(400, "The service restarted; rediscover observations.");
    }
    public async Task DismissAsync(DismissalIntent intent, CancellationToken cancellation)
    {
        if (intent.Endpoint != transport.Endpoint.ToString()) throw new InvalidDataException("Dismissal belongs to another service.");
        await AttachAsync(intent.HostIdentity, cancellation);
        var response = await transport.RequestAsync("/api/actions", new { id = intent.RequestId, action = "presentation.dismiss",
            args = new { items = new[] { new { id = intent.Id, fingerprint = intent.Fingerprint } } } }, cancellation);
        if (!response.TryGetProperty("accepted", out var accepted) || !accepted.GetBoolean())
            throw new InvalidDataException("Dismissal synchronization is unconfirmed.");
    }
    private string PagePath(string name, string? cursor)
    {
        var path = resources[name];
        path += path.Contains('?') ? "&scope=roots" : "?scope=roots";
        if (name != "activity") path += "&limit=200";
        if (name != "activity" && supportsChatStatus) path += "&chatStatus=1";
        if (name == "sessions") path += "&activity=active-or-pinned";
        if (cursor != null) path += "&cursor=" + Uri.EscapeDataString(cursor);
        return path;
    }

    private async Task<IReadOnlyList<T>> CollectionAsync<T>(string name, CancellationToken cancellation)
    {
        var result = new List<T>();
        var seen = new HashSet<string>();
        var ids = new HashSet<string>();
        string? cursor = null, revision = null;
        for (var page = 0; page < 256; page++)
        {
            var value = await transport.RequestAsync(PagePath(name, cursor), null, cancellation);
            VerifyIdentity(value);
            var current = value.GetProperty("revision").GetRawText();
            if (revision != null && revision != current) throw new InvalidDataException("Incoherent observation pages.");
            revision = current;
            var items = value.GetProperty("items");
            if (items.GetArrayLength() > 200) throw new InvalidDataException("Oversized observation page.");
            foreach (var item in items.EnumerateArray())
                if (!ids.Add(item.RequiredText("id"))) throw new InvalidDataException("Duplicate observation item.");
            result.AddRange(ParseItems<T>(items));
            cursor = value.GetProperty("nextCursor").GetString();
            if (cursor == null) return result;
            if (cursor.Length is 0 or > 8192 || !seen.Add(cursor)) throw new InvalidDataException("Invalid or repeated observation cursor.");
        }
        throw new InvalidDataException("Observation exceeded its traversal limit; no partial result published.");
    }

    private static IReadOnlyList<T> ParseItems<T>(JsonElement items)
    {
        var result = new List<T>();
            foreach (var item in items.EnumerateArray())
            {
                _ = item.RequiredText("id");
                if (typeof(T) == typeof(Session))
                {
                    _ = item.RequiredText("status");
                    _ = item.GetProperty("title").GetString() ?? throw new InvalidDataException("Missing title.");
                    _ = item.GetProperty("pinned").GetBoolean();
                    foreach (var flag in new[] { "execution", "preparing", "loading" })
                        _ = item.GetProperty("activity").GetProperty(flag).GetBoolean();
                }
                else
                {
                    _ = item.RequiredText("fingerprint");_ = item.RequiredText("kind");
                    _ = item.GetProperty("title").GetString() ?? throw new InvalidDataException("Missing title.");
                    _ = item.GetProperty("summary").GetString() ?? throw new InvalidDataException("Missing summary.");
                    _ = item.GetProperty("read").GetBoolean();_ = item.GetProperty("requiresAction").GetBoolean();
                    var target = item.GetProperty("target");
                    if (target.RequiredText("type") == "session") _ = target.RequiredText("sessionId");
                }
                if (item.TryGetProperty("dismissal", out var dismissal) && dismissal.ValueKind != JsonValueKind.Null)
                {
                    _ = dismissal.RequiredText("id"); _ = dismissal.RequiredText("fingerprint"); _ = dismissal.GetProperty("dismissed").GetBoolean();
                }
                else if (typeof(T) == typeof(Notice) || item.GetProperty("activity").GetProperty("execution").GetBoolean() || item.GetProperty("activity").GetProperty("preparing").GetBoolean())
                    throw new InvalidDataException("Missing shared update identity.");
                result.Add(item.Deserialize<T>(Json.Options) ?? throw new InvalidDataException("Invalid observation item."));
            }
        return result;
    }

    private async Task<Activity> ActivityAsync(CancellationToken cancellation)
    {
        var seen = new HashSet<string>();
        string? cursor = null, revision = null;
        for (var page = 0; page < 256; page++)
        {
            var value = await transport.RequestAsync(PagePath("activity", cursor), null, cancellation);
            VerifyIdentity(value);
            var current = value.GetProperty("revision").GetRawText();
            if (revision != null && revision != current) throw new InvalidDataException("Incoherent activity pages.");
            revision = current;
            if (value.RequiredText("status") == "ready" && value.GetProperty("nextCursor").ValueKind == JsonValueKind.Null)
            {
                foreach (var name in new[] { "executionCount", "preparingCount", "loadingCount", "attentionCount", "requiresActionCount" })
                    if (value.GetProperty(name).GetInt32() < 0) throw new InvalidDataException("Invalid activity count.");
                var activity = value.Deserialize<Activity>(Json.Options) ?? throw new InvalidDataException("Invalid activity.");
                if (new[] { activity.ExecutionCount, activity.PreparingCount, activity.LoadingCount, activity.AttentionCount, activity.RequiresActionCount }.Any(x => x < 0))
                    throw new InvalidDataException("Negative activity count.");
                return activity;
            }
            if (value.RequiredText("status") != "collecting") throw new InvalidDataException("Unsupported activity state.");
            cursor = value.RequiredText("nextCursor");
            if (cursor.Length > 8192 || !seen.Add(cursor)) throw new InvalidDataException("Invalid activity continuation.");
        }
        throw new InvalidDataException("Activity remains incomplete; no partial counts published.");
    }
}
