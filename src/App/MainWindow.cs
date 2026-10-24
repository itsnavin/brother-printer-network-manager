using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrotherNetwork.Core;
using Microsoft.Win32;

namespace BrotherNetwork.App;

internal sealed class MainWindow : Window
{
    private readonly bool demo;
    private readonly bool smoke;
    private readonly ComboBox adapters = new() { MinWidth = 500 };
    private readonly ListBox queues = new() { Height = 140 };
    private readonly TextBlock adapterSummary = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox current = new();
    private readonly TextBox candidate = new();
    private readonly TextBox mask = new();
    private readonly TextBox gateway = new();
    private readonly TextBox mac = new();
    private readonly CheckBox reservation = new() { Content = "Router DHCP reservation (recommended)", IsChecked = true };
    private readonly CheckBox confirmed = new() { Content = "I checked the printer report, wired interface and router DHCP pool / ownership." };
    private readonly CheckBox authorized = new() { Content = "I own/administer the selected private subnet and authorize this ONE target check." };
    private readonly CheckBox allowHttp = new() { Content = "Use HTTP explicitly (unencrypted; only if needed on a trusted LAN)" };
    private readonly TextBox review = new() { IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 330 };
    private readonly TabControl tabs = new();
    private readonly List<Button> planActions = [];
    private readonly Dictionary<string, Button> buttons = [];
    private ReviewedPlan? reviewed;
    private CancellationTokenSource? probeCancellation;
    private string? mockExport;
    private Uri? mockOpened;
    private int mockProbeCalls;

