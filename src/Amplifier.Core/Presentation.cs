using System.Net;
using System.Text.RegularExpressions;

namespace Amplifier.Core;

public enum NoticeVisibility { Visible, Hidden, Hover }
public enum NoticeSize { Compact, Comfortable, Large }
public static class NoticeSizes
{
    public static double Scale(NoticeSize size) => size switch { NoticeSize.Compact => .82, NoticeSize.Comfortable => .91, _ => 1 };
    public static string Label(NoticeVisibility mode) => mode switch { NoticeVisibility.Visible => "Always visible", NoticeVisibility.Hover => "On hover", _ => "Hidden" };
}
public enum AvatarState { Idle, Working, Ready, Review, Error, Connecting, Offline }
public sealed record NoticeRow(string Key, string RegistrationId, string HostIdentity, string SessionId,
    string Title, string Detail, string Fingerprint, AvatarState State, string ServiceName)
{
    // Action-required items share one tier, preserving the host's order within it.
    public int Priority => State switch { AvatarState.Error or AvatarState.Review => 0, _ => 1 };
    public bool Attention { get; init; } = true;
    public DismissalDescriptor? Dismissal { get; init; }
    public ChatStatus? ChatStatus { get; init; }
    public IReadOnlyList<DismissalDescriptor> VisibleStates { get; init; } = [];
    public bool UnreadResponse { get; init; }
    public int SortPriority => Attention ? Priority : 2;
}

public static partial class Presentation
{
    public static readonly string[] Shapes = ["relay", "accord", "d20", "crest", "meter", "clippy"];
    public static readonly string[] Palettes = ["blue", "cyan", "amber", "crimson", "green", "purple", "cyan-amber", "purple-blue"];
    public static string Plain(string text)
    {
        var result = Links().Replace(text[..Math.Min(text.Length, 8192)], "$1");
        result = Markup().Replace(result, "");
        result = Space().Replace(WebUtility.HtmlDecode(result), " ").Trim();
        return result.Length > 600 ? result[..599] + "…" : result;
    }
    [GeneratedRegex(@"!?\[([^\]]+)\]\([^)]*\)")] private static partial Regex Links();
    [GeneratedRegex(@"(\*\*|__|`{1,3}|(?m:^\s{0,3}#{1,6}\s+))")] private static partial Regex Markup();
    [GeneratedRegex(@"\s+")] private static partial Regex Space();

    public static IReadOnlyList<NoticeRow> Rows(Registration service, Observation observation, bool includeWorking)
    {
        var result = new List<NoticeRow>();
        var sessions = observation.Sessions.ToDictionary(x => x.Id);
        foreach (var notice in observation.Notices.Where(n => !n.Read && n.Target.Type == "session"))
        {
            sessions.TryGetValue(notice.Target.SessionId, out var session);
            if (notice.AgentCreated == true && !notice.Pinned && session?.Pinned != true) continue;
            var state = notice.Kind == "session-error" ? AvatarState.Error : notice.RequiresAction || notice.Kind is not ("completion" or "manual-unread") ? AvatarState.Review :
                session is { } s && (s.Execution || s.Preparing) ? AvatarState.Working : AvatarState.Ready;
            result.Add(new(service.Id + ":" + observation.HostIdentity + ":" + notice.Id, service.Id,
                observation.HostIdentity, notice.Target.SessionId, Plain(notice.Title), state == AvatarState.Working ? "Unread · Thinking…" : Plain(notice.Summary),
                notice.Fingerprint, state, service.Label) { Dismissal = notice.Dismissal, ChatStatus = notice.ChatStatus ?? session?.ChatStatus, UnreadResponse = notice.Kind is "completion" or "manual-unread" });
        }
        if (includeWorking)
            foreach (var session in observation.Sessions.Where(s => (s.Execution || s.Preparing) && (s.AgentCreated != true || s.Pinned)))
                result.Add(new(service.Id + ":" + observation.HostIdentity + ":work:" + session.Id,
                    service.Id, observation.HostIdentity, session.Id, Plain(session.Title),
                    Plain(session.Display?.Summary ?? "Thinking…"),
                    session.Dismissal?.Fingerprint ?? "work|" + session.Status + "|", AvatarState.Working, service.Label) { Attention = false, Dismissal = session.Dismissal, ChatStatus = session.ChatStatus });
        return result;
    }

