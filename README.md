# Brother Network Manager

A native Windows desktop helper for **Brother DCP-T820DW wired Ethernet** discovery and guided stable-IP setup. Independent software, **not affiliated with Brother**.

**It plans; it does not apply network changes.** Review a same-subnet address, copy/export the plan, then explicitly open the printer in your default browser. Authenticate directly to Brother's interface and manually apply supported settings. A router DHCP reservation is usually the safer alternative and is explained separately.

## Download and run

Download `BrotherNetworkManager-win-x64.zip` and `SHA256SUMS.txt` from [Releases](https://github.com/itsnavin/brother-printer-network-manager/releases). Extract the **whole ZIP** to a folder and run `BrotherNetworkManager.exe`. Windows 11 x64 recommended; Windows 10 x64 requires an OS edition still receiving security updates. ARM64/x86 native packages are not provided.

The ZIP includes .NET 8 and Windows Desktop runtime; no separate runtime, installer or administrator privilege is required. Keep all extracted files together. The executable is **unsigned**; this project does not claim publisher verification or recommend bypassing SmartScreen. Inspect the public source and build locally if your policies require it.

```powershell
Get-FileHash .\BrotherNetworkManager-win-x64.zip -Algorithm SHA256
.\BrotherNetworkManager.exe --demo
```

`--demo` uses synthetic fixtures only, simulates clipboard/export/browser/probes, and never reads your adapters or printer registry. Normal startup reads only local adapter metadata; it does **not** contact a printer. No live LAN/printer operations were performed during development.

## Workflow

1. Print a private network report: **Menu > Print Reports > OK > Network Config > OK > Mono Start or Colour Start**. If the IP is `0.0.0.0`, wait one minute and retry. Confirm the physical model, current wired IP, mask, gateway, BOOT Method and wired MAC. The report can contain a management password: do not upload it.
2. Deliberately choose the PC's LAN adapter. Optionally read installed Windows queues and Standard TCP/IP port IPs, or **always enter the printer IP manually** from its report.
3. Choose **router DHCP reservation** (recommended) or manual printer static TCP/IP. Enter a candidate; none is automatically selected or reserved. Mask/gateway initially reflect the PC and are editable **suggestions**, not confirmed printer/router values. Check your router DHCP pool, leases, reservations and address ownership yourself.
4. Validate and review current/proposed values and recovery steps. Copy or export plain text only if needed. Changing any field/adapter invalidates the review.
5. For a reservation, use your router's trusted UI and the **active wired MAC**, leaving printer BOOT Method DHCP/Auto. For static configuration, explicitly open current printer settings, authenticate there, and look for the active wired TCP/IP controls. **If offered**, choose BOOT Method Static and enter the reviewed values. If unavailable/unclear, stop and use Brother support or reservation; the exact web screen has not been verified on hardware.
6. Manually submit only after checking everything. Reconnect explicitly to the proposed address, print another report and verify interface/MAC/IP. Optionally check one current/proposed local IP with explicit ownership confirmation. Reachability is **not** model/configuration verification.
7. Update a Windows Standard TCP/IP printer port manually only after recording its old configuration and all queues sharing it. Printer properties > Ports > Configure Port. Shared ports affect multiple queues; WSD/IPP/shared queues may require Brother's installer or an administrator. Send a test page manually.

## Discovery and safety boundaries

| Capability | What actually happens |
| --- | --- |
| Local discovery | On explicit click, reads installed queue names, drivers and literal private IPv4 addresses from Windows Standard TCP/IP port registry entries, read-only. All queues are shown, not guessed to be Brother devices. |
| Identification | Source is local Windows configuration only; labels may be stale or user-edited. **No physical model verification**. No MAC vendor guessing. |
| Missing devices | WSD, IPP, shared, USB, hostname-only ports and uninstalled printers may be absent. Use manual report entry. No SNMP, mDNS, WS-Discovery or LAN sweep is implemented. |
| Optional check | Explicit ownership checkbox plus explicit click sends **one ICMP echo** to the current/proposed RFC1918 IPv4 on the selected adapter's subnet; 1.2-second packet timeout, 3-second overall bound, cancellation, no retries. Windows routing determines the egress adapter; on VPN/overlapping networks consult your administrator. |
| Address validity | Strict dotted-decimal IPv4; contiguous masks /1-/30; usable same-subnet hosts only. Rejects selected-PC addresses, all listed PC address collisions, gateway collisions, network/broadcast, public/multicast/loopback/link-local and /31-/32. No remote Internet targets. |
| Availability | **No ping reply never proves an address is free.** A reply means something answered, not that it is a Brother printer. Router pool/ownership remain unknown to the app. |
| Browser links | Only strict literal, private, same-subnet printer IPv4 URLs, HTTPS by default. No advertisement URLs, description fetching, embedded login, automatic opening or password attempts. |
| HTTP | Explicit optional choice with a second warning before opening; unencrypted LAN credentials may be exposed. No automatic fallback/downgrade or TLS bypass. |
| Changes | No OS, adapter, firewall, router, printer, queue or port changes, firmware downloads, print jobs, or SNMP SET. Opening a URL never means success. |

This release intentionally uses local queue discovery rather than active network discovery: it works without unsafe broad scans or unverified protocol/model inference. Nonresponsive/isolated devices can be missed. Routed printers, nonprivate/link-local networks and IPv6 planning are out of scope.

DNS is not edited by this version. Preserve existing DNS unless Brother's interface and your administrator require a specific change. Gateway may be left blank for an isolated LAN only if the printer supports that configuration; verify how its interface represents "none."

## Wi-Fi and recovery

The DCP-T820DW **cannot use wired and wireless LAN at the same time**. WiFi > Network I/F > OK > Wired LAN or WLAN(Wi-Fi) > OK changes the active interface. Switching can drop Ethernet and jobs. Follow Brother's panel setup wizard for WLAN; the app has no SSID/password field. Wired and wireless MACs may differ: re-read the active interface report and plan/reserve separately.

If the new address is unreachable, try old and new addresses explicitly, read the panel/report and router lease table, and restore known-good printer DHCP/Auto through its supported panel if appropriate. Do not factory-reset as a first step, change the PC subnet/firewall, or disable certificate checks.

The default management password is on the machine's **Pwd** label or private network report; use your changed password if you have one. Never assume a generic or empty password. Prefer HTTPS when supported and verify the certificate/device using Brother's guidance, rather than globally ignoring warnings.

## Official model-specific evidence

Checked **2026-10-24**, English Malaysia/Philippines regional pages, each explicitly including DCP-T820DW. The model manual listing dates Online User's Guide Version A to 2023-01-25. Sources are linked, not redistributed.

| Source | Confirmed / limitation |
| --- | --- |
| [Web Based Management FAQ](https://support.brother.com/g/b/faqend.aspx?c=my&lang=en&prod=dcpt820dw_all&faqid=faq00100217_008) | Browser access, direct authentication, Pwd label/report; FAQ shows HTTP access, not proof that every device has HTTPS configured. |
| [Network report FAQ](https://support.brother.com/g/b/faqend.aspx?c=my&lang=en&prod=dcpt820dw_all&faqid=faq00000027_525) | Exact DCP panel report sequence, IP/mask/MAC report fields, wait/retry for 0.0.0.0. |
| [Interface switching FAQ](https://support.brother.com/g/b/faqend.aspx?c=ph&lang=en&prod=dcpt820dw_all&faqid=faq00100029_024) | DCP-T820DW network I/F sequence; wired and WLAN cannot coexist. |
| [SSL/TLS management](https://support.brother.com/g/s/id/htmldoc/mfc/cv_dcp520/ase/html/GUID-D18B1036-D519-4A40-8E0D-741D174C3DD6_1.html) | Model guide documents HTTPS management and says HTTPS is enabled by default; actual device configuration/certificate still needs verification. |
| [Model manuals](https://support.brother.com/g/b/manualtop.aspx?c=my&lang=en&prod=dcpt820dw_all), [Online guide](https://support.brother.com/g/s/id/htmldoc/mfc/cv_dcp520/ase/index.html?c=my&lang=en&prod=dcpt820dw_all) | Settings Tables (1 Line LCD) confirms Menu > Network > Wired LAN > TCP/IP with BOOT Method/IP/Mask/Gateway/DNS. The guide also documents SSL/TLS management/certificates. **Exact web Static menu/options were not established**, so app guidance is conditional and has a stop path, not an invented Apply API. |
| [Official downloads](https://support.brother.com/g/b/downloadtop.aspx?c=my&lang=en&prod=dcpt820dw_all) | Select your actual region and Windows version for drivers/BRAdmin if needed. No proprietary Brother binaries bundled. |

## Build and verification

Requires Windows x64, PowerShell and the .NET 8 SDK (latest serviced patch). No third-party application packages or test framework dependencies.

```powershell
dotnet run --project .\tests\Core.Tests\Core.Tests.csproj -c Release
dotnet run --project .\src\App\App.csproj -- --demo
.\scripts\Package.ps1
```

`Package.ps1` runs offline core assertions, publishes self-contained win-x64, runs the published native GUI's `--smoke-test`, includes exact runtime package licenses, checks required files, and creates ZIP/SHA256SUMS in `artifacts`. `--smoke-test` forces offline demo, exercises the rendered wizard/manual entry/review/export/link actions with mocks, and exits nonzero on failure. No tests contact printers or the LAN; transport tests cover authorization, target policy, reply ambiguity, timeout and cancellation via mocks. CI runs the same script on Windows; [Actions](https://github.com/itsnavin/brother-printer-network-manager/actions) offers a downloadable artifact.

Tests cover strict IPv4/URI parsing, RFC1918 boundaries, all contiguous masks /1-/30, malformed masks, /23-/30 boundaries, self/gateway collisions, MAC validation, queue parsing, immutable review, manual entry, stale-review invalidation and simulated browser/export. No live protocol discovery parser exists to validate.

**Hardware limitation:** actual Brother login, firmware-specific static web controls, certificate configuration, IP application, physical model discovery, router changes and real print/reconnect behavior have **not** been tested on a physical DCP-T820DW. This is a guided helper, not a guarantee of device compatibility.

.NET 8 support ends November 2026. Keep runtime distributions updated and migrate to a supported SDK/runtime before that date. Downloading an old self-contained ZIP does not update its bundled runtime.

## Privacy, license and support

No telemetry/cloud calls, logs, automatic update checks, credentials or persisted settings. Normal mode reads local network metadata and explicitly requested queue data. Only an explicit check sends ICMP; only explicit browser actions launch external network access. Plans live in memory until explicit copy/export. Clipboard and exports contain private IP/MAC/adapter names: keep them local and redact before sharing. Never include passwords, reports or real LAN identifiers in public issues.

MIT license for the app; exact Microsoft runtime license/notices ship under `runtime-licenses`. No proprietary Brother software is redistributed. File reproducible, redacted app issues in this repository; contact Brother for printer/firmware support.
