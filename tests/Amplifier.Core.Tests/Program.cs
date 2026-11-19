using Amplifier.Core;
using System.Net;
using System.Text;
using System.Text.Json;

var passed = 0;
var endpoint = new Endpoint("https://example.test");
var registration = new Registration("service-a", endpoint.ToString(), "Example", "host-a");
const string attach = """{"protocolVersion":1,"hostIdentity":"host-a","hostInstanceId":"instance-a","capabilities":{"observations":{"version":1,"sessions":"/api/sessions?view=observation","attention":"/api/attention","activity":"/api/activity","identity":true,"dismissals":{"version":1,"action":"presentation.dismiss"}}}}""";
const string ready = """{"revision":7,"status":"ready","nextCursor":null,"executionCount":1,"preparingCount":0,"loadingCount":0,"attentionCount":0,"requiresActionCount":0}""";
const string session = """{"id":"chat-a","title":"Test chat","status":"working","pinned":false,"agentCreated":false,"dismissal":{"id":"activity-a","fingerprint":"work-one","dismissed":false},"activity":{"execution":true,"preparing":false,"loading":false}}""";
string Page(string items = "", string cursor = "null", int revision = 1) => $"{{\"revision\":{revision},\"items\":[{items}],\"nextCursor\":{cursor}}}";
async Task Check(string name, Func<Task> run) { await run();passed++;Console.WriteLine("PASS " + name); }
void Assert(bool value) { if (!value) throw new Exception("Assertion failed."); }
async Task Reject(Func<Task> work) { try { await work(); } catch (Exception e) when (e is not TestFailure) { return; } throw new TestFailure("Expected rejection."); }
HttpResponseMessage Response(string json, int code = 200) {
    var body = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
    if (body.ContainsKey("revision")) { body.TryAdd("hostIdentity", "host-a"); body.TryAdd("hostInstanceId", "instance-a"); }
    return new((HttpStatusCode)code) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
HostTransport Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) => new(endpoint, handler: new Handler(handle));
HostTransport Queue(params string[] bodies) { var q = new Queue<string>(bodies);return Transport((_, _) => Task.FromResult(Response(q.Dequeue()))); }

