using System.Net;
using System.Text.RegularExpressions;

namespace Amplifier.Core;

public enum NoticeVisibility { Visible, Hidden, Hover }
public enum AvatarState { Idle, Working, Ready, Review, Error, Connecting, Offline }
public sealed record NoticeRow(string Key, string RegistrationId, string HostIdentity, string SessionId,
    string Title, string Detail, string Fingerprint, AvatarState State, string ServiceName)
{
    public int Priority => State switch { AvatarState.Error => 0, AvatarState.Review => 1, AvatarState.Ready => 2, _ => 3 };
}

public static partial class Presentation
{
    public static readonly string[] Shapes = ["relay", "accord", "d20", "crest", "meter", "clippy"];
    public static readonly string[] Palettes = ["blue", "cyan", "amber", "crimson", "green", "purple", "cyan-amber", "purple-blue"];
    public static string Plain(string text)
    {
        var result = Links().Replace(text, "$1");
        result = Markup().Replace(result, "");
        return Space().Replace(WebUtility.HtmlDecode(result), " ").Trim();
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
            if (notice.AgentCreated == true && session?.Pinned != true) continue;
            var state = notice.Kind == "session-error" ? AvatarState.Error : notice.RequiresAction ? AvatarState.Review : AvatarState.Ready;
            result.Add(new(service.Id + ":" + observation.HostIdentity + ":" + notice.Id, service.Id,
                observation.HostIdentity, notice.Target.SessionId, Plain(notice.Title), Plain(notice.Summary),
                notice.Fingerprint, state, service.Label));
        }
        if (includeWorking)
            foreach (var session in observation.Sessions.Where(s => (s.Execution || s.Preparing) && (s.AgentCreated != true || s.Pinned)))
                result.Add(new(service.Id + ":" + observation.HostIdentity + ":work:" + session.Id,
                    service.Id, observation.HostIdentity, session.Id, Plain(session.Title),
                    session.Preparing && !session.Execution ? "Preparing…" : "Working…",
                    session.Status + ":" + session.Execution + ":" + session.Preparing, AvatarState.Working, service.Label));
        return result;
    }

    public static IReadOnlyList<NoticeRow> Sort(IEnumerable<NoticeRow> rows, IReadOnlyList<string> serviceOrder, bool grouped)
    {
        var order = serviceOrder.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        return (grouped ? rows.OrderBy(x => order.GetValueOrDefault(x.RegistrationId, int.MaxValue)).ThenBy(x => x.Priority)
            : rows.OrderBy(x => x.Priority).ThenBy(x => order.GetValueOrDefault(x.RegistrationId, int.MaxValue)))
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
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
