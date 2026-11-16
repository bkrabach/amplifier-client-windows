namespace Amplifier.Core;

public sealed class AvatarPlayback
{
    public AvatarState State { get; private set; } = AvatarState.Offline;
    private double started;
    private double? workingStarted;
    private readonly Queue<string> seen = new();
    public void Update(bool connected, bool connecting, int working, IReadOnlyList<NoticeRow> notices, double now)
    {
        workingStarted = connected && working > 0 ? workingStarted ?? now : null;
        var next = !connected ? connecting ? AvatarState.Connecting : AvatarState.Offline :
            notices.Any(x => x.State == AvatarState.Error) ? AvatarState.Error :
            notices.Any(x => x.State == AvatarState.Review) ? AvatarState.Review :
            notices.Any(x => x.State == AvatarState.Ready) ? AvatarState.Ready :
            working > 0 ? AvatarState.Working : AvatarState.Idle;
        var fresh = notices.Where(x => x.State == next && next is AvatarState.Ready or AvatarState.Error)
            .Select(x => x.Key + ":" + x.Fingerprint).Where(x => !seen.Contains(x)).ToArray();
        if (next != State || fresh.Length != 0)
            started = now - ((next is AvatarState.Ready or AvatarState.Error) && fresh.Length == 0 ? 4 : 0);
        foreach (var key in fresh) seen.Enqueue(key);
        while (seen.Count > 256) seen.Dequeue();
        State = next;
    }
    public (AvatarState State, double Elapsed) Presentation(double now)
    {
        var elapsed = Math.Max(0, now - started);
        if (workingStarted is double work && (State == AvatarState.Working || (State is AvatarState.Ready or AvatarState.Error) && elapsed >= 4))
            return (AvatarState.Working, Math.Max(0, now - work));
        return (State, elapsed);
    }
}

/// Choreography ported from the approved Mac avatar family; identical timing for every finish/palette.
public sealed class AvatarPose
{
    public double[] Weights { get; }
    public double Tilt { get; private set; }
    public double Rise { get; private set; }
    public double ScaleX { get; private set; } = 1;
    public double ScaleY { get; private set; } = 1;
    public static int Count(string shape) => shape == "relay" ? 6 : shape is "accord" or "clippy" ? 8 : 16;
    public static int Quiet(string shape) => shape switch { "relay" => 1, "accord" => 7, "meter" => 13, "clippy" => 4, _ => 15 };
    private AvatarPose(int count) => Weights = new double[count];
    public static AvatarPose Sample(string shape, AvatarState state, double elapsed, bool reduced)
    {
        var pose = new AvatarPose(Count(shape));
        var quiet = Quiet(shape);
        int peak = shape switch { "relay" or "clippy" => 0, "accord" or "meter" => 3, _ => 7 };
        int[] path = shape switch { "accord" => [1, 3, 5], "d20" => [4, 7, 9], "crest" => [3, 7, 9], "meter" => [0, 1, 3], _ => [0, 2, 4] };
        static double Ease(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
        static double Bump(double t, double start, double length) { var u = (t - start) / length; return u > 0 && u < 1 ? Math.Pow(Math.Sin(Math.PI * u), 2) : 0; }
        var period = state == AvatarState.Connecting ? 3.6 : 6;
        var loops = state is AvatarState.Idle or AvatarState.Working or AvatarState.Review or AvatarState.Connecting;
        var t = reduced ? state is AvatarState.Ready or AvatarState.Error ? 4 : 0 : loops ? elapsed % period : Math.Min(4, elapsed);
        (int Index, double Weight)[] selected = [(peak, 1)];double amp = 0;
        switch (state)
        {
            case AvatarState.Idle:
                var breath = (1 - Math.Cos(2 * Math.PI * t / 6)) / 2;
                pose.ScaleX += .003 * breath;pose.ScaleY += .006 * breath;
                if (shape == "clippy") { selected = [(5, 1)];amp = Bump(t, 3.8, .34); }
                break;
            case AvatarState.Ready:
                var progress = Math.Min(2, t / 1.35 * 2);var i = Math.Min(2, (int)progress);var f = Ease(progress - i);
                selected = [(path[i], 1 - f), (path[Math.Min(2, i + 1)], f)];
                amp = .95 * Bump(t, 0, 1.5) + .22 * Ease((t - 1.2) / .5);
                var g = Bump(t, .15, 1.1);
                pose.Tilt = (shape switch { "relay" => -1.4, "d20" => 3, "crest" => -1.8, "clippy" => 2, _ => 0 }) * g;
                pose.Rise = 2 * g;pose.ScaleY += (shape == "meter" ? .013 : .005) * g;
                if (shape == "clippy" && t > .55 && t < .9) { selected = [(5, 1)];amp = Bump(t, .55, .35); }
                break;
            case AvatarState.Review:
                amp = .13 + .75 * Bump(t, .35, .7) + .65 * Bump(t, 1.35, .7);
                pose.Tilt = (shape switch { "relay" => -.6, "d20" => 1, "crest" => -.8, "clippy" => 1.4, _ => 0 }) * Bump(t, .2, 2.2);
                break;
            case AvatarState.Error:
                amp = .45 * Bump(t, 0, .5);var recoil = Bump(t, .05, .7);
                pose.Tilt = (shape switch { "relay" or "crest" => 2, "accord" => 1, "d20" => -4, "meter" => .6, _ => -2 }) * recoil;
                pose.ScaleY -= .014 * recoil;pose.Rise = -1.5 * recoil;
                break;
            case AvatarState.Connecting:
                var unit = t / 1.2;selected = [(path[(int)unit % 3], 1)];amp = .55 * Math.Pow(Math.Sin(Math.PI * (unit - Math.Floor(unit))), 2);
                if (shape == "clippy") pose.Tilt = Math.Sin(2 * Math.PI * t / 3.6) * .8;
                break;
            case AvatarState.Working when !reduced && shape is "relay" or "meter":
                var phase = elapsed % (shape == "relay" ? .84 : 7.2) / (shape == "relay" ? .84 : 7.2) * pose.Weights.Length;
                var index = (int)phase;var fraction = phase - index;var blend = Ease(shape == "relay" ? fraction : (fraction - .8) / .2);
                selected = [(index, 1 - blend), ((index + 1) % pose.Weights.Length, blend)];amp = 1;
                if (shape == "meter") { var b = (1 - Math.Cos(2 * Math.PI * elapsed / 3.6)) / 2;pose.ScaleX += .014 * b;pose.ScaleY += .024 * b; }
                break;
        }
        amp = Math.Clamp(amp, 0, 1);pose.Weights[quiet] += 1 - amp;
        foreach (var (index, weight) in selected) pose.Weights[index] += amp * weight;
        return pose;
    }
}
