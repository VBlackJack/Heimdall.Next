# Network Discovery Implementation Research

Target: raw TCP/UDP socket implementation in .NET 10 / WPF, no Nmap/libpcap/libpcap wrappers.

## 1. SMB2 anonymous enumeration

### SMB2 NEGOTIATE Response layout

SMB2 sits after a 4-byte NetBIOS Session Service header on TCP/445. The SMB2 header is always 64 bytes. Offsets below are:

- `BodyOff`: offset inside the SMB2 NEGOTIATE response body
- `PktOff`: offset from the start of the SMB2 header
- `TcpOff`: offset from the start of the TCP payload including the 4-byte NetBIOS header

| Field | Size | BodyOff | PktOff | TcpOff | Notes |
|---|---:|---:|---:|---:|---|
| `StructureSize` | 2 | `0x00` | `0x40` | `0x44` | Must be `0x0041` (65) |
| `SecurityMode` | 2 | `0x02` | `0x42` | `0x46` | Signing flags |
| `DialectRevision` | 2 | `0x04` | `0x44` | `0x48` | Max common SMB version selected by server |
| `NegotiateContextCount` or `Reserved` | 2 | `0x06` | `0x46` | `0x4A` | Count only for SMB `0x0311`, else reserved |
| `ServerGuid` | 16 | `0x08` | `0x48` | `0x4C` | Stable host identifier, useful for dedup/dual-homed detection |
| `Capabilities` | 4 | `0x18` | `0x58` | `0x5C` | DFS, leasing, multichannel, encryption, etc. |
| `MaxTransactSize` | 4 | `0x1C` | `0x5C` | `0x60` | Upper bound for transacted requests |
| `MaxReadSize` | 4 | `0x20` | `0x60` | `0x64` | Server max read payload |
| `MaxWriteSize` | 4 | `0x24` | `0x64` | `0x68` | Server max write payload |
| `SystemTime` | 8 | `0x28` | `0x68` | `0x6C` | FILETIME UTC |
| `ServerStartTime` | 8 | `0x30` | `0x70` | `0x74` | FILETIME UTC |
| `SecurityBufferOffset` | 2 | `0x38` | `0x78` | `0x7C` | Absolute offset from SMB2 header start, not body start |
| `SecurityBufferLength` | 2 | `0x3A` | `0x7A` | `0x7E` | Length of SPNEGO/GSS security blob |
| `NegotiateContextOffset` or `Reserved2` | 4 | `0x3C` | `0x7C` | `0x80` | Absolute offset for SMB `0x0311`, else reserved |
| `Buffer` | variable | `0x40` | `0x80` | `0x84` | Security blob starts here if offset points here |

### Exact enums to hardcode

`SecurityMode` bitmask:

- `0x0001` = signing enabled
- `0x0002` = signing required

`Capabilities` bitmask:

- `0x00000001` = DFS
- `0x00000002` = Leasing
- `0x00000004` = Large MTU
- `0x00000008` = Multi-channel
- `0x00000010` = Persistent Handles
- `0x00000020` = Directory Leasing
- `0x00000040` = Encryption

`DialectRevision` values:

- `0x0202` = SMB 2.0.2
- `0x0210` = SMB 2.1
- `0x0300` = SMB 3.0
- `0x0302` = SMB 3.0.2
- `0x0311` = SMB 3.1.1

### What you can extract from NEGOTIATE alone

- `ServerGuid`: strong device identity, often stable across IPs/reboots
- max SMB dialect supported: if your request offered all dialects, returned `DialectRevision` is the highest common version
- signing policy:
  - `(SecurityMode & 0x0002) != 0` -> signing mandatory
  - `(SecurityMode & 0x0001) != 0 && (SecurityMode & 0x0002) == 0` -> signing supported but optional
- server feature set from `Capabilities`
- throughput hints from `MaxReadSize`/`MaxWriteSize`/`MaxTransactSize`
- clock skew and uptime proxy from `SystemTime` and `ServerStartTime`
- SPNEGO mech list from `SecurityBuffer` even before session setup