    internal MainWindow(bool demo, bool smoke)
    {
        this.demo = demo;
        this.smoke = smoke;
        Title = "Brother Network Manager | DCP-T820DW" + (demo ? " | OFFLINE DEMO" : "");
        Width = 1080; Height = 850; MinWidth = 850; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(225, 235, 248))));
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.MidnightBlue));
        buttonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(170, 190, 215))));
        buttonStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        Resources.Add(typeof(Button), buttonStyle);
        foreach (var option in new[] { reservation, confirmed, authorized, allowHttp })
        {
            option.Content = new TextBlock { Text = option.Content.ToString(), TextWrapping = TextWrapping.Wrap };
            option.Margin = new Thickness(0, 8, 0, 12);
            option.VerticalContentAlignment = VerticalAlignment.Center;
        }
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "A stable address. A clear plan.", FontSize = 30,
            FontWeight = FontWeights.SemiBold, Foreground = Brushes.MidnightBlue });
        heading.Children.Add(Paragraph("Brother DCP-T820DW  /  Wired Ethernet first  /  Unofficial, not affiliated with Brother"));
        heading.Children.Add(Paragraph(demo
            ? "OFFLINE DEMO - synthetic addresses only. Browser, clipboard, export and probes are simulated."
            : "Read-only planning. No passwords, automatic changes, background scans or telemetry."));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new Border { Background = Brushes.White, Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0), Child = status };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(tabs); Content = root;
        AddTab("1  Find & record", BuildFind());
        AddTab("2  Plan", BuildPlan());
        AddTab("3  Review & open", BuildReview());
        AddTab("4  Reconnect", BuildReconnect());
        AddTab("Wi-Fi & sources", BuildSources());
        foreach (var field in new[] { current, candidate, mask, gateway, mac })
            field.TextChanged += (_, _) => InvalidateReview();
        reservation.Checked += (_, _) => InvalidateReview();
        reservation.Unchecked += (_, _) => InvalidateReview();
        confirmed.Checked += (_, _) => InvalidateReview();
        confirmed.Unchecked += (_, _) => InvalidateReview();
        adapters.SelectionChanged += (_, _) => AdapterChanged();
        Closing += (_, _) => probeCancellation?.Cancel();
        LoadAdapters();
        SetStatus("Start with a fresh printer report. Manual IP entry is always available; no device has been contacted.");
    }

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12)
    };

    private static StackPanel Section(string title, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Paragraph(text));
        return panel;
    }

    private void AddTab(string name, StackPanel content) => tabs.Items.Add(new TabItem
    {
        Header = name, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
    });

    private Button Action(StackPanel panel, string title, Action action, bool requiresReview = false)
    {
        var button = new Button { Content = title, HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 5, 0, 9) };
        button.Click += (_, _) => Guard(action);
        buttons.Add(title, button);
        panel.Children.Add(button);
        if (requiresReview) { planActions.Add(button); button.IsEnabled = false; }
        return button;
    }

    private static void Field(StackPanel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        box.Padding = new Thickness(8); box.MaxLength = 64;
        box.HorizontalAlignment = HorizontalAlignment.Left; box.Width = 420;
        panel.Children.Add(box);
    }

    private StackPanel BuildFind()
    {
        var panel = Section("Find your printer, without guessing",
            "On the DCP-T820DW: Menu > Print Reports > OK > Network Config > OK > Mono Start or Colour Start. " +
            "If the report says 0.0.0.0, wait one minute and print it again. Keep the report private: it can contain a management password.");
        panel.Children.Add(Paragraph("Select your PC's connected LAN adapter. Only private same-subnet IPv4 targets are supported. " +
            "VPNs, multiple adapters and routed/VLAN printers may need your administrator."));
        panel.Children.Add(adapters);
        adapterSummary.TextWrapping = TextWrapping.Wrap; panel.Children.Add(adapterSummary);
        Action(panel, "Refresh local adapters (no probes)", LoadAdapters);
        panel.Children.Add(Paragraph("Optional discovery: read installed Windows queues and Standard TCP/IP port addresses locally. " +
            "This does not contact devices and is NOT proof of model or presence. WSD/IPP/USB/hostname-only ports are not discovered. " +
            "No LAN sweep is implemented; use the report/manual entry when a printer is missing."));
        panel.Children.Add(queues);
        Action(panel, "Read installed TCP/IP printer queues", LoadQueues);
        Action(panel, "Use selected queue address (confirm against report)", () =>
        {
            if (queues.SelectedItem is not QueueEvidence evidence) throw new ArgumentException("Select a queue first.");
            current.Text = evidence.Address;
            SetStatus("Copied local queue IP. Confirm its physical model, wired MAC and address against the report.");
        });
        Field(panel, "Current printer IPv4 address - enter manually from the report", current);
        panel.Children.Add(Paragraph("Record the wired MAC in Plan. Also keep the current mask, gateway and BOOT Method privately " +
            "for recovery. No network credentials belong in this app."));
        Action(panel, "Continue to plan", () => tabs.SelectedIndex = 1);
        return panel;
    }

    private StackPanel BuildPlan()
    {
        var panel = Section("Choose how the address stays stable",
            "A router DHCP reservation is often safer: it manages address ownership centrally while the printer stays on DHCP/Auto. " +
            "It is NOT the same as setting Static on the printer. Neither method is applied by this app.");
        panel.Children.Add(reservation);
        Field(panel, "Proposed stable printer IPv4 - no address is automatically suggested", candidate);
        Field(panel, "Proposed subnet mask - initially inferred from selected PC; verify", mask);
        Field(panel, "Proposed gateway - initially inferred from PC; verify (blank = none)", gateway);
        Field(panel, "Wired interface MAC from printer report (required for reservation)", mac);
        panel.Children.Add(Paragraph("DNS is intentionally not configured in this version. Preserve existing DNS unless your " +
            "administrator and Brother's interface require a change. Do not copy the PC's DNS blindly."));
        panel.Children.Add(Paragraph(Planner.Caution));
        panel.Children.Add(confirmed);
        Action(panel, "Validate and create review (does not apply)", CreateReview);
        return panel;
    }

    private StackPanel BuildReview()
    {
        var panel = Section("Review before touching any settings",
            "The review is invalidated whenever you edit a plan field or change adapters. Opening a browser is not applying an IP.");
        panel.Children.Add(review);
        Action(panel, "Copy reviewed plan (no credentials)", CopyPlan, true);
        Action(panel, "Export reviewed plan as text...", ExportPlan, true);
        panel.Children.Add(Paragraph("Authenticate only in your external browser. Use the password you set, or check the physical " +
            "'Pwd' label / private report; never assume a generic password. HTTPS is preferred when supported. Verify the device " +
            "identity and certificate using Brother's certificate guidance. This app does not ignore TLS errors, auto-login or auto-downgrade."));
        panel.Children.Add(allowHttp);
        Action(panel, "Open CURRENT printer settings in browser", () => OpenPrinter(false), true);
        panel.Children.Add(Paragraph("For a reservation, use your router's own trusted management UI separately; " +
            "this app never guesses or opens a router address. For Static, use the active wired TCP/IP controls offered by your firmware. " +
            "If the BOOT Method Static option is not available or the page differs, stop and consult Brother. No private endpoints or SNMP SET are used."));
        return panel;
    }

    private StackPanel BuildReconnect()
    {
        var panel = Section("Reconnect and verify manually",
            "After you manually apply the router or printer change, read a fresh report to confirm the new IP and active wired MAC. " +
            "Only then open the proposed address. This app cannot certify the IP was set.");
        Action(panel, "Open PROPOSED printer address in browser", () => OpenPrinter(true), true);
        panel.Children.Add(Paragraph("Optional reachability: one ICMP request to one selected current or proposed address; " +
            "1.2 second packet timeout / 3 second overall bound; cancellable. No model identification, port scan or automatic repeats. " +
            "A reply at a proposed address is a conflict unless you have independently confirmed it is this printer."));
        panel.Children.Add(authorized);
        Action(panel, "Check current IP once", () => _ = CheckTarget(false), true);
        Action(panel, "Check proposed IP once", () => _ = CheckTarget(true), true);
        Action(panel, "Cancel reachability check", () => probeCancellation?.Cancel());
        panel.Children.Add(Paragraph("Windows: Printer properties > Ports. First record the old configuration and all queues sharing " +
            "the port. For an existing Standard TCP/IP Port, Configure Port lets you manually change the IP after verifying the device. " +
            "Do not change a shared port without considering all its queues. WSD/IPP/shared printers need Brother's installer or administrator. " +
            "Print a test page manually. Nothing here modifies Windows printing."));
        panel.Children.Add(Paragraph("Recovery: check both old and proposed IPs, then the printer panel/report and router leases. " +
            "If needed, restore the printer's known-good DHCP/Auto via its supported panel. Do not factory reset as a first step, " +
            "change the PC's subnet/firewall, or disable TLS verification."));
        return panel;
    }

    private StackPanel BuildSources()
    {
        var panel = Section("Wi-Fi is a separate interface",
            "DCP-T820DW cannot use wired LAN and WLAN together. Switching can disconnect Ethernet and interrupt jobs. " +
            "Use WiFi > Network I/F > OK > Wired LAN or WLAN(Wi-Fi) > OK. For Wi-Fi setup use Brother's panel setup wizard/manual. " +
            "Do not enter your Wi-Fi password here. Wired and WLAN MAC addresses may differ: re-read the active report and create a separate plan/reservation.");
        panel.Children.Add(Paragraph("Official sources checked 2026-10-24, English Malaysia/Philippines model pages. " +
            "The online guide is Version A (model manual listing: 2023-01-25). Panel TCP/IP fields and Web Based Management access " +
            "are documented; an exact web Static screen/path has NOT been established on this printer. Firmware/region may differ."));
        AddSource(panel, "Brother: Web Based Management / password guidance",
            "https://support.brother.com/g/b/faqend.aspx?c=my&lang=en&prod=dcpt820dw_all&faqid=faq00100217_008");
        AddSource(panel, "Brother: print Network Configuration Report",
            "https://support.brother.com/g/b/faqend.aspx?c=my&lang=en&prod=dcpt820dw_all&faqid=faq00000027_525");
        AddSource(panel, "Brother: DCP-T820DW wired / WLAN switching",
            "https://support.brother.com/g/b/faqend.aspx?c=ph&lang=en&prod=dcpt820dw_all&faqid=faq00100029_024");
        AddSource(panel, "Brother: model manuals (TCP/IP settings and certificate guidance)",
            "https://support.brother.com/g/b/manualtop.aspx?c=my&lang=en&prod=dcpt820dw_all");
        AddSource(panel, "Brother: HTTPS management and certificate prerequisites",
            "https://support.brother.com/g/s/id/htmldoc/mfc/cv_dcp520/ase/html/GUID-D18B1036-D519-4A40-8E0D-741D174C3DD6_1.html");
        AddSource(panel, "Brother: official downloads / BRAdmin (choose your region and Windows version)",
            "https://support.brother.com/g/b/downloadtop.aspx?c=my&lang=en&prod=dcpt820dw_all");
        panel.Children.Add(Paragraph("Support: this is an independent MIT-licensed utility. No Brother binaries, firmware, cloud API, " +
            "analytics, credential storage or automatic printing. Settings live only in memory; explicit clipboard/export may expose " +
            "network identifiers to other local apps. Review before sharing. No physical-printer change has been validated by this project."));
        return panel;
    }

    private void AddSource(StackPanel panel, string label, string url) =>
        Action(panel, label, () => Launch(new Uri(url)));

    private AdapterInfo SelectedAdapter() => adapters.SelectedItem as AdapterInfo ??
        throw new ArgumentException("Select a connected IPv4 adapter first.");

    private void LoadAdapters()
    {
        Guard(() =>
        {
            adapters.ItemsSource = demo
                ? new List<AdapterInfo> { new("Demo Ethernet (synthetic)", "192.168.50.10", "255.255.255.0", "192.168.50.1") }
                : LocalInventory.Adapters();
            adapters.SelectedIndex = -1;
            adapterSummary.Text = "No adapter selected. Choose deliberately; PC data is not confirmed printer data.";
            InvalidateReview();
        });
    }

    private void AdapterChanged()
    {
        InvalidateReview();
        authorized.IsChecked = false;
        if (adapters.SelectedItem is not AdapterInfo adapter) return;
        mask.Text = adapter.Mask; gateway.Text = adapter.Gateway;
        adapterSummary.Text = $"PC IPv4: {adapter.Address} | mask: {adapter.Mask} | PC gateway: {adapter.Gateway}\n" +
            "These are local PC observations, NOT verified printer/router settings. Plan supports only this same private subnet.";
    }

    private void LoadQueues()
    {
        queues.ItemsSource = demo
            ? new List<QueueEvidence> { new("Demo DCP-T820DW queue", "Synthetic model label", "DemoPort", "192.168.50.20") }
            : LocalInventory.Queues();
        SetStatus($"Read {queues.Items.Count} local IPv4 TCP/IP queue(s). No devices contacted; labels are not trusted identification.");
    }

    private void InvalidateReview()
    {
        probeCancellation?.Cancel();
        reviewed = null; review.Text = "No valid review. Complete Plan and select Validate.";
        foreach (var action in planActions) action.IsEnabled = false;
    }

    private void CreateReview()
    {
        InvalidateReview();
        if (confirmed.IsChecked != true) throw new ArgumentException("Check the report / DHCP pool confirmation before reviewing.");
        var selfAddresses = adapters.Items.Cast<AdapterInfo>().Select(a => a.Address).ToHashSet();
        if (selfAddresses.Contains(current.Text.Trim()) || selfAddresses.Contains(candidate.Text.Trim()) ||
            selfAddresses.Contains(gateway.Text.Trim()))
            throw new ArgumentException("Printer and router addresses must not equal any listed local PC IPv4 address.");
        reviewed = Planner.Review(new PlanInput(current.Text.Trim(), candidate.Text.Trim(), mask.Text.Trim(),
            gateway.Text.Trim(), mac.Text.Trim(), reservation.IsChecked == true), SelectedAdapter());
        review.Text = reviewed.Text;
        foreach (var action in planActions) action.IsEnabled = true;
        tabs.SelectedIndex = 2;
        SetStatus("Plan validated and reviewed, NOT applied. Confirm real address ownership before changing anything.");
    }

    private ReviewedPlan RequireReview() => reviewed ?? throw new ArgumentException("Create a fresh valid review first.");

    private void CopyPlan()
    {
        var text = RequireReview().Text;
        if (demo) mockExport = text; else Clipboard.SetText(text);
        SetStatus(demo ? "Demo: copy simulated." : "Reviewed plan copied. Clipboard may be readable by other local applications.");
    }

    private void ExportPlan()
    {
        var text = RequireReview().Text;
        if (demo) { mockExport = text; SetStatus("Demo: export simulated; no file written."); return; }
        var dialog = new SaveFileDialog { Filter = "Text plan (*.txt)|*.txt", FileName = "printer-network-plan.txt" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, text);
        SetStatus("Text plan exported to the selected file. It contains private network identifiers, not credentials.");
    }

    private void OpenPrinter(bool proposed)
    {
        var plan = RequireReview();
        var uri = IPv4.PrinterUri(proposed ? plan.Input.Candidate : plan.Input.Current,
            plan.Adapter, allowHttp.IsChecked == true);
        if (!demo && allowHttp.IsChecked == true && MessageBox.Show(this,
                "HTTP is unencrypted. Management credentials may be exposed on the LAN. Continue only if required and trusted?",
                "Explicit HTTP choice", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Launch(uri);
        SetStatus(demo ? $"Demo: simulated browser open to {uri}. No connection made." :
            $"Browser requested for {uri}. No settings applied or verified by this app.");
    }

    private void Launch(Uri uri)
    {
        if (demo) { mockOpened = uri; return; }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async Task CheckTarget(bool proposed)
    {
        if (probeCancellation is not null)
        {
            SetStatus("A check is running; cancel it or wait.");
            return;
        }
        try
        {
            var plan = RequireReview();
            var target = proposed ? plan.Input.Candidate : plan.Input.Current;
            probeCancellation = new CancellationTokenSource();
            IReachability transport = demo ? new DemoReachability(() => mockProbeCalls++) : new IcmpReachability();
            SetStatus("Checking one explicitly selected target...");
            var result = await Probe.CheckAsync(target, plan.Adapter, authorized.IsChecked == true,
                transport, probeCancellation.Token);
            SetStatus($"{target}: {result}");
        }
        catch (OperationCanceledException) { SetStatus("Check cancelled. No reachability or identity conclusion."); }
        catch (Exception ex) when (ex is ArgumentException or PingException or TimeoutException or InvalidOperationException)
        { SetStatus($"Check failed: {ex.Message} No address availability or identity conclusion."); }
        finally { probeCancellation?.Dispose(); probeCancellation = null; }
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                                   SecurityException or Win32Exception or InvalidOperationException or
                                   NetworkInformationException or System.Runtime.InteropServices.ExternalException)
        { SetStatus($"Operation failed: {ex.Message}"); }
    }

    private void SetStatus(string text) => status.Text = text;

    internal async Task SmokeTest()
    {
        if (!smoke || !demo) throw new InvalidOperationException("Smoke test must be offline.");
        void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Click(string title) => buttons[title].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(reviewed is null && mockOpened is null && mockProbeCalls == 0 && adapters.SelectedIndex == -1,
            "Startup must not contact devices or select an adapter.");
        Assert(tabs.Items.Count == 5 && IsVisible, "Native window and wizard tabs must render.");
        adapters.SelectedIndex = 0;
        current.Text = "192.168.50.20"; candidate.Text = "192.168.50.40";
        mac.Text = "02:00:00:00:00:01"; confirmed.IsChecked = true;
        Click("Validate and create review (does not apply)");
        Assert(reviewed is not null && planActions.All(b => b.IsEnabled), "Manual-entry review failed.");
        Click("Copy reviewed plan (no credentials)"); Assert(mockExport == reviewed!.Text, "Copy must use reviewed values.");
        Click("Export reviewed plan as text..."); Assert(mockExport!.Contains("NOT applied"), "Export must not claim success.");
        Click("Open CURRENT printer settings in browser"); Assert(mockOpened!.AbsoluteUri == "https://192.168.50.20/", "Safe current HTTPS link failed.");
        Click("Open PROPOSED printer address in browser"); Assert(mockOpened!.AbsoluteUri == "https://192.168.50.40/", "Proposed HTTPS link failed.");
        await CheckTarget(false); Assert(mockProbeCalls == 0, "Probe must require explicit ownership confirmation.");
        authorized.IsChecked = true; await CheckTarget(true);
        Assert(mockProbeCalls == 1 && status.Text.Contains("does NOT prove"), "Mock probe result must be honest.");
        candidate.Text = "192.168.50.1";
        Assert(reviewed is null && planActions.All(b => !b.IsEnabled), "Edits must invalidate review.");
        Guard(CreateReview); Assert(reviewed is null && status.Text.StartsWith("Operation failed:"), "Bad plan must visibly fail.");
        Click("Read installed TCP/IP printer queues"); Assert(queues.Items.Count == 1, "Demo queue discovery missing.");
        Assert(mockProbeCalls == 1, "Queue discovery must not probe.");
    }

    private sealed class DemoReachability(Action onCall) : IReachability
    {
        public Task<bool> SendAsync(string address, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); onCall(); return Task.FromResult(false); }
    }
}
