using System.Net;
using System.Net.NetworkInformation;

namespace BrotherNetwork.Core;

public interface IReachability
{
    Task<bool> SendAsync(string address, CancellationToken cancellationToken);
}

public sealed class IcmpReachability : IReachability
{
    public async Task<bool> SendAsync(string address, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(IPAddress.Parse(address),
            TimeSpan.FromMilliseconds(1200), new byte[16], null, cancellationToken);
        return reply.Status == IPStatus.Success;
    }
}

public static class Probe
{
    public static async Task<string> CheckAsync(string target, AdapterInfo adapter,
        bool authorized, IReachability transport, CancellationToken cancellationToken)
    {
        if (!authorized) throw new ArgumentException("Confirm that you own or administer this selected private subnet first.");
        IPv4.LocalTarget(target, adapter);
        cancellationToken.ThrowIfCancellationRequested();
        var reply = await transport.SendAsync(target, cancellationToken).WaitAsync(
            TimeSpan.FromSeconds(3), cancellationToken);
        return reply
            ? "Reply received. Address is in use; identity and settings are NOT verified. Do not assign it to a different device."
            : "No reply. Device may block ICMP, be offline or isolated. This does NOT prove the address is free.";
    }
}

public sealed record QueueEvidence(string Name, string Driver, string Port, string Address)
{
    public string Confidence => "Local Windows configuration only; model/address not verified on device";
    public override string ToString() => $"{Name} | {Address} | {Driver} | {Confidence}";
}

public static class QueueParser
{
    public static QueueEvidence? Parse(string name, string driver, string port, string address)
    {
        if (name.Length > 256 || driver.Length > 256 || port.Length > 256 ||
            !IPv4.TryParse(address, out var ip) || !IPv4.IsPrivate(ip))
            return null;
        return new QueueEvidence(name, driver, port, IPv4.Format(ip));
    }
}