### SMB 3.1.1 negotiate contexts

Only valid when `DialectRevision == 0x0311`.

- `NegotiateContextCount` at body `0x06`
- `NegotiateContextOffset` at body `0x3C`
- Context header layout at `NegotiateContextOffset`:
  - `ContextType` `uint16`
  - `DataLength` `uint16`
  - `Reserved` `uint32`
  - `Data`
  - 8-byte alignment between contexts

Useful context types to parse if present:

- `0x0001` = Preauth Integrity Capabilities
- `0x0002` = Encryption Capabilities
- `0x0003` = Compression Capabilities
- `0x0005` = Netname Negotiate Context ID
- `0x0008` = Signing Capabilities

### SMB signing requirement detection

Hard rule:

- `SecurityMode == 0x0003` -> signing enabled and required
- `SecurityMode == 0x0001` -> signing enabled, not required
- `SecurityMode == 0x0000` -> uncommon/legacy, signing not advertised

### SMBv1 fallback availability

Not inferable from SMB2 NEGOTIATE alone. Probe separately on a new TCP connection:

1. Send SMB1 `SMB_COM_NEGOTIATE` with only SMB1 dialects, e.g. `NT LM 0.12`.
2. If the first 4 bytes after the NetBIOS header are `FF 53 4D 42` and the negotiate succeeds, SMB1 is enabled.
3. If the connection resets, times out, or does not select an SMB1 dialect, SMB1 is not available.

Do not offer SMB2 dialect strings in this probe if the goal is strict SMB1 availability.

## 2. HTTP fingerprinting beyond `Server`

### Default page/content hashing

Hash normalized bodies, not raw bodies, or CSRF tokens and hostnames will explode cardinality.

Recommended GET order:

1. `/`
2. redirect target from `/` if `30x`
3. `/login`
4. a guaranteed-miss path like `/this-path-should-not-exist-<rand>` for 404 fingerprinting
5. product probes only after generic pass

Normalization before hashing:

- decompress gzip/deflate/br
- lowercase HTML tag names
- remove `<script>` and `<style>` blocks for framework-only fingerprinting
- collapse whitespace: `\s+ -> " "`
- replace volatile values:
  - IPv4: `\b\d{1,3}(?:\.\d{1,3}){3}\b -> <ip>`
  - MAC: `\b[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5}\b -> <mac>`
  - long hex/base64 tokens: `\b[A-Fa-f0-9]{16,}\b|\b[A-Za-z0-9+/_=-]{24,}\b -> <tok>`
  - dates/timestamps: ISO8601 and RFC1123 -> `<time>`

Hash both:

- `SHA-256` of normalized UTF-8 body
- `MMH3` of normalized body if you want short integer fingerprints like favicon work

### Cookie names worth hardcoding

| Cookie name | Strongest inference |
|---|---|
| `PHPSESSID` | PHP |
| `ASP.NET_SessionId` | classic ASP.NET / IIS app |
| `__RequestVerificationToken` | ASP.NET anti-CSRF |
| `JSESSIONID` | Java servlet container (Tomcat/Jetty/Spring on servlet stack) |
| `connect.sid` | Node.js Express |
| `SESSION` | Spring Session / Spring Boot common default |
| `laravel_session` + `XSRF-TOKEN` | Laravel |
| `csrftoken` + `sessionid` | Django |
| `sysauth` / `sysauth_https` | LuCI / OpenWrt |

Treat cookies as evidence, not proof.

### Error page signatures

Regexes to hardcode against `404` and `500` pages:

- ASP.NET: `(?i)Server Error in '/' Application\\.|<title>Runtime Error</title>`
- ASP.NET WebForms: `(?i)__VIEWSTATE|WebResource\\.axd`
- Tomcat: `(?i)Apache Tomcat(?:/\\d+(?:\\.\\d+)*)? - Error report`
- Spring Boot: `(?i)Whitelabel Error Page|\\{\"timestamp\":.*\"status\":\\d+,\"error\":`
- Jetty: `(?i)Powered by Jetty|<title>Error \\d+ .*Jetty`
- nginx/openresty: `(?i)<center>nginx(?:/[0-9.]+)?</center>|openresty`
- lighttpd: `(?i)lighttpd`
- Caddy: `(?i)^404 page not found$`

