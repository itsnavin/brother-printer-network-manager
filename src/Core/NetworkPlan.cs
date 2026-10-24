using System.Globalization;

namespace BrotherNetwork.Core;

public sealed record AdapterInfo(string Name, string Address, string Mask, string Gateway)
{
    public override string ToString() => $"{Name} | {Address} / {Mask}";
}

public sealed record PlanInput(string Current, string Candidate, string Mask, string Gateway,
    string Mac, bool Reservation);

public sealed record ReviewedPlan(PlanInput Input, AdapterInfo Adapter, string Text);

public static class IPv4
{
    public static bool TryParse(string text, out uint value)
    {
        value = 0;
        var parts = text.Split('.');
        if (parts.Length != 4) return false;
        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 3 || (part.Length > 1 && part[0] == '0') ||
                !part.All(c => c is >= '0' and <= '9') ||
                !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                return false;
            value = (value << 8) | b;
        }
        return true;
    }

    public static uint Parse(string text) => TryParse(text, out var value) ? value :
        throw new ArgumentException("Enter a dotted-decimal IPv4 address (four numbers, no leading zeros).");

    public static string Format(uint value) =>
        $"{value >> 24}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}";

    public static bool IsPrivate(uint value) =>
        (value >> 24) == 10 || (value >> 20) == 0xAC1 || (value >> 16) == 0xC0A8;

    public static bool IsOrdinaryHost(uint address, uint mask) =>
        (address & ~mask) != 0 && (address & ~mask) != ~mask;

    public static uint LanMask(string text)
    {
        var mask = Parse(text);
        var inverse = ~mask;
        if ((inverse & (inverse + 1)) != 0 || inverse < 3 || mask == 0)
            throw new ArgumentException("Use a contiguous LAN mask from /1 through /30; /31 and /32 are not printer LANs.");
        return mask;
    }

    public static uint LocalTarget(string text, AdapterInfo adapter)
    {
        var address = Parse(text);
        var pc = Parse(adapter.Address);
        var mask = LanMask(adapter.Mask);
        if (!IsPrivate(address) || !IsPrivate(pc) ||
            (address & mask) != (pc & mask) || !IsOrdinaryHost(address, mask) || address == pc)
            throw new ArgumentException("Target must be a private, usable IPv4 host on the selected adapter subnet, not this PC.");
        return address;
    }

    public static Uri PrinterUri(string address, AdapterInfo adapter, bool http)
    {
        var target = LocalTarget(address, adapter);
        return new Uri($"{(http ? "http" : "https")}://{Format(target)}/");
    }
}

public static class Planner
{
    public const string Caution =
        "DHCP pool and address ownership are NOT known. Check your router's leases, reservations and pool, " +
        "and your network administrator before choosing this address. No ping reply does NOT prove it is free. " +
        "A reply does NOT identify a Brother printer. Local queue data may be stale or user-edited.";

    public static ReviewedPlan Review(PlanInput input, AdapterInfo adapter)
    {
        var current = IPv4.LocalTarget(input.Current, adapter);
        var candidate = IPv4.LocalTarget(input.Candidate, adapter);
        var mask = IPv4.LanMask(input.Mask);
        var pc = IPv4.Parse(adapter.Address);
        if ((candidate & mask) != (pc & mask) || (current & mask) != (candidate & mask) ||
            !IPv4.IsOrdinaryHost(candidate, mask) || !IPv4.IsOrdinaryHost(pc, mask))
            throw new ArgumentException("The proposed mask must keep the printer and selected PC on the same usable subnet.");
        if (!string.IsNullOrEmpty(input.Gateway))
        {
            var gateway = IPv4.LocalTarget(input.Gateway, adapter);
            if ((gateway & mask) != (candidate & mask) || !IPv4.IsOrdinaryHost(gateway, mask) ||
                gateway == candidate || gateway == current)
                throw new ArgumentException("Gateway must be a distinct usable host on the proposed subnet, not a printer address.");
        }
        if (IPv4.TryParse(adapter.Gateway, out var pcGateway) && (candidate == pcGateway || current == pcGateway))
            throw new ArgumentException("A printer address equals the selected PC's gateway. Verify router addresses first.");
        if (input.Mac.Length > 0 && !ValidMac(input.Mac))
            throw new ArgumentException("MAC must be six hexadecimal pairs separated by colons or hyphens; use the active interface's report.");
        if (input.Reservation && input.Mac.Length == 0)
            throw new ArgumentException("Record the active wired interface MAC from the report for a DHCP reservation.");
        var text = $"""
            Brother DCP-T820DW - reviewed plan (NOT applied)
            Method: {(input.Reservation ? "Router DHCP reservation (recommended)" : "Manual printer static TCP/IP")}
            Interface: Wired Ethernet
            Selected PC adapter: {adapter.Name}
            PC address / mask: {adapter.Address} / {adapter.Mask}
            PC gateway (inferred router, NOT printer-confirmed): {adapter.Gateway}
            Current printer IP (user-confirmed report): {input.Current}
            Wired MAC (user record, not discovered): {input.Mac}
            Proposed printer IP: {input.Candidate}
            Proposed mask: {input.Mask}
            Proposed gateway: {(input.Gateway.Length == 0 ? "None; verify printer permits an empty/zero gateway" : input.Gateway)}
            Mask/gateway are editable PC-derived suggestions until checked against printer/router.

            {Caution}

            Before proceeding: save the current report privately; it can include a password.
            Do not enter credentials into this app or include them in an export.
            Reservation: verify the wired MAC at your router, keep printer DHCP/Auto, and use the router's supported reservation UI.
            Static: authenticate directly to the printer. Find the active Wired LAN TCP/IP settings.
            If the firmware offers BOOT Method = Static, choose it and enter the reviewed values.
            Exact web menu labels/options vary and have not been verified on hardware.
            The model manual confirms panel Menu > Network > Wired LAN > TCP/IP, including BOOT Method, IP Address, Subnet Mask and Gateway.
            If Static is absent or unclear, STOP and use Brother support or a router reservation.
            Submit manually only after checking every value. Restart only if Brother's UI requests it.

            After changing: reconnect explicitly to {input.Candidate}; print a fresh Network Configuration Report.
            Confirm IP, active interface and MAC. Reachability alone does not verify settings or model.
            Windows: Printer properties > Ports. Record the current port/queues first.
            If it is a Standard TCP/IP Port, Configure Port and update its address only after confirming the printer.
            A shared port affects all queues using it. For WSD/IPP/shared queues, use Brother's installer or administrator.
            Print a Windows test page manually. This app never changes queues or sends print jobs.

            Recovery: try current {input.Current} and proposed {input.Candidate} only on your own LAN.
            If neither works, read the panel/report or router lease list for the actual active address.
            Restore known-good DHCP/Auto using the printer's supported panel if appropriate.
            Do not factory reset, change the PC subnet/firewall, or disable certificate checks.
            """;
        return new ReviewedPlan(input, adapter, text);
    }

    public static bool ValidMac(string text)
    {
        var separator = text.Contains(':') ? ':' : '-';
        var parts = text.Split(separator);
        return parts.Length == 6 && parts.All(p => p.Length == 2 && p.All(Uri.IsHexDigit)) &&
               byte.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var first) &&
               (first & 1) == 0 && parts.Any(p => p != "00");
    }
}
