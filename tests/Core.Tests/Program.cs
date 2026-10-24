using BrotherNetwork.Core;

var total = 0;
void Check(bool condition, string label)
{
    total++;
    if (!condition) throw new InvalidOperationException($"FAILED: {label}");
}
void Reject(Action action, string label)
{
    try { action(); }
    catch (ArgumentException) { total++; return; }
    throw new InvalidOperationException($"FAILED (expected rejection): {label}");
}
async Task RejectAsync<T>(Func<Task> action, string label) where T : Exception
{
    try { await action(); }
    catch (T) { total++; return; }
    throw new InvalidOperationException($"FAILED (expected {typeof(T).Name}): {label}");
}

var adapter = new AdapterInfo("Synthetic Ethernet", "192.168.50.10", "255.255.255.0", "192.168.50.1");
var input = new PlanInput("192.168.50.20", "192.168.50.40", "255.255.255.0", "192.168.50.1", "02:00:00:00:00:01", true);
foreach (var bad in new[] { "", "127.1", "0xC0.168.50.1", "192.168.50.01", "-1.2.3.4", "1.2.3.256",
             "1.2.3.4/24", " 1.2.3.4", "1.2.3.4\n", "1.2.3.4@evil.example", "https://192.168.50.20/", "::1", "1.2.3.4:80" })
    Check(!IPv4.TryParse(bad, out _), $"Strict parse {bad}");
foreach (var good in new[] { "0.0.0.0", "255.255.255.255", "10.0.0.1", "172.16.0.1", "192.168.1.1" })
    Check(IPv4.Format(IPv4.Parse(good)) == good, "IPv4 roundtrip");
foreach (var privateIp in new[] { "10.0.0.1", "10.255.255.254", "172.16.0.1", "172.31.255.254", "192.168.0.1" })
    Check(IPv4.IsPrivate(IPv4.Parse(privateIp)), "RFC1918 range");
foreach (var publicIp in new[] { "9.255.255.255", "11.0.0.1", "172.15.255.255", "172.32.0.1", "192.169.0.1",
             "127.0.0.1", "169.254.1.1", "224.0.0.1", "0.0.0.0", "255.255.255.255" })
    Check(!IPv4.IsPrivate(IPv4.Parse(publicIp)), "Exclude public / special");
foreach (var badMask in new[] { "255.0.255.0", "255.255.255.254", "255.255.255.255", "0.0.0.0", "255.255.127.0" })
    Reject(() => IPv4.LanMask(badMask), "Bad mask");
for (var prefix = 1; prefix <= 30; prefix++)
{
    var numeric = uint.MaxValue << (32 - prefix);
    Check(IPv4.LanMask(IPv4.Format(numeric)) == numeric, $"Contiguous /{prefix}");
}
foreach (var target in new[] { "192.168.50.0", "192.168.50.255", "192.168.50.10", "192.168.51.20", "8.8.8.8",
             "127.0.0.1", "224.0.0.1", "169.254.1.2", "0.0.0.0" })
    Reject(() => IPv4.LocalTarget(target, adapter), "Target policy");
Check(IPv4.PrinterUri(input.Current, adapter, false).AbsoluteUri == "https://192.168.50.20/", "HTTPS default");
Check(IPv4.PrinterUri(input.Current, adapter, true).AbsoluteUri == "http://192.168.50.20/", "Explicit HTTP");
foreach (var attack in new[] { "//evil.example", "192.168.50.20/?x=y", "192.168.50.20#@evil.example", "file:///C:/",
             "192.168.50.20\r\nx", "192.168.50.20:443", "user@192.168.50.20" })
    Reject(() => IPv4.PrinterUri(attack, adapter, false), "URI injection");