### HTTP method support fingerprinting

Send both:

- `OPTIONS / HTTP/1.1`
- `OPTIONS * HTTP/1.1`

Parse:

- `Allow`
- `Public`
- `DAV`
- `MS-Author-Via`
- `Access-Control-Allow-Methods`

High-signal cases:

- `DAV: 1,2` or `MS-Author-Via: DAV` -> WebDAV stack
- `Allow` contains `TRACE` -> common on older IIS/Apache defaults
- `Allow` contains `PROPFIND`, `MKCOL`, `LOCK`, `UNLOCK` -> WebDAV appliance/UI
- only `GET, HEAD, OPTIONS` -> many embedded/lightweight UIs

### Product-confirming URL paths

High-confidence field markers:

| Path | Signal |
|---|---|
| `/cgi-bin/luci` | OpenWrt / LuCI |
| `/luci-static/` | OpenWrt static assets |
| `/webfig/` | MikroTik WebFig |
| `/webman/index.cgi` | Synology DSM |
| `/webapi/entry.cgi` | Synology DSM API |
| `/cgi-bin/authLogin.cgi` | QNAP QTS auth endpoint |
| `/ui/` | ESXi Host Client, some OPNsense builds, many appliance SPAs; combine with title/body |
| `/+CSCOE+/logon.html` | Cisco ASA/AnyConnect portal |
| `/admin/public/index.html` | Cisco ASA/ASDM web admin |
| `/remote/login` | FortiGate SSL-VPN / portal family |
| `/api/v2/monitor/system/status` | FortiGate REST API |
| `/php/login.php` | Palo Alto PAN-OS |
| `/ISAPI/System/deviceInfo` | Hikvision |
| `/RPC2_Login` | Dahua |
| `/cgi-bin/magicBox.cgi?action=getSystemInfo` | Dahua |
| `/redfish/v1/Managers/iDRAC.Embedded.1` | Dell iDRAC |
| `/rest/v1/Managers/1` | HPE iLO older REST |
| `/sdk` | ESXi / vCenter SOAP endpoint |

## 3. TCP stack fingerprinting without raw capture

### What you cannot get from a normal `Socket`

- remote TCP receive window: `Socket.ReceiveBufferSize` is your local socket buffer only
- MSS from SYN/SYN-ACK options
- TCP timestamps option
- window scale / SACK-permitted / ECN bits
- peer initial TTL on TCP responses

Those require packet capture, raw sockets, ETW/eBPF, or OS-specific TCP info APIs not exposed as plain cross-platform .NET sockets.

### What you can still infer

| Probe | What you learn |
|---|---|
| `ConnectAsync()` to closed port | immediate `ConnectionRefused` -> host reachable, port closed |
| `ConnectAsync()` timeout | filtered path or host down |
| send protocol preface, then read | application-specific reject style: banner, graceful close, RST |
| `Shutdown(SocketShutdown.Send)` then read until EOF | whether peer half-close semantics are graceful or abortive |
| invalid HTTP/TLS/SSH preface | some appliances RST immediately, others send protocol error text |

### Reset/close behavior

Useful but weak heuristics:

- `recv == 0` -> peer sent FIN/graceful close
- `SocketError.ConnectionReset` -> peer or middlebox sent RST
- immediate RST after malformed first request often fingerprints appliance web/SSH stacks, not the OS TCP stack

Do not treat close style as a primary OS fingerprint. It is app-controlled too often.

## 4. SNMP deep dive

### High-value OIDs that often answer on `public`

