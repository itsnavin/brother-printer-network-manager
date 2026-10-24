using System.Net.NetworkInformation;
using System.Net.Sockets;
using BrotherNetwork.Core;
using Microsoft.Win32;

namespace BrotherNetwork.App;

internal static class LocalInventory
{
    public static List<AdapterInfo> Adapters()
    {
        var result = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var properties = nic.GetIPProperties();
            var gateway = properties.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";
            foreach (var address in properties.UnicastAddresses.Where(a =>
                         a.Address.AddressFamily == AddressFamily.InterNetwork))
                result.Add(new AdapterInfo(nic.Name, address.Address.ToString(),
                    address.IPv4Mask.ToString(), gateway));
        }
        return result;
    }

    public static List<QueueEvidence> Queues()
    {
        var result = new List<QueueEvidence>();
        using var printers = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
        using var ports = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports");
        if (printers is null || ports is null) return result;
        foreach (var name in printers.GetSubKeyNames())
        {
            using var printer = printers.OpenSubKey(name);
            var portName = printer?.GetValue("Port") as string ?? "";
            using var port = ports.OpenSubKey(portName);
            var address = port?.GetValue("IPAddress") as string ?? "";
            var evidence = QueueParser.Parse(name, printer?.GetValue("Printer Driver") as string ?? "",
                portName, address);
            if (evidence is not null) result.Add(evidence);
        }
        return result;
    }
}
