namespace Amplifier.Core;

/// Host-issued state identity. It is never derived from display text or polling time.
public sealed record DismissalDescriptor(string Id, string Fingerprint, bool Dismissed);

public sealed record DismissalIntent(string RequestId, string RegistrationId, string Endpoint,
    string HostIdentity, string Id, string Fingerprint, bool Accepted = false);

/// Durable exact-state presentation intent. Unlike a chat command, retrying this
/// idempotent receipt cannot repeat execution or suppress a newer fingerprint.
public sealed class SharedDismissals(string path)
{
    private readonly object gate = new();
    private List<DismissalIntent>? loaded;
    private void Persist(IEnumerable<DismissalIntent> values)
    {
        var next = values.ToList(); AtomicFile.Write(path, next); loaded = next;
    }
    public IReadOnlyList<DismissalIntent> Entries { get { lock (gate) return (loaded ??= AtomicFile.Read(path, () => new List<DismissalIntent>())).ToArray(); } }
    public DismissalIntent Save(Registration service, DismissalDescriptor state)
    {
        if (service.HostIdentity == null || string.IsNullOrEmpty(state.Id) || string.IsNullOrEmpty(state.Fingerprint))
            throw new InvalidOperationException("A current host-issued update is required.");
        lock (gate)
        {
            var entries = Entries.ToList();
            var existing = entries.FirstOrDefault(x => Matches(x, service) && x.Id == state.Id && x.Fingerprint == state.Fingerprint);
            if (existing != null) return existing;
            if (entries.Count >= 512) throw new InvalidOperationException("Dismissal synchronization is full. Reconnect services before hiding more updates.");
            var item = new DismissalIntent(Guid.NewGuid().ToString("N"), service.Id,
                new Endpoint(service.Address).ToString(), service.HostIdentity, state.Id, state.Fingerprint);
            entries.Add(item); Persist(entries); return item;
        }
    }
    public static bool Matches(DismissalIntent item, Registration service) => item.RegistrationId == service.Id &&
        item.Endpoint == new Endpoint(service.Address).ToString() && item.HostIdentity == service.HostIdentity;
    public bool Hidden(Registration service, DismissalDescriptor state) => state.Dismissed ||
        Entries.Any(x => Matches(x, service) && x.Id == state.Id && x.Fingerprint == state.Fingerprint);
    public void Accepted(string requestId)
    {
        lock (gate) Persist(Entries.Select(x => x.RequestId == requestId ? x with { Accepted = true } : x));
    }
    /// Only a complete fresh observation may retire intent. An offline/partial
    /// result must not be passed here. Other services and their pending work stay intact.
    public void Reconcile(Registration service, IReadOnlyList<DismissalDescriptor> current)
    {
        lock (gate)
        {
            var states = current.ToDictionary(x => (x.Id, x.Fingerprint));
            var entries = Entries;
            var retained = entries.Where(x => !Matches(x, service) ||
                states.TryGetValue((x.Id, x.Fingerprint), out var state) && !state.Dismissed).ToArray();
            if (retained.Length != entries.Count) Persist(retained);
        }
    }
}