| OID | Name | Why it matters |
|---|---|---|
| `1.3.6.1.2.1.1.2.0` | `sysObjectID` | vendor subtree, best first pivot |
| `1.3.6.1.2.1.1.7.0` | `sysServices` | layer bitmask, quick router/server/printer hint |
| `1.3.6.1.2.1.25.3.2.1.2.<idx>` | `hrDeviceType` | host resources device type |
| `1.3.6.1.2.1.25.3.2.1.3.<idx>` | `hrDeviceDescr` | often contains model + revision + serial |
| `1.3.6.1.2.1.25.3.2.1.5.<idx>` | `hrDeviceStatus` | state |
| `1.3.6.1.2.1.25.2.3.1.3.<idx>` | `hrStorageDescr` | flash, RAM, volumes |
| `1.3.6.1.2.1.2.2.1.2.<ifIndex>` | `ifDescr` | interface label/vendor text |
| `1.3.6.1.2.1.31.1.1.1.1.<ifIndex>` | `ifName` | canonical interface name |
| `1.3.6.1.2.1.31.1.1.1.15.<ifIndex>` | `ifHighSpeed` | Mbps for modern ports |
| `1.3.6.1.2.1.31.1.1.1.18.<ifIndex>` | `ifAlias` | admin-entered uplink/room labels |
| `1.3.6.1.2.1.17.1.1.0` | `dot1dBaseBridgeAddress` | bridge MAC, strong switch/AP clue |
| `1.3.6.1.2.1.17.4.3.1.1.<mac>` | `dot1dTpFdbAddress` | learned MAC table entries |
| `1.3.6.1.2.1.17.4.3.1.2.<mac>` | `dot1dTpFdbPort` | FDB port mapping |
| `1.3.6.1.2.1.4.22.1.2.<ifIndex>.<ip>` | `ipNetToMediaPhysAddress` | ARP table / neighbors |
| `1.3.6.1.2.1.47.1.1.1.1.13.<entIdx>` | `entPhysicalModelName` | often the cleanest chassis model string |
| `1.3.6.1.2.1.47.1.1.1.1.10.<entIdx>` | `entPhysicalSoftwareRev` | firmware version |
| `1.3.6.1.2.1.47.1.1.1.1.11.<entIdx>` | `entPhysicalSerialNum` | serial number |

### Parsing `sysObjectID`

Algorithm:

1. Match prefix `1.3.6.1.4.1.`.
2. First arc after that is the IANA Private Enterprise Number (PEN).
3. Everything after the PEN is vendor-specific model/product branch.

Regex:

- `^1\\.3\\.6\\.1\\.4\\.1\\.(\\d+)(?:\\.(.*))?$`

Verified PENs from IANA useful for this domain:

| PEN | Vendor |
|---:|---|
| `9` | Cisco |
| `11` | Hewlett-Packard |
| `311` | Microsoft |
| `872` | AVM GmbH |
| `1038` | Sagemcom |
| `1368` | Orange |
| `4526` | Netgear |
| `6574` | Synology |
| `11863` | TP-Link |
| `12356` | Fortinet |
| `14988` | MikroTik |
| `24681` | QNAP |
| `25461` | Palo Alto Networks |
| `37496` | Zhejiang Dahua |
| `39165` | Hikvision |
| `41112` | Ubiquiti Networks |
| `55062` | QNAP Systems |

The PEN only gives you vendor. Model resolution requires vendor MIB mapping of the remaining suffix.

### Raw BER/ASN.1 notes for SNMP walk code

Tags you need:

- `0x30` sequence
- `0x02` integer
- `0x04` octet string
- `0x05` null
- `0x06` object identifier
- `0xA1` GetNextRequest-PDU
- `0xA2` Response-PDU
- `0xA5` GetBulkRequest-PDU

Walk strategy:

1. Try SNMPv2c `GetBulk` first for table roots.
2. `nonRepeaters = 0`, `maxRepetitions = 10` to start.
3. Stop when returned OID leaves the requested prefix, repeats the prior OID, or returns `endOfMibView`.
4. If response size approaches MTU or device starts timing out, drop `maxRepetitions` to `5`.
5. Fall back to `GetNext` for SNMPv1-only devices.

