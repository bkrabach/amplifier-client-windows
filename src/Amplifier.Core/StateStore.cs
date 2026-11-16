using System.Text.Json;

namespace Amplifier.Core;

public sealed class Preferences
{
    public int Version { get; set; } = 1;
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
    public List<Registration> Services { get; set; } = [];
    public string? PreferredId { get; set; }
    public string Shape { get; set; } = "relay";
    public string Finish { get; set; } = "clear";
    public string Palette { get; set; } = "blue";
    public NoticeVisibility Visibility { get; set; } = NoticeVisibility.Visible;
    public NoticeSize NoticeSize { get; set; } = NoticeSize.Compact;
    public bool HideTempo { get; set; }
    public bool Expanded { get; set; }
    public bool IncludeWorking { get; set; } = true;
    public bool GroupByService { get; set; }
    public bool ServiceHeadings { get; set; } = true;
    public bool DesktopNotifications { get; set; }
    public bool FitAll { get; set; }
    public double MaximumHeight { get; set; } = 360;
    public double? AvatarLeft { get; set; }
    public double? AvatarTop { get; set; }
    public Dictionary<string, string> Dismissed { get; set; } = [];
}

public static class AtomicFile
{
    public static T Read<T>(string path, Func<T> absent)
        => !File.Exists(path) ? absent() : JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json.Options)
            ?? throw new InvalidDataException("Saved state is invalid; the original file has been preserved.");
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Json.Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record PendingReply(string Id, string RegistrationId, string Endpoint, string HostIdentity,
    string SessionId, string Text, string Status = "saved");

/// No constructor, load or reconnect path submits anything. Retry is an explicit action.
public sealed class Outbox(string path)
{
    private readonly object gate = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    public IReadOnlyList<PendingReply> Entries { get { lock (gate) return AtomicFile.Read(path, () => new List<PendingReply>()); } }
    public PendingReply Save(Registration service, string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || service.HostIdentity == null) throw new InvalidOperationException("Choose a connected chat and enter a message.");
        var reply = new PendingReply(Guid.NewGuid().ToString("N"), service.Id, new Endpoint(service.Address).ToString(), service.HostIdentity, sessionId, text);
        lock (gate) { var entries = Entries.ToList();entries.Add(reply);AtomicFile.Write(path, entries); }
        return reply;
    }
    public async Task SendAsync(PendingReply reply, Registration registration, HostTransport transport, CancellationToken cancellation)
    {
        await sending.WaitAsync(cancellation);
        try
        {
        var persisted = Entries.SingleOrDefault(x => x.Id == reply.Id);
        if (persisted == null || persisted with { Status = reply.Status } != reply)
            throw new InvalidOperationException("The exact saved message was not found. Nothing was sent.");
        if (persisted.Status == "accepted") return;
        if (registration.Paused || registration.Id != reply.RegistrationId ||
            registration.HostIdentity != reply.HostIdentity || transport.Endpoint.ToString() != reply.Endpoint)
            throw new InvalidOperationException("The saved message belongs to a different or unavailable destination.");
        // Recheck authority immediately before sending; no re-targeting based on current UI selection.
        await new ObservationClient(transport).AttachAsync(reply.HostIdentity, cancellation);
        Update(reply.Id, "unknown");
        var result = await transport.RequestAsync("/api/sessions/" + Uri.EscapeDataString(reply.SessionId) + "/commands",
            new { id = reply.Id, action = "conversation.send", args = new { text = reply.Text, via = "chat", preserveDraft = true, attachmentIds = Array.Empty<string>() } }, cancellation);
        if (!result.TryGetProperty("accepted", out var accepted) || !accepted.GetBoolean())
            throw new InvalidOperationException("Delivery is unconfirmed. The saved message was retained.");
        Update(reply.Id, "accepted");
        }
        finally { sending.Release(); }
    }
    private void Update(string id, string status)
        { lock (gate) AtomicFile.Write(path, Entries.Select(x => x.Id == id ? x with { Status = status } : x).ToList()); }
}
