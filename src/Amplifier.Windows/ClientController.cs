using Amplifier.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Amplifier.Windows;

internal sealed class ServiceRuntime(Registration registration, HostTransport transport) : IDisposable
{
    public Registration Registration = registration;
    public HostTransport Transport { get; } = transport;
    public CancellationTokenSource Cancellation { get; } = new();
    public Observation? Observation;
    public string Status = "Connecting…";
    public bool Sending;
    public void Dispose() { Cancellation.Cancel();Transport.Dispose();Cancellation.Dispose(); }
}

internal sealed class ClientController : IDisposable
{
    public bool Preview { get; }
    public Preferences Preferences { get; }
    public Outbox Outbox { get; }
    private readonly string preferencePath;
    private readonly CredentialStore credentials;
    private readonly Dictionary<string, ServiceRuntime> services = new();
    private readonly Forms.NotifyIcon tray;
    private readonly TempoWindow tempo;
    private SettingsWindow? settings;
    private bool disposed;
    public event Action? Changed;
    public IReadOnlyList<NoticeRow> Rows { get; private set; } = [];
    public IReadOnlyList<NoticeRow> AllRows { get; private set; } = [];
    public IReadOnlyList<ServiceRuntime> Services => Preferences.Services.Select(x => services.GetValueOrDefault(x.Id)).Where(x => x != null).Cast<ServiceRuntime>().ToArray();
    public int Attention => AllRows.Count(x => x.State != AvatarState.Working);
    public int Working => Services.Where(x => x.Observation != null).Sum(x => x.Observation!.Sessions.Count(s => (s.Execution || s.Preparing) && (s.AgentCreated != true || s.Pinned)));
    public bool Connected => Services.Any(x => x.Observation != null);
    public bool Connecting => Services.Any(x => x.Status == "Connecting…");
    public ClientController(string directory, bool preview = false)
    {
        Preview = preview;
        preferencePath = Path.Combine(directory, "preferences.json");
        Preferences = AtomicFile.Read(preferencePath, () => new Preferences());
        if (Preferences.Version != 1 || Preferences.Services.Select(x => x.Id).Distinct().Count() != Preferences.Services.Count)
            throw new InvalidDataException("Unsupported or corrupt saved service catalog.");
        foreach (var service in Preferences.Services) _ = new Endpoint(service.Address);
        credentials = new CredentialStore(Path.Combine(directory, "credentials"));
        Outbox = new Outbox(Path.Combine(directory, "outbox.json"));
        _ = Outbox.Entries; // Validate recovery data before admitting any new input.
        tray = new Forms.NotifyIcon { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "amplifier.ico")), Visible = true, Text = "Amplifier" };
        tray.DoubleClick += (_, _) => OpenDefault();
        tempo = new TempoWindow(this);
    }
    public void Start()
    {
        if (Preview)
        {
            Preferences.Services = [new Registration("11111111111111111111111111111111", "https://preview.example", "Preview", "synthetic")];
            var service = Preferences.Services[0];
            var runtime = new ServiceRuntime(service, new HostTransport(new Endpoint(service.Address)));
            runtime.Status = "Synthetic activity · no network";
            runtime.Observation = new Observation("synthetic", "preview", [new Session("sample", "Preview animation", "working", false, false, new(true, false, false))],
                [new Notice("ready", "sample1", "completion", new("session", "sample"), false, false, "Windows companion preview", "Synthetic activity — no services connected", false, false)], new(1, 0, 0, 1, 0));
            services.Add(service.Id, runtime);
        }
        else foreach (var service in Preferences.Services) Connect(service);
        Refresh();
        if (!Preferences.HideTempo) tempo.Show();
        if (Preferences.Services.Count == 0) OpenSettings();
    }
    public void Save() => AtomicFile.Write(preferencePath, Preferences);
    public void PreferencesChanged() { Save();Refresh(); }
    public void OpenSettings()
    {
        if (settings == null) { settings = new SettingsWindow(this);settings.Closed += (_, _) => settings = null; }
        settings.Show();settings.Activate();
    }
    public void Open(Uri url)
    {
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch { MessageBox.Show("Windows could not open the browser. Your chat has not been marked read.", "Amplifier"); }
    }
    public void OpenDefault()
    {
        if (Preview) { OpenSettings();return; }
        var selected = Preferences.Services.FirstOrDefault(x => x.Id == Preferences.PreferredId) ?? Preferences.Services.FirstOrDefault();
        if (selected == null) OpenSettings();else Open(new Endpoint(selected.Address).Origin);
    }
    public void OpenChat(NoticeRow row)
    {
        if (Preview) { OpenSettings();return; }
        var service = Preferences.Services.FirstOrDefault(x => x.Id == row.RegistrationId && x.HostIdentity == row.HostIdentity);
        if (service != null) Open(new Endpoint(service.Address).Chat(row.SessionId));
    }
    public void Dismiss(NoticeRow row)
    {
        Preferences.Dismissed[row.Key] = row.Fingerprint;
        while (Preferences.Dismissed.Count > 512) Preferences.Dismissed.Remove(Preferences.Dismissed.Keys.First());
        Save();Refresh();
    }
    public void ToggleTempo() { Preferences.HideTempo = !Preferences.HideTempo;Save();if (Preferences.HideTempo) tempo.Hide();else tempo.Show();Refresh(); }
    public string Status(string id) => services.TryGetValue(id, out var runtime) ? runtime.Status : "Disconnected";
    public async Task RegisterAsync(Endpoint endpoint, string label, string token, CancellationToken cancellation)
    {
        if (Preview) throw new InvalidOperationException("Connections are disabled in the isolated preview.");
        if (Preferences.Services.Any(x => new Endpoint(x.Address) == endpoint)) throw new InvalidOperationException("This service is already registered.");
        using var transport = new HostTransport(endpoint, token, Preferences.ClientId);
        var host = await new ObservationClient(transport).AttachAsync(null, cancellation);
        var registration = new Registration(Guid.NewGuid().ToString("N"), endpoint.ToString(), label.Trim(), host);
        cancellation.ThrowIfCancellationRequested();
        var previousPreferred = Preferences.PreferredId;
        credentials.Write(registration.Id, token);
        try { Preferences.Services.Add(registration);Preferences.PreferredId ??= registration.Id;Save(); }
        catch { Preferences.Services.Remove(registration);Preferences.PreferredId = previousPreferred;credentials.Remove(registration.Id);throw; }
        Connect(registration);Refresh();
    }
    public void Pause(Registration registration)
    {
        if (Preview) return;
        if (services.GetValueOrDefault(registration.Id)?.Sending == true) throw new InvalidOperationException("Wait for the current message delivery before disconnecting.");
        var replacement = registration with { Paused = !registration.Paused };
        Preferences.Services[Preferences.Services.FindIndex(x => x.Id == registration.Id)] = replacement;Save();
        if (services.Remove(registration.Id, out var old)) old.Dispose();Connect(replacement);Refresh();
    }
    public void Forget(Registration registration)
    {
        if (Preview) return;
        if (services.GetValueOrDefault(registration.Id)?.Sending == true) throw new InvalidOperationException("Wait for current message delivery before forgetting this service.");
        if (MessageBox.Show("Forget " + registration.Label + " on this PC? Saved messages remain with their original destination. Host revocation is separate.", "Amplifier", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        credentials.Remove(registration.Id);
        Preferences.Services.RemoveAll(x => x.Id == registration.Id);
        if (Preferences.PreferredId == registration.Id) Preferences.PreferredId = Preferences.Services.FirstOrDefault()?.Id;
        Save();if (services.Remove(registration.Id, out var old)) old.Dispose();Refresh();
    }
    public async Task SendAsync(PendingReply reply)
    {
        if (!services.TryGetValue(reply.RegistrationId, out var runtime) || runtime.Observation == null || runtime.Sending)
            throw new InvalidOperationException("The captured service is unavailable or already sending. Your message remains saved.");
        runtime.Sending = true;
        try { await Outbox.SendAsync(reply, runtime.Registration, runtime.Transport, runtime.Cancellation.Token); }
        finally { runtime.Sending = false;Changed?.Invoke(); }
    }
    public void Compose() { if (Preview) OpenSettings();else new ComposerWindow(this).Show(); }
    private void Connect(Registration registration)
    {
        if (registration.Paused) return;
        try
        {
            var runtime = new ServiceRuntime(registration, new HostTransport(new Endpoint(registration.Address), credentials.Read(registration.Id), Preferences.ClientId));
            services.Add(registration.Id, runtime);_ = PollAsync(runtime);
        }
        catch (Exception) { /* Saved registration is retained; UI exposes disconnected status. */ }
    }
    private async Task PollAsync(ServiceRuntime runtime)
    {
        var token = runtime.Cancellation.Token;var observer = new ObservationClient(runtime.Transport);var delay = 5;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await observer.AttachAsync(runtime.Registration.HostIdentity, token);
                var snapshot = await observer.PollAsync(token);
                if (!services.TryGetValue(runtime.Registration.Id, out var current) || current != runtime) return;
                runtime.Observation = snapshot;runtime.Status = "Connected";delay = Preferences.HideTempo ? 15 : 5;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (HostException error) when (error.AuthenticationLost)
            {
                runtime.Observation = null;runtime.Status = "Connection approval required";Refresh();return;
            }
            catch (Exception error)
            {
                runtime.Observation = null;runtime.Status = error is InvalidDataException ? error.Message : "Unavailable — reconnecting";
                delay = Math.Min(60, Math.Max(5, delay * 2));
            }
            if (disposed || token.IsCancellationRequested) return;
            Refresh();
            try { await Task.Delay(TimeSpan.FromSeconds(delay + Random.Shared.NextDouble()), token); }
            catch (OperationCanceledException) { return; }
        }
    }
    private void Refresh()
    {
        if (disposed) return;
        AllRows = Services.Where(x => x.Observation != null).SelectMany(x => Presentation.Rows(x.Registration, x.Observation!, Preferences.IncludeWorking)).ToArray();
        Rows = Presentation.Sort(AllRows.Where(x => Preferences.Dismissed.GetValueOrDefault(x.Key) != x.Fingerprint), Preferences.Services.Select(x => x.Id).ToArray(), Preferences.GroupByService);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Connected ? Attention + " updates · " + Working + " working" : "No services connected").Enabled = false;
        menu.Items.Add("Open Amplifier", null, (_, _) => OpenDefault());
        menu.Items.Add("New message…", null, (_, _) => Compose()).Enabled = !Preview;
        var serviceMenu = new Forms.ToolStripMenuItem("Services");
        foreach (var registration in Preferences.Services)
            serviceMenu.DropDownItems.Add(registration.Label + " · " + Status(registration.Id), null, (_, _) => { Preferences.PreferredId = registration.Id;Save(); });
        menu.Items.Add(serviceMenu);menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Preferences.HideTempo ? "Show Tempo" : "Hide Tempo", null, (_, _) => ToggleTempo());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Quit Amplifier", null, (_, _) => Application.Current.Shutdown());
        var previous = tray.ContextMenuStrip;tray.ContextMenuStrip = menu;previous?.Dispose();
        tray.Text = Preview ? "Amplifier preview · synthetic activity" : Connected ? "Amplifier · " + Attention + " updates · " + Working + " working" : "Amplifier · disconnected";
        tempo.Refresh();Changed?.Invoke();
    }
    public void Dispose()
    {
        disposed = true;foreach (var runtime in services.Values) runtime.Dispose();services.Clear();
        tray.Visible = false;tray.Dispose();tempo.Dispose();settings?.Close();
    }
}