await Check("origin and navigation boundaries", async () => {
    foreach (var address in new[] { "http://example.test", "https://user:pass@example.test", "https://example.test/path", "https://example.test?secret=x", "https://example.test/#x" })
        await Reject(() => Task.FromResult(new Endpoint(address)));
    foreach (var path in new[] { "//elsewhere.test/a", "https://elsewhere.test", "/\\elsewhere.test", "/#fragment" }) await Reject(() => Task.FromResult(endpoint.Resource(path)));
    Assert(new Endpoint("https://EXAMPLE.test:443/") == endpoint);
    Assert(new Endpoint("http://localhost:8080").Origin.IsLoopback);
    Assert(endpoint.Chat("x&token=private").Query == "?chat=x%26token%3Dprivate");
    await Reject(() => Task.FromResult(new HostTransport(endpoint, "owner-token")));
});
await Check("attach advertises native identity without browser state", async () => {
    using var transport = Transport(async (req, ct) => {
        Assert(req.RequestUri!.AbsolutePath == "/api/clients/attach");
        var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)).RootElement;
        Assert(body.GetProperty("kind").GetString() == "native" && !body.GetProperty("includeState").GetBoolean());
        Assert(req.Headers.Contains("X-Amplifier-State-Transport"));
        Assert(req.Headers.Authorization == null);
        return Response(attach);
    });
    Assert(await new ObservationClient(transport).AttachAsync(null, default) == "host-a");
});
await Check("host replacement and hostile capability fail closed", async () => {
    using var first = Queue(attach);await Reject(() => new ObservationClient(first).AttachAsync("another-host", default));
    using var second = Queue(attach.Replace("/api/attention", "https://evil.test/steal"));await Reject(() => new ObservationClient(second).AttachAsync(null, default));
});
await Check("empty pages continue, activity collecting has no partial totals", async () => {
    using var transport = Queue(attach, Page(cursor:"\"next\""), Page(session), Page(), """{"revision":7,"status":"collecting","nextCursor":"count"}""", ready);
    var client = new ObservationClient(transport);await client.AttachAsync(null, default);var observation = await client.PollAsync(default);
    Assert(observation.Sessions.Single().Execution && observation.Activity.ExecutionCount == 1);
});
await Check("repeated cursor and changed revision reject incomplete snapshots", async () => {
    using var repeated = Queue(attach, Page(cursor:"\"repeat\""), Page(cursor:"\"repeat\""));
    var client = new ObservationClient(repeated);await client.AttachAsync(null, default);await Reject(() => client.PollAsync(default));
    using var changed = Queue(attach, Page(cursor:"\"next\""), Page(session, revision:2));
    client = new ObservationClient(changed);await client.AttachAsync(null, default);await Reject(() => client.PollAsync(default));
});
await Check("missing activity fields do not become reassuring zero counts", async () => {
    using var transport = Queue(attach, Page(), Page(), ready.Replace(",\"executionCount\":1", ""));
    var client = new ObservationClient(transport);await client.AttachAsync(null, default);await Reject(() => client.PollAsync(default));
});
await Check("missing nested session activity rejects snapshot", async () => {
    using var transport = Queue(attach, Page("""{"id":"a","status":"working","title":"A","pinned":false}"""));
    var client = new ObservationClient(transport);await client.AttachAsync(null, default);await Reject(() => client.PollAsync(default));
});
await Check("409 abandons partial traversal and begins again", async () => {
    var responses = new Queue<HttpResponseMessage>([Response(attach), Response(Page(session, "\"next\"")), Response("{}", 409), Response(Page()), Response(Page()), Response(ready)]);
    using var transport = Transport((_, _) => Task.FromResult(responses.Dequeue()));
    var client = new ObservationClient(transport);await client.AttachAsync(null, default);var observation = await client.PollAsync(default);
    Assert(observation.Sessions.Count == 0 && responses.Count == 0);
});
await Check("401 is surfaced without an automatic retry", async () => {
    int count = 0;using var transport = Transport((_, _) => { count++;return Task.FromResult(Response("{}",401)); });
    try { await new ObservationClient(transport).AttachAsync(null,default);throw new TestFailure("Expected auth error"); }
    catch (HostException e) { Assert(e.AuthenticationLost && count == 1); }
});
await Check("one request in flight, cancellation and response bounds", async () => {
    int active = 0, maximum = 0;
    using var transport = Transport(async (_, ct) => { var value=Interlocked.Increment(ref active);maximum=Math.Max(maximum,value);await Task.Delay(20,ct);Interlocked.Decrement(ref active);return Response("{}"); });
    await Task.WhenAll(Enumerable.Range(0,5).Select(_ => transport.RequestAsync("/api/activity",null,default)));
    Assert(maximum == 1);
    using var huge=Queue(new string(' ',2*1024*1024+1));await Reject(() => huge.RequestAsync("/api/activity",null,default));
});
await Check("pairing validates proof and same-origin approval path", async () => {
    using var transport=Transport(async (request, ct) => {
        var value=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
        Assert(value.GetProperty("challenge").GetString()!.Length==43 && !value.TryGetProperty("verifier",out _));
        return Response(JsonSerializer.Serialize(new { id=new string('a',43), code="ABCD-2345", verificationPath="https://evil.test/approve", expiresIn=120, interval=5 }));
    });
    await Reject(() => new PairingClient(transport).BeginAsync(default));
});
await Check("global versus service grouping and plain summary", () => {
    var rows=new[] { new NoticeRow("a","a","h","s","A","", "f",AvatarState.Working,"A"),new NoticeRow("b","b","h","s","B","","f",AvatarState.Review,"B") };
    Assert(Presentation.Sort(rows,["a","b"],false)[0].Key=="b");
    Assert(Presentation.Sort(rows,["a","b"],true)[0].Key=="a");
    Assert(Presentation.Plain("**Done**: [sample](https://example.test)\n`code`")=="Done: sample code");return Task.CompletedTask;
});
await Check("Mac parity: stable host ties, pinned inactive notices, one row per working chat", () => {
    var service = registration;
    var working = new Session("active", "Active", "working", false, false, new(true,false,false));
    Notice N(string id, string chat, string kind, bool agent=false, bool pinned=false) => new(id,"f",kind,new("session",chat),kind=="session-error",false,id,"Summary",agent,pinned);
    var observation = new Observation("h","i",[working],[
        N("Zulu", "inactive", "completion",true,true), N("Alpha", "other", "completion"),
        N("Error", "failed", "session-error"), N("Active", "active", "completion")], new(1,0,0,4,1));
    var rows = Presentation.Sort(Presentation.ChatRows(Presentation.Rows(service,observation,true)),[service.Id],false);
    Assert(rows.Count==4 && rows[0].Title=="Error" && rows[1].Title=="Zulu" && rows[2].Title=="Alpha");
    Assert(rows.Count(x=>x.SessionId=="active")==1 && rows.Last().State==AvatarState.Working && rows.Last().Attention);
    Assert(rows.Last().Detail=="Unread · Thinking…");return Task.CompletedTask;
});
await Check("one visible chat per service, with sidebar status and exact dismissal states", () => {
    var completion = new DismissalDescriptor("completion:s", "reply", false);
    var error = new DismissalDescriptor("error:s", "error", false);
    var status = new ChatStatus("attention", "Needs attention");
    var ready = new NoticeRow("completion","first","host","s","Chat","Result","reply",AvatarState.Ready,"First")
        { ChatStatus = status, Dismissal = completion, UnreadResponse = true };
    var failure = ready with { Key = "error", State = AvatarState.Error, Dismissal = error, UnreadResponse = false };
    var other = ready with { RegistrationId = "second", ChatStatus = new("paused","Paused") };
    var rows = Presentation.ChatRows([ready, failure, other]).ToArray();
    Assert(rows.Length == 2 && rows[0].Detail == "Needs attention · Unread response");
    Assert(rows[0].VisibleStates.SequenceEqual(new[] { completion, error }));
    Assert(rows[1].Detail == "Paused · Unread response");
    return Task.CompletedTask;
});
await Check("notice size defaults to compact and never exceeds original size", () => {
    var preferences=JsonSerializer.Deserialize<Preferences>("{}",Json.Options)!;
    Assert(preferences.NoticeSize==NoticeSize.Compact);
    Assert(NoticeSizes.Scale(NoticeSize.Compact)<NoticeSizes.Scale(NoticeSize.Comfortable));
    Assert(NoticeSizes.Scale(NoticeSize.Comfortable)<NoticeSizes.Scale(NoticeSize.Large));
    Assert(NoticeSizes.Scale(NoticeSize.Large)==1);return Task.CompletedTask;
});
await Check("PWA launch is exact-origin and preserves the chat argument without shell parsing", async () => {
    var app=new InstalledWebApp("https://example.test/",new string('a',32),"Profile 2");
    Assert(app.Accepts(endpoint.Chat("a&b")));
    Assert(app.Arguments(endpoint.Chat("a&b")).Last()=="--app-launch-url-for-shortcuts-menu-item=https://example.test/?chat=a%26b");
    foreach(var target in new[]{"https://other.test/","https://example.test:444/","http://example.test/","https://user@example.test/"})
        await Reject(()=>Task.FromResult(app.Arguments(new Uri(target))));
    Assert(!(app with { Profile="../profile" }).Accepts(endpoint.Origin));
    Assert(!(app with { AppId="--bad" }).Accepts(endpoint.Origin));
});
await Check("display details never use raw tool input or an answer before the latest user", () => {
    JsonElement Parse(string json)=>JsonDocument.Parse(json).RootElement;
    Assert(NoticeDetails.Response(Parse("""[{"role":"assistant","text":"old"},{"role":"user","text":"new"}]"""))==null);
    Assert(NoticeDetails.Response(Parse("""[{"role":"assistant","text":"**Ready**"}]"""))=="Ready");
    var work=NoticeDetails.Work(Parse("""[{"id":"a","kind":"tool","tool":"app_control","label":"app_control","phase":"running","actionPath":"dispatch → browser.click","input":{"password":"NEVER SHOW"}},{"id":"b","kind":"llm","phase":"running"}]"""));
    Assert(work?.Text=="App · browser click" && work?.State=="a|tool|running");return Task.CompletedTask;
});
await Check("response preview waits for a matching complete observation and discards stale work", async () => {
    var notice=new Notice("n","f","completion",new("session","chat"),false,false,"Chat","Response ready",false,false);
    var observation=new Observation("h","i",[],[notice],new(0,0,0,1,0));
    var row=Presentation.Rows(registration,observation,true).Single();var details=new NoticeDetails();
    using var transport=Queue("""{"items":[{"role":"assistant","text":"New answer"}]}""");
    details.Reconcile(observation);await details.FetchAsync(transport,observation,[row],default);
    Assert(details.Enrich(row,observation).Detail=="Response ready");
    details.Reconcile(observation);Assert(details.Enrich(row,observation).Detail=="New answer");
    var active=observation with { Sessions=[new Session("chat","Chat","working",false,false,new(true,false,false))] };
    details.Reconcile(active);Assert(details.Enrich(row,active).Detail=="Response ready");
});
await Check("working dismissal survives missing detail but clears for a new action", () => {
    var row=new NoticeRow("k","s","h","c","Chat","Thinking…","work|working|",AvatarState.Working,"Service") { Attention=false };
    var saved=new Dictionary<string,string>{{"k",row.Fingerprint}};
    Assert(Presentation.Dismissed(row with { Fingerprint="work|working|action-a" },saved));
    Assert(Presentation.Dismissed(row,saved));
    Assert(!Presentation.Dismissed(row with { Fingerprint="work|working|action-b" },saved));return Task.CompletedTask;
});
await Check("agent-created work is opt-in through pinning", () => {
    var hidden=new Session("a","A","working",false,true,new(true,false,false));
    var observation=new Observation("h","i",[hidden],[],new(1,0,0,0,0));
    Assert(Presentation.Rows(registration,observation,true).Count==0);
    Assert(Presentation.Rows(registration,observation with { Sessions=[hidden with { Pinned=true }] },true).Count==1);return Task.CompletedTask;
});
await Check("hover grace, drag hold, dismiss and re-entry", () => {
    var hover=new HoverPresentation { Mode=NoticeVisibility.Hover };
    hover.Update(true,false,TimeSpan.Zero);Assert(hover.Visible);
    hover.Update(false,false,TimeSpan.FromSeconds(1));Assert(hover.Visible);
    hover.Update(false,true,TimeSpan.FromSeconds(2));Assert(hover.Visible);
    hover.Dismiss();Assert(!hover.Visible);
    hover.Update(false,false,TimeSpan.FromSeconds(3));hover.Update(false,false,TimeSpan.FromSeconds(4));hover.Update(true,false,TimeSpan.FromSeconds(5));Assert(hover.Visible);return Task.CompletedTask;
});
await Check("viewport shrinks at top edge without moving its anchor", () => {
    Assert(NoticeGeometry.Height(1000,600,false,200,0)==162);
    Assert(NoticeGeometry.Height(90,600,false,900,0)==90);
    Assert(NoticeGeometry.Height(1000,100,true,500,0)==462);
    Assert(NoticeGeometry.Height(100,500,true,15,0)==0);return Task.CompletedTask;
});
await Check("every avatar/state/finish-independent pose has bounded weights", () => {
    foreach(var shape in Presentation.Shapes) foreach(var state in Enum.GetValues<AvatarState>()) foreach(var reduced in new[]{false,true})
        for(double t=0;t<12;t+=.037) {
            var pose=AvatarPose.Sample(shape,state,t,reduced);
            Assert(pose.Weights.All(w=>double.IsFinite(w)&&w>=0&&w<=1));Assert(Math.Abs(pose.Weights.Sum()-1)<1e-9);
        }
    return Task.CompletedTask;
});
await Check("attention reaction settles back to ongoing animation without replay", () => {
    var playback=new AvatarPlayback();var row=new NoticeRow("r","a","h","s","","","f",AvatarState.Ready,"A");
    playback.Update(true,false,1,[row],0);Assert(playback.Presentation(1).State==AvatarState.Ready);
    playback.Update(true,false,1,[row],5);Assert(playback.Presentation(5).State==AvatarState.Working);return Task.CompletedTask;
});

