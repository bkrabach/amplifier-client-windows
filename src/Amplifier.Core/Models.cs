using System.Text.Json;

namespace Amplifier.Core;

public sealed record Registration(string Id, string Address, string Name, string? HostIdentity = null, bool Paused = false)
{
    public string Label => string.IsNullOrWhiteSpace(Name) ? new Uri(Address).Authority : Name;
}

public sealed record DisplayHint(string Summary);
public sealed record SessionActivity(bool Execution, bool Preparing, bool Loading);
public sealed record Session(string Id, string Title, string Status, bool Pinned,
    bool? AgentCreated, SessionActivity Activity, DismissalDescriptor? Dismissal = null, DisplayHint? Display = null)
{
    public bool Execution => Activity.Execution;
    public bool Preparing => Activity.Preparing;
    public bool Loading => Activity.Loading;
}
public sealed record NoticeTarget(string Type, string SessionId);
public sealed record Notice(string Id, string Fingerprint, string Kind, NoticeTarget Target,
    bool RequiresAction, bool Read, string Title, string Summary, bool? AgentCreated, bool Pinned, DismissalDescriptor? Dismissal = null);
public sealed record Activity(int ExecutionCount, int PreparingCount, int LoadingCount,
    int AttentionCount, int RequiresActionCount);
public sealed record Observation(string HostIdentity, string HostInstanceId,
    IReadOnlyList<Session> Sessions, IReadOnlyList<Notice> Notices, Activity Activity);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string RequiredText(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(value.GetString())) throw new InvalidDataException("Missing " + name);
        return value.GetString()!;
    }
}

public sealed class HostException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public bool AuthenticationLost => Status is 401 or 403;
}