    public static IEnumerable<NoticeRow> ChatRows(IEnumerable<NoticeRow> visible)
    {
        foreach (var group in visible.GroupBy(row => (row.RegistrationId, row.HostIdentity, row.SessionId)))
        {
            var primary = group.OrderBy(row => row.SortPriority).First();
            var status = primary.ChatStatus;
            var state = status?.Kind switch
            {
                "working" => AvatarState.Working,
                "unread" => AvatarState.Ready,
                "attention" when status.Label is "Approval requested" or "Answer requested" or "Dependency decision requested" => AvatarState.Review,
                "attention" => AvatarState.Error,
                "waiting" or "paused" or "idle" => AvatarState.Idle,
                _ => primary.State
            };
            var detail = primary.Detail;
            var unread = group.Any(row => row.UnreadResponse);
            if (status?.Kind == "working")
            {
                var work = group.FirstOrDefault(row => !row.Attention);
                if (work != null || primary.State != AvatarState.Working)
                    detail = (unread ? "Unread · " : "") + (work?.Detail ?? "Thinking…");
            }
            if (status != null && status.Kind is not ("working" or "unread"))
                detail = status.Label + (unread ? " · Unread response" : "");
            yield return primary with {
                State = state, Detail = detail,
                VisibleStates = group.Select(row => row.Dismissal).OfType<DismissalDescriptor>().Distinct().ToArray()
            };
        }
    }

    public static bool Dismissed(NoticeRow row, IDictionary<string,string> saved)
    {
        if (!saved.TryGetValue(row.Key,out var previous)) return false;
        if (previous == row.Fingerprint) return true;
        var old = previous.Split('|',3);var current = row.Fingerprint.Split('|',3);
        if (old.Length == 3 && current.Length == 3 && old[0] == "work" && current[0] == "work" && old[1] == current[1] && (old[2].Length == 0 || current[2].Length == 0))
        {
            if(current[2].Length > 0) saved[row.Key] = row.Fingerprint;
            return true;
        }
        return false;
    }

    public static IReadOnlyList<NoticeRow> Sort(IEnumerable<NoticeRow> rows, IReadOnlyList<string> serviceOrder, bool grouped)
    {
        var order = serviceOrder.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return (grouped ? rows.OrderBy(x => order.GetValueOrDefault(x.RegistrationId, int.MaxValue)).ThenBy(x => x.SortPriority)
            : rows.OrderBy(x => x.SortPriority)).ToArray();
    }
}

public sealed class HoverPresentation
{
    public NoticeVisibility Mode { get; set; } = NoticeVisibility.Visible;
    private bool hovered, suppressed, explicitReveal;
    private TimeSpan? leftAt;
    public bool Visible => Mode == NoticeVisibility.Visible || (Mode == NoticeVisibility.Hover && (explicitReveal || hovered && !suppressed));
    public void Update(bool inside, bool dragging, TimeSpan now)
    {
        if (Mode != NoticeVisibility.Hover) { hovered = false; suppressed = false; explicitReveal = false; leftAt = null; return; }
        if (inside || dragging && hovered)
        {
            if (!hovered) { suppressed = false; explicitReveal = false; }
            hovered = true; leftAt = null;
        }
        else if (hovered)
        {
            leftAt ??= now;
            if (now - leftAt >= TimeSpan.FromMilliseconds(650)) { hovered = false; suppressed = false; explicitReveal = false; }
        }
    }
    public void Toggle()
    {
        if (Mode == NoticeVisibility.Hover) { if (Visible) Dismiss(); else { explicitReveal = true; suppressed = false; } }
        else Mode = Mode == NoticeVisibility.Visible ? NoticeVisibility.Hidden : NoticeVisibility.Visible;
    }
    public void Dismiss()
    {
        if (Mode == NoticeVisibility.Hover) { explicitReveal = false; suppressed = hovered; }
        else Mode = NoticeVisibility.Hidden;
    }
}

public static class NoticeGeometry
{
    // All arguments use the same logical coordinate system. Never move the bottom anchor.
    public static double Height(double content, double preferredMaximum, bool fitAll, double anchorTop, double workAreaTop, double header = 30)
        => Math.Max(0, Math.Min(content, Math.Min(fitAll ? content : preferredMaximum, anchorTop - workAreaTop - header - 8)));
}