await Check("restricted and anonymous denials never use a legacy feed", async () => {
    foreach (var restricted in new[] { false, true }) foreach(var status in new[]{401,403,404,503}) {
        var paths = new List<string>();
        using var transport = new HostTransport(endpoint, restricted ? "amc_" + new string('0',32) + "." + new string('a',64) : null,
            handler: new Handler((req,_) => {
                paths.Add(req.RequestUri!.AbsolutePath);
                return Task.FromResult(req.RequestUri.AbsolutePath == "/api/clients/attach" ? Response(attach) : Response("{}", status));
            }));
        var client=new ObservationClient(transport); await client.AttachAsync(null,default);
        await Reject(()=>client.PollAsync(default));
        Assert(paths.SequenceEqual(new[]{"/api/clients/attach", "/api/sessions"}));
    }
});
await Check("duplicate generic IDs cannot publish a baseline", async () => {
    using var transport = Queue(attach, Page(session, cursor:"\"next\""), Page(session));
    var client = new ObservationClient(transport); await client.AttachAsync(null, default);
    await Reject(()=>client.PollAsync(default));
});
await Check("dismissal retries retain exact command and never fall back after denial", async () => {
    var intent = new DismissalIntent("stable-command", registration.Id, endpoint.ToString(), "host-a", "update", "state");
    var writes = 0;
    using var transport = Transport(async (req, ct) => {
        if (req.RequestUri!.AbsolutePath == "/api/clients/attach") return Response(attach);
        Assert(req.RequestUri.AbsolutePath == "/api/actions"); writes++;
        var body = JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)).RootElement;
        Assert(body.GetProperty("id").GetString() == intent.RequestId);
        Assert(body.GetProperty("action").GetString() == "presentation.dismiss");
        var item = body.GetProperty("args").GetProperty("items")[0];
        Assert(item.GetProperty("id").GetString() == "update" && item.GetProperty("fingerprint").GetString() == "state");
        return Response(writes == 1 ? "{}" : "{\"accepted\":true}", writes == 1 ? 503 : 200);
    });
    var client = new ObservationClient(transport);
    await Reject(() => client.DismissAsync(intent, default)); Assert(writes == 1);
    await client.DismissAsync(intent, default); Assert(writes == 2);
    await Reject(() => client.DismissAsync(intent with { HostIdentity = "other" }, default)); Assert(writes == 2);
});
await Check("service restart abandons old pages and rediscovery pins fresh identity", async () => {
    using var transport = Queue(attach, Page(session, cursor:"\"next\""),
        Page().Replace("\"revision\":1", "\"hostInstanceId\":\"instance-b\",\"revision\":1"),
        attach.Replace("instance-a", "instance-b"),
        Page().Replace("\"revision\":1", "\"hostInstanceId\":\"instance-b\",\"revision\":1"),
        Page().Replace("\"revision\":1", "\"hostInstanceId\":\"instance-b\",\"revision\":1"),
        ready.Replace("\"revision\":7", "\"hostInstanceId\":\"instance-b\",\"revision\":7"));
    var client = new ObservationClient(transport); await client.AttachAsync(null, default);
    var value = await client.PollAsync(default); Assert(value.HostInstanceId == "instance-b" && value.Sessions.Count == 0);
});
var temp=Path.Combine(Path.GetTempPath(),"amplifier-core-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try {
    await Check("shared dismissal intent survives restart and is exact-state and service scoped", () => {
        var path = Path.Combine(temp, "dismissals.json");
        var state = new DismissalDescriptor("update", "one", false);
        var store = new SharedDismissals(path); var first = store.Save(registration, state);
        Assert(store.Save(registration, state).RequestId == first.RequestId);
        store = new SharedDismissals(path);
        Assert(store.Hidden(registration, state));
        Assert(!store.Hidden(registration, state with { Fingerprint = "two" }));
        var other = registration with { Id = "other", Address = "https://other.example" };
        Assert(!store.Hidden(other, state));
        var second = store.Save(other, state);
        store.Accepted(first.RequestId);
        Assert(store.Entries.Single(x=>x.RequestId==first.RequestId).Accepted);
        store.Reconcile(registration, [state]); Assert(store.Hidden(registration, state));
        store.Reconcile(registration, [state with { Dismissed = true }]);
        Assert(store.Entries.Single().RequestId == second.RequestId);
        Assert(store.Hidden(registration, state with { Dismissed = true }));
        store.Reconcile(other, [state with { Fingerprint = "two" }]); Assert(store.Entries.Count == 0);
        return Task.CompletedTask;
    });
    await Check("saved intent survives uncertain delivery; explicit retry preserves ID", async () => {
        var outbox=new Outbox(Path.Combine(temp,"outbox.json"));var reply=outbox.Save(registration,"chat-a","Hello");
        using var lost=Transport((req,_) => req.RequestUri!.AbsolutePath=="/api/clients/attach" ? Task.FromResult(Response(attach)) : throw new HttpRequestException("Lost response"));
        await Reject(()=>outbox.SendAsync(reply,registration,lost,default));Assert(outbox.Entries.Single().Status=="unknown");
        var recovered=new Outbox(Path.Combine(temp,"outbox.json"));Assert(recovered.Entries.Single().Id==reply.Id);
        var writes=0;using var retry=Transport(async (req,ct)=> {
            if(req.RequestUri!.AbsolutePath=="/api/clients/attach")return Response(attach);
            writes++;var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync(ct)).RootElement;
            Assert(body.GetProperty("id").GetString()==reply.Id && body.GetProperty("args").GetProperty("text").GetString()=="Hello");return Response("{\"accepted\":true}");
        });
        await Reject(()=>recovered.SendAsync(reply with {Text="Changed"},registration,retry,default));
        await Reject(()=>recovered.SendAsync(reply,registration with {Id="other"},retry,default));
        Assert(writes==0);await recovered.SendAsync(reply,registration,retry,default);await recovered.SendAsync(reply,registration,retry,default);Assert(writes==1);
    });
    await Check("concurrent local saves do not lose intent", async () => {
        var outbox=new Outbox(Path.Combine(temp,"parallel.json"));
        await Task.WhenAll(Enumerable.Range(0,30).Select(i=>Task.Run(()=>outbox.Save(registration,"chat-a",i.ToString()))));Assert(outbox.Entries.Count==30);
    });
    await Check("corrupt saved data is not silently overwritten", async () => {
        var path=Path.Combine(temp,"broken.json");File.WriteAllText(path,"broken");await Reject(()=>Task.FromResult(AtomicFile.Read(path,()=>new Preferences())));Assert(File.ReadAllText(path)=="broken");
    });
} finally { Directory.Delete(temp,true); }
Console.WriteLine($"{passed} focused checks passed.");
sealed class TestFailure(string message):Exception(message);
sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handle):HttpMessageHandler
{ protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>handle(request,cancellationToken); }