var plan = Planner.Review(input, adapter);
Check(plan.Text.Contains("NOT applied") && plan.Text.Contains(input.Candidate) && plan.Text.Contains("does NOT prove"), "Honest export");
Check(plan.Input == input && plan.Adapter == adapter, "Immutable reviewed snapshot");
Reject(() => Planner.Review(input with { Candidate = adapter.Address }, adapter), "Self candidate");
Reject(() => Planner.Review(input with { Candidate = adapter.Gateway }, adapter), "Router candidate");
Reject(() => Planner.Review(input with { Current = adapter.Gateway, Gateway = "" }, adapter), "Current router address");
Reject(() => Planner.Review(input with { Gateway = input.Candidate }, adapter), "Gateway collision");
Reject(() => Planner.Review(input with { Gateway = input.Current }, adapter), "Gateway current collision");
Reject(() => Planner.Review(input with { Gateway = "192.168.51.1" }, adapter), "Off-link gateway");
Reject(() => Planner.Review(input with { Gateway = "192.168.50.255" }, adapter), "Broadcast gateway");
Reject(() => Planner.Review(input with { Mask = "255.255.255.224" }, adapter), "Mask separates PC/printer");
Reject(() => Planner.Review(input with { Mac = "" }, adapter), "Reservation needs MAC");
Check(Planner.Review(input with { Reservation = false, Mac = "", Gateway = "" }, adapter).Text.Contains("None; verify"), "Optional gateway / static MAC");
Check(Planner.Review(input with { Candidate = input.Current }, adapter).Input.Candidate == input.Current, "Keep existing stable IP");
foreach (var badMac in new[] { "00:00:00:00:00:00", "ff:ff:ff:ff:ff:ff", "01:00:00:00:00:01", "02:00-00:00:00:01", "Pwd", "02:00:00:00:00:01\nsecret" })
    Check(!Planner.ValidMac(badMac), "Malformed or invalid unicast MAC");
Check(Planner.ValidMac("02-00-00-00-00-01"), "Hyphenated MAC");
var smaller = new AdapterInfo("Synthetic /30", "10.0.0.1", "255.255.255.252", "");
Check(IPv4.LocalTarget("10.0.0.2", smaller) == IPv4.Parse("10.0.0.2"), "/30 peer");
Reject(() => IPv4.LocalTarget("10.0.0.3", smaller), "/30 broadcast");
var larger = new AdapterInfo("Synthetic /23", "172.16.0.10", "255.255.254.0", "172.16.0.1");
Check(IPv4.LocalTarget("172.16.1.254", larger) == IPv4.Parse("172.16.1.254"), "/23 across octet");
Reject(() => IPv4.LocalTarget("172.16.1.255", larger), "/23 broadcast");
Reject(() => IPv4.LocalTarget("192.168.50.20", adapter with { Address = "8.8.8.8" }), "Nonprivate adapter");
var queue = QueueParser.Parse("Untrusted Brother label", "Driver DCP-T820DW", "IP_mock", "192.168.50.20");
Check(queue is not null && queue.Confidence.Contains("not verified"), "Queue evidence confidence");
foreach (var malformed in new[] { "hostname", "0xC0A83214", "8.8.8.8", "192.168.50.20/24", "", "192.168.50.20:80" })
    Check(QueueParser.Parse("Queue", "Driver", "Port", malformed) is null, "Malformed port address");
Check(QueueParser.Parse(new string('x', 257), "Driver", "Port", input.Current) is null, "Queue metadata bounded");
var fake = new FakeReachability();
await RejectAsync<ArgumentException>(() => Probe.CheckAsync(input.Current, adapter, false, fake, default), "Authorization required");
await RejectAsync<ArgumentException>(() => Probe.CheckAsync("8.8.8.8", adapter, true, fake, default), "No internet probe");
Check(fake.Calls == 0, "No transport for rejected probes");
Check((await Probe.CheckAsync(input.Current, adapter, true, fake, default)).Contains("does NOT prove"), "No reply uncertainty");
fake.Reply = true;
Check((await Probe.CheckAsync(input.Current, adapter, true, fake, default)).Contains("identity and settings are NOT verified"), "Reply not identity");
using var cancel = new CancellationTokenSource();
cancel.Cancel();
await RejectAsync<OperationCanceledException>(() => Probe.CheckAsync(input.Current, adapter, true, fake, cancel.Token), "Pre-cancel");
fake.Hang = true;
using var inFlight = new CancellationTokenSource(30);
await RejectAsync<OperationCanceledException>(() => Probe.CheckAsync(input.Current, adapter, true, fake, inFlight.Token), "In-flight cancel");
await RejectAsync<TimeoutException>(() => Probe.CheckAsync(input.Current, adapter, true, fake, default), "Overall timeout bound");
Console.WriteLine($"PASS: {total} offline assertions. No real network transport used.");

sealed class FakeReachability : IReachability
{
    public int Calls { get; private set; }
    public bool Reply { get; set; }
    public bool Hang { get; set; }
    public Task<bool> SendAsync(string address, CancellationToken cancellationToken)
    {
        Calls++;
        return Hang ? new TaskCompletionSource<bool>().Task : Task.FromResult(Reply);
    }
}
