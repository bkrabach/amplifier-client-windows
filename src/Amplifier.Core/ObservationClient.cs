using System.Text.Json;

namespace Amplifier.Core;

public sealed class ObservationClient(HostTransport transport)
{
    private string? hostIdentity, instanceId;
    private bool compatibilityFeed;
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
        foreach (var name in new[] { "sessions", "attention", "activity" })
        {
            var resource = observations.RequiredText(name);
            _ = transport.Endpoint.Resource(resource);
            // Restrict even same-origin advertisements to the public observation routes.
            var expected = name == "sessions" ? "/api/sessions?view=observation" : "/api/" + name;
            if (resource != expected) throw new InvalidDataException("Unsupported observation resource.");
            resources[name] = resource;
        }
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
                    if (compatibilityFeed) return await CompatibilityAsync(budget.Token);
                    IReadOnlyList<Session> sessions;
                    try { sessions = await CollectionAsync<Session>("sessions", budget.Token); }
                    catch (HostException error) when (error.Status == 403 && transport.RestrictedCredential)
                    {
                        // Shipped companion-v1 scopes can advertise generic observations but deny
                        // this route. Only this exact denial admits the versioned, read-only adapter.
                        var compatible = await CompatibilityAsync(budget.Token);
                        compatibilityFeed = true;
                        return compatible;
                    }
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

    private string PagePath(string name, string? cursor)
    {
        var path = resources[name];
        path += path.Contains('?') ? "&scope=roots" : "?scope=roots";
        if (name != "activity") path += "&limit=200";
        if (name == "sessions") path += "&activity=active-or-pinned";
        if (cursor != null) path += "&cursor=" + Uri.EscapeDataString(cursor);
        return path;
    }

    private async Task<IReadOnlyList<T>> CollectionAsync<T>(string name, CancellationToken cancellation)
    {
        var result = new List<T>();
        var seen = new HashSet<string>();
        string? cursor = null, revision = null;
        for (var page = 0; page < 256; page++)
        {
            var value = await transport.RequestAsync(PagePath(name, cursor), null, cancellation);
            var current = value.GetProperty("revision").GetRawText();
            if (revision != null && revision != current) throw new InvalidDataException("Incoherent observation pages.");
            revision = current;
            var items = value.GetProperty("items");
            if (items.GetArrayLength() > 200) throw new InvalidDataException("Oversized observation page.");
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
                result.Add(item.Deserialize<T>(Json.Options) ?? throw new InvalidDataException("Invalid observation item."));
            }
        return result;
    }

    private async Task<JsonElement> CompatibilityPageAsync(string path, CancellationToken cancellation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await transport.RequestAsync(path, null, cancellation); }
            catch (HostException e) when (e.Status == 503 && attempt < 2)
            {
                // Finish the immutable count traversal before retrying this read only.
                await ActivityAsync(cancellation);
            }
        }
    }

    private async Task<Observation> CompatibilityAsync(CancellationToken cancellation)
    {
        var sessions = new List<Session>();var notices = new List<Notice>();
        var sessionIds = new HashSet<string>();var noticeIds = new HashSet<string>();
        var seenSessions = new HashSet<string>();var seenItems = new HashSet<string>();
        string? sessionCursor = null, itemCursor = null, retainedSessionCursor = null, retainedItemCursor = null;
        bool sessionDone = false, itemsDone = false;
        for (var page = 0; page < 256; page++)
        {
            var query = new List<string>();
            if (retainedSessionCursor != null) query.Add("sessionsCursor=" + Uri.EscapeDataString(retainedSessionCursor));
            if (retainedItemCursor != null) query.Add("itemsCursor=" + Uri.EscapeDataString(retainedItemCursor));
            var path = "/api/companion" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            var value = await CompatibilityPageAsync(path, cancellation);
            if (value.GetProperty("protocolVersion").GetInt32() != 1 ||
                value.RequiredText("hostIdentity") != hostIdentity || value.RequiredText("hostInstanceId") != instanceId)
                throw new InvalidDataException("Companion compatibility identity or version changed.");
            if (!sessionDone)
            {
                var array = value.GetProperty("sessions");
                if (array.GetArrayLength() > 50) throw new InvalidDataException("Oversized compatibility session page.");
                foreach (var session in ParseItems<Session>(array))
                {
                    if (!sessionIds.Add(session.Id)) throw new InvalidDataException("Duplicate session in compatibility traversal.");
                    sessions.Add(session);
                }
                sessionCursor = value.GetProperty("sessionsNextCursor").GetString();
                if (value.GetProperty("sessionsTruncated").GetBoolean() != (sessionCursor != null)) throw new InvalidDataException("Incomplete session continuation.");
                sessionDone = sessionCursor == null;
                if (!sessionDone)
                {
                    if (sessionCursor!.Length is 0 or > 8192 || !seenSessions.Add(sessionCursor)) throw new InvalidDataException("Invalid compatibility session cursor.");
                    retainedSessionCursor = sessionCursor;
                }
            }
            if (!itemsDone)
            {
                var array = value.GetProperty("items");
                if (array.GetArrayLength() > 200) throw new InvalidDataException("Oversized compatibility notice page.");
                foreach (var notice in ParseItems<Notice>(array))
                {
                    if (!noticeIds.Add(notice.Id)) throw new InvalidDataException("Duplicate notice in compatibility traversal.");
                    notices.Add(notice with { Kind = notice.Kind switch { "session" => "session-error", "unread" => "manual-unread", _ => notice.Kind } });
                }
                itemCursor = value.GetProperty("itemsNextCursor").GetString();
                if (value.GetProperty("itemsTruncated").GetBoolean() != (itemCursor != null)) throw new InvalidDataException("Incomplete notice continuation.");
                itemsDone = itemCursor == null;
                if (!itemsDone)
                {
                    if (itemCursor!.Length is 0 or > 8192 || !seenItems.Add(itemCursor)) throw new InvalidDataException("Invalid compatibility notice cursor.");
                    retainedItemCursor = itemCursor;
                }
            }
            if (sessionDone && itemsDone)
                return new(hostIdentity!, instanceId!, sessions, notices, await ActivityAsync(cancellation));
        }
        throw new InvalidDataException("Compatibility observation exceeded its traversal limit.");
    }

    private async Task<Activity> ActivityAsync(CancellationToken cancellation)
    {
        var seen = new HashSet<string>();
        string? cursor = null, revision = null;
        for (var page = 0; page < 256; page++)
        {
            var value = await transport.RequestAsync(PagePath("activity", cursor), null, cancellation);
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