Good roots to bulk walk:

- `1.3.6.1.2.1.2.2.1` (`ifTable`)
- `1.3.6.1.2.1.31.1.1.1` (`ifXTable`)
- `1.3.6.1.2.1.17.4.3.1` (bridge FDB)
- `1.3.6.1.2.1.43.11.1.1` (printer supplies)

### Printer-specific OIDs

`Printer-MIB` root is `1.3.6.1.2.1.43`.

| OID | Name | Use |
|---|---|---|
| `1.3.6.1.2.1.43.5.1.1.16.<hrDeviceIndex>` | `prtGeneralPrinterName` | admin-friendly printer name |
| `1.3.6.1.2.1.43.5.1.1.17.<hrDeviceIndex>` | `prtGeneralSerialNumber` | serial number |
| `1.3.6.1.2.1.25.3.2.1.3.<idx>` | `hrDeviceDescr` | model/revision string |
| `1.3.6.1.2.1.43.11.1.1.6.<hrDeviceIndex>.<supplyIndex>` | `prtMarkerSuppliesDescription` | toner/drum/waste container label |
| `1.3.6.1.2.1.43.11.1.1.8.<hrDeviceIndex>.<supplyIndex>` | `prtMarkerSuppliesMaxCapacity` | max level |
| `1.3.6.1.2.1.43.11.1.1.9.<hrDeviceIndex>.<supplyIndex>` | `prtMarkerSuppliesLevel` | current level |

`prtMarkerSuppliesTable` index is `{ hrDeviceIndex, prtMarkerSuppliesIndex }`.

### UPS-specific OIDs

`UPS-MIB` root is `1.3.6.1.2.1.33`.

| OID | Name |
|---|---|
| `1.3.6.1.2.1.33.1.1.1.0` | `upsIdentManufacturer` |
| `1.3.6.1.2.1.33.1.1.2.0` | `upsIdentModel` |
| `1.3.6.1.2.1.33.1.1.3.0` | `upsIdentUPSSoftwareVersion` |
| `1.3.6.1.2.1.33.1.1.5.0` | `upsIdentName` |
| `1.3.6.1.2.1.33.1.2.1.0` | `upsBatteryStatus` (`1` unknown, `2` normal, `3` low, `4` depleted) |
| `1.3.6.1.2.1.33.1.2.3.0` | `upsEstimatedMinutesRemaining` |
| `1.3.6.1.2.1.33.1.2.4.0` | `upsEstimatedChargeRemaining` |
| `1.3.6.1.2.1.33.1.2.5.0` | `upsBatteryVoltage` |
| `1.3.6.1.2.1.33.1.4.4.1.2.<line>` | `upsOutputVoltage` |
| `1.3.6.1.2.1.33.1.4.4.1.3.<line>` | `upsOutputCurrent` |
| `1.3.6.1.2.1.33.1.4.4.1.4.<line>` | `upsOutputPower` |
| `1.3.6.1.2.1.33.1.4.4.1.5.<line>` | `upsOutputPercentLoad` |
| `1.3.6.1.2.1.33.1.6.1.0` | `upsAlarmsPresent` |

## 5. mDNS / Bonjour deep service discovery

### Query sequence

1. Send `PTR _services._dns-sd._udp.local.` to enumerate service types.
2. For each returned service type, send `PTR <service>.<domain>` to enumerate instances.
3. For each returned instance name, send `TXT` and `SRV` queries for the full instance FQDN.
4. Resolve `A`/`AAAA` for the `SRV` target host.

DNS-SD model:

- PTR name: `<service>.<domain>`
- PTR rdata: `<instance>.<service>.<domain>`
- SRV/TXT name: same full instance name

TXT parsing rules:

- TXT RDATA is a sequence of length-prefixed strings
- split each string on the first `=`
- keys are case-insensitive for matching

### High-value service types and TXT keys

Observed/high-value keys:

| Service type | TXT keys worth parsing | What they reveal |
|---|---|---|
| `_device-info._tcp.local` | `model` | Apple hardware model, e.g. `MacBookPro18,1` |
| `_googlecast._tcp.local` | `fn`, `md`, `id`, `ve`, `ca`, `ic` | Chromecast/Google Home friendly name, model, device id |
| `_ipp._tcp.local` / `_ipps._tcp.local` | `ty`, `product`, `note`, `UUID`, `adminurl`, `rp`, `pdl`, `TLS` | printer make/model, location, admin URL, protocol support |
| `_airplay._tcp.local` | `model`, `deviceid`, `features`, `srcvers` | Apple TV / AirPlay-capable devices |
| `_http._tcp.local` | product-specific TXT varies | generic appliance web UI |

The Apple printer TXT keys above are directly specified by the Bonjour Printing spec.

### Implementation notes

- multicast destination: `224.0.0.251:5353` and IPv6 `ff02::fb:5353`
- send multiple questions in one packet to save RTTs
- respect record TTLs; do not rescan hot services aggressively
- cache unsolicited announcements from the answer section

## 6. UPnP / SSDP / WSD deep enumeration

### SSDP follow-up after `M-SEARCH`

Parse from each response:

- `LOCATION`
- `SERVER`
- `ST`
- `USN`
- `CACHE-CONTROL`

Then fetch `LOCATION` and parse `rootDesc.xml`.

### `rootDesc.xml` nodes to extract

Typical structure:

```xml
<root xmlns="urn:schemas-upnp-org:device-1-0">
  <specVersion><major>1</major><minor>0</minor></specVersion>
  <URLBase>http://host:port/</URLBase>
  <device>
    <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
    <friendlyName>...</friendlyName>
    <manufacturer>...</manufacturer>
    <manufacturerURL>...</manufacturerURL>
    <modelDescription>...</modelDescription>
    <modelName>...</modelName>
    <modelNumber>...</modelNumber>
    <serialNumber>...</serialNumber>
    <UDN>uuid:...</UDN>
    <presentationURL>/</presentationURL>
    <serviceList>...</serviceList>
    <deviceList>...</deviceList>
  </device>
</root>
```

Most useful fields:

- `deviceType`
- `friendlyName`
- `manufacturer`
- `modelDescription`
- `modelName`
- `modelNumber`
- `serialNumber`
- `UDN`
- `presentationURL`

### Service discovery from `serviceList`

Each service entry contains:

```xml
<service>
  <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
  <serviceId>urn:upnp-org:serviceId:WANIPConn1</serviceId>
  <controlURL>/upnp/control/WANIPConn1</controlURL>
  <eventSubURL>/upnp/event/WANIPConn1</eventSubURL>
  <SCPDURL>/wanipconnSCPD.xml</SCPDURL>
</service>
```

Fetch each `SCPDURL` and parse:

- action names from `actionList/action/name`
- state variables from `serviceStateTable/stateVariable/name`

High-value gateway service types:

- `urn:schemas-upnp-org:device:InternetGatewayDevice:1`
- `urn:schemas-upnp-org:service:WANIPConnection:1`
- `urn:schemas-upnp-org:service:WANPPPConnection:1`
- `urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1`
- `urn:schemas-upnp-org:service:Layer3Forwarding:1`
- `urn:schemas-upnp-org:service:WLANConfiguration:1`

### WSD / WS-Discovery on UDP 3702

Send SOAP 1.2 `Probe` to:

- IPv4 multicast `239.255.255.250:3702`
- IPv6 multicast `ff02::c:3702`

Minimal probe:

```xml
<soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"
               xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
               xmlns:wsd="http://schemas.xmlsoap.org/ws/2005/04/discovery">
  <soap:Header>
    <wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>
    <wsa:MessageID>uuid:PUT-GUID-HERE</wsa:MessageID>
    <wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>
  </soap:Header>
  <soap:Body>
    <wsd:Probe />
  </soap:Body>
</soap:Envelope>
```

Parse from `ProbeMatch`:

- `EndpointReference/Address`
- `Types`
- `XAddrs`
- `MetadataVersion`

Then fetch metadata from `XAddrs` and extract manufacturer/model/friendly-name equivalents.

## 7. Certificate-based identification patterns

### Extract these fields every time

- subject DN
- issuer DN
- SAN DNS/IP entries
- `NotBefore` / `NotAfter`
- self-signed boolean: `subject == issuer`
- key type and size
- signature algorithm
- SHA-256 SPKI fingerprint

High-value SAN regexes:

- `(?i)fritz\\.box|myfritz`
- `(?i)mafreebox\\.freebox\\.fr|freebox`
- `(?i)livebox|orange|sagemcom`
- `(?i)synology|diskstation`
- `(?i)qnap|qts`
- `(?i)idrac`
- `(?i)ilo|hpe|hewlett[ -]?packard`
- `(?i)vmware|esxi|vcenter`

### Common appliance/self-signed heuristics

These are useful, but they are heuristics, not protocol guarantees:

| Pattern | Use |
|---|---|
| `Subject == Issuer` | local appliance self-signed certificate |
| validity around `3650`/`3652` days | very common appliance default cert |
| SAN includes private IP, `.local`, `.lan`, `.home` | local-only management UI |
| issuer `O` matches vendor name | often stronger than CN for appliance certs |

### Vendor strings worth matching

Observed vendor strings to match in subject/issuer `CN`, `O`, `OU`, and SANs:

- `AVM`, `fritz.box`
- `Freebox`, `mafreebox.freebox.fr`
- `Orange`, `Livebox`, `Sagemcom`
- `Hikvision`
- `Dahua`
- `Synology`, `DiskStation`
- `QNAP`
- `Dell`, `iDRAC`
- `HPE`, `Hewlett Packard Enterprise`, `iLO`
- `VMware`, `ESXi`, `vCenter`

## 8. Network topology inference

### TTL-based hop estimation

Only reliable where the API exposes received TTL, e.g. ICMP `PingReply.Options.Ttl`.

Use nearest standard initial TTL bucket:

- observed `<= 32` -> guess `32`
- observed `33..64` -> guess `64`
- observed `65..128` -> guess `128`
- observed `129..255` -> guess `255`

Estimated hop count:

- `guessedInitialTtl - observedTtl`

Do not use TTL as a unique OS fingerprint by itself.

### TCP traceroute with decreasing TTL

Not practical with a normal managed TCP connect socket. Setting outgoing socket TTL is possible, but ICMP `Time Exceeded` is not surfaced back through a regular `Socket` in a portable/useful way.

Under your constraints:

- ICMP traceroute: yes
- TCP SYN traceroute without raw packet capture: effectively no

### NAT / load balancer / dual-homed heuristics

NAT or LB suspicion on one public IP:

- different services on the same IP return different TTL classes
- different TLS certs / SSH host keys / SMB `ServerGuid`s / HTTP normalized hashes on different ports

Dual-homed host suspicion across multiple IPs:

- same SMB `ServerGuid`
- same SSH host key
- same TLS SPKI fingerprint
- same SNMP serial/model and hostname
- same mDNS instance/UUID

These are heuristic until corroborated by at least one stable identifier.

## 9. Passive discovery opportunities

### What is and is not possible with raw TCP/UDP sockets

- ARP / gratuitous ARP: not available through plain TCP/UDP sockets; needs L2 capture or raw Ethernet support
- VRRP: not available through plain UDP sockets; it is IP protocol `112` to `224.0.0.18`
- HSRP: yes, listen on UDP `1985` multicast `224.0.0.2`
- mDNS / SSDP / LLMNR / NBNS / NetBIOS datagrams / DHCP broadcasts: yes, plain UDP listeners work on the local L2 domain

### NetBIOS browser and announcements

Listen on UDP `138`. High-value marker in payload:

- mailslot name `\\MAILSLOT\\BROWSE`

Useful browser opcodes seen in browse mailslot traffic:

- `0x01` = HostAnnouncement
- `0x02` = AnnouncementRequest
- `0x08` = Election
- `0x0F` = LocalMasterAnnouncement

Extract:

- host/computer name
- workgroup/domain
- server type bitmask
- browser protocol version

### LLMNR / NBNS

Listen on:

- UDP `5355` for LLMNR
- UDP `137` for NBNS

NBNS name suffixes worth decoding:

- `<00>` workstation/service name
- `<20>` file server
- `<1B>` domain master browser
- `<1D>` local master browser
- `<1E>` browser elections/group

Cache both question and answer traffic. Questions alone often reveal hostname intent before a host is actively scanned.

### DHCP observation

Listen on UDP `67`/`68` and parse BOOTP options after magic cookie `63 82 53 63`.

High-value options:

- `12` = Host Name
- `50` = Requested IP Address
- `53` = DHCP Message Type
- `55` = Parameter Request List
- `60` = Vendor Class Identifier
- `61` = Client Identifier
- `77` = User Class
- `81` = FQDN
- `93` = Client System Architecture
- `94` = Client Network Interface Identifier
- `97` = Client Machine Identifier / UUID

Limitations:

- only same broadcast domain
- renewals are often unicast and invisible
- binding `67`/`68` may conflict with the local DHCP client/service

### HSRP passive identification

Listen on UDP `1985` and parse:

- version
- opcode: `0` hello, `1` coup, `2` resign
- state
- hello time
- hold time
- priority
- group
- virtual IP

This gives you active/standby gateway evidence and the virtual router IP without any active probe.

## 10. Verified favicon hash seed list

These were computed from official project/vendor-owned assets using Shodan-compatible MurmurHash3 over base64-encoded icon bytes. This is a seed list, not a complete appliance database.

| Product | Asset path used | Hash |
|---|---|---:|
| pfSense | `src/usr/local/www/favicon.ico` | `1405460984` |
| OPNsense | `src/opnsense/www/themes/opnsense/build/images/favicon.png` | `-1068289244` |
| Grafana | `public/img/fav32.png` | `2123863676` |
| Jenkins | `war/src/main/webapp/favicon.ico` | `81586312` |
| GitLab | `app/assets/images/favicon.png` | `1265477436` |
| Pi-hole | `img/favicons/favicon.ico` | `-1711538164` |
| Home Assistant | `public/static/icons/favicon.ico` | `1303369757` |
| Portainer | `app/assets/ico/favicon.ico` | `712297537` |
| Proxmox VE | `www/images/favicon.ico` | `2017541283` |

Practical rule: store `hash + source path + product version/commit` because favicon hashes do change across UI redesigns.

## Sources

- [MS-SMB2 / SMB2 processing and negotiate structures](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/)
- [RFC 3418 - SNMPv2-MIB](https://www.rfc-editor.org/rfc/rfc3418.txt)
- [RFC 2790 - Host Resources MIB](https://www.rfc-editor.org/rfc/rfc2790.txt)
- [RFC 1213 - MIB-II](https://www.rfc-editor.org/rfc/rfc1213.txt)
- [RFC 2863 - Interfaces Group MIB](https://www.rfc-editor.org/rfc/rfc2863.txt)
- [RFC 1493 - Bridge MIB](https://www.rfc-editor.org/rfc/rfc1493.txt)
- [RFC 3805 - Printer MIB v2](https://www.rfc-editor.org/rfc/rfc3805.txt)
- [RFC 1628 - UPS MIB](https://www.rfc-editor.org/rfc/rfc1628.txt)
- [RFC 6762 - Multicast DNS](https://www.rfc-editor.org/rfc/rfc6762.txt)
- [RFC 6763 - DNS-Based Service Discovery](https://www.rfc-editor.org/rfc/rfc6763.txt)
- [Apple Bonjour Printing Specification](https://developer.apple.com/bonjour/printing-specification/)
- [IANA Private Enterprise Numbers](https://www.iana.org/assignments/enterprise-numbers/enterprise-numbers)
