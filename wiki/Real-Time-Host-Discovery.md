# Real-Time LAN Host Discovery Engine

> [!WARNING]
> **LEGACY COMPATIBILITY REFERENCE**
> This document describes legacy compatibility code. The discovery module discovers Sunshine/GameStream hosts, but this code is classified as **Incompatible** and is not used by production Moonshine roles. Moonshine is its own platform with its own protocol (MNBP v1), defined in `docs/PROTOCOL_SPEC_V1.md`.

The Moonshine Host Discovery subsystem provides instantaneous, zero-configuration local network discovery of active Sunshine and NVIDIA GameStream hosts. It operates without external dependencies by combining custom, zero-allocation Multicast DNS (mDNS) parsers, Simple Service Discovery Protocol (SSDP / UPnP) broadcast scanners, and asynchronous HTTP/HTTPS `/serverinfo` XML probes.

## 1. Network Discovery Architecture

The discovery engine runs concurrent socket listeners across all network interfaces, continuously maintaining a thread-safe registry of active hosts with automated TTL expiry.

```
+-----------------------------------------------------------------------------------------+
|                                MOONSHINE DISCOVERY ENGINE                               |
+-----------------------------------------------------------------------------------------+
|                                                                                         |
|   +-----------------------+     +-----------------------+     +---------------------+   |
|   |   mDNS Socket         |     |   SSDP Socket         |     |   Direct IP Probe   |   |
|   |   UDP 224.0.0.251:5353|     |   UDP 239.255.255.250 |     |   HTTP Port 47989   |   |
|   +-----------+-----------+     +-----------+-----------+     |   HTTPS Port 47984  |   |
|               |                             |                 +----------+----------+   |
|               v                             v                            |              |
|   +-----------------------+     +-----------------------+                |              |
|   | Custom MdnsCodec      |     | Custom SsdpCodec      |                |              |
|   | (RFC 6762 / RFC 1035) |     | (HTTP/1.1 M-SEARCH)   |                |              |
|   +-----------+-----------+     +-----------+-----------+                |              |
|               |                             |                            |              |
|               +-----------------------------+----------------------------+              |
|                                             |                                           |
|                                             v                                           |
|                              +------------------------------+                           |
|                              | Asynchronous ServerInfo Probe|                           |
|                              +--------------+---------------+                           |
|                                             |                                           |
|                                             v                                           |
|                              +------------------------------+                           |
|                              | Custom ServerInfoCodec (XML) |                           |
|                              +--------------+---------------+                           |
|                                             |                                           |
|                                             v                                           |
|                              +------------------------------+                           |
|                              | Concurrent Host Registry     |                           |
|                              | (Thread-Safe + TTL Eviction) |                           |
|                              +------------------------------+                           |
+-----------------------------------------------------------------------------------------+
```

## 2. Multicast DNS Protocol Specification (RFC 6762)

Sunshine broadcasts its presence on the local network using the service name `_nvstream._tcp.local`.

### Query Construction
To discover hosts, Moonshine constructs a standard 12-byte DNS header followed by length-prefixed domain labels:
- **Transaction ID**: `0x0000`
- **Flags**: `0x0000` (Standard Query)
- **Questions**: `1`
- **QNAME**: `\x09_nvstream\x04_tcp\x05local\x00`
- **QTYPE**: `0x000C` (PTR - Pointer Record)
- **QCLASS**: `0x0001` (IN - Internet Class)

### Response Parsing with Compression Pointer Resolution
mDNS response packets contain PTR, SRV, A, and TXT resource records. DNS domain name compression pointers (`0xC000` mask) are resolved recursively with jump counters to prevent cyclical malformed packet exploits.

| Record Type | QTYPE Code | Extracted Metadata |
| :--- | :--- | :--- |
| **PTR** | `12` (`0x000C`) | Service Instance Name |
| **SRV** | `33` (`0x0021`) | Target Hostname and Service Port (default 47989) |
| **A** | `1` (`0x0001`) | IPv4 Address (`192.168.x.x`) |
| **TXT** | `16` (`0x0010`) | Key-Value Attributes (`model=Sunshine`, `version=0.23.1`) |

## 3. Simple Service Discovery Protocol (SSDP / UPnP)

In addition to mDNS, Moonshine emits SSDP `M-SEARCH` broadcast packets over UDP to `239.255.255.250:48010` (custom GameStream port) and `239.255.255.250:1900` (standard UPnP port).

### Search Request Structure
```http
M-SEARCH * HTTP/1.1
HOST: 239.255.255.250:48010
MAN: "ssdp:discover"
ST: urn:schemas-upnp-org:device:MediaServer:1
MX: 2

```

### Response Parsing
Incoming `HTTP/1.1 200 OK` and `NOTIFY` datagrams are parsed using zero-copy ASCII string spans to extract:
- **`LOCATION`**: Target URL containing host IP and HTTP port (`http://192.168.1.50:47989/serverinfo`).
- **`ST` / `NT`**: Service Type verification.
- **`USN`**: Unique Service Name / UUID.
- **`SERVER`**: Host software banner (`Sunshine/0.23.1`).
- **`CACHE-CONTROL`**: Maximum advertisement lifespan (`max-age=1800`).

## 4. ServerInfo XML Extraction (`/serverinfo`)

Once an IP address is detected via mDNS or SSDP, the engine triggers an asynchronous HTTP GET request to `http://<host>:47989/serverinfo` (falling back to HTTPS port `47984` if HTTP is disabled on host).

### ServerInfo Schema Fields
The custom `ServerInfoCodec` extracts all metadata fields:

| Field Name | Type | Description |
| :--- | :--- | :--- |
| **`hostname`** | String | Human-readable machine name |
| **`LocalIP` / `ExternalIP`** | String | Host internal and public IP addresses |
| **`HttpPort` / `HttpsPort`** | Integer | HTTP and HTTPS communication ports |
| **`PairStatus`** | Boolean | Pairing state (`1` = Paired, `0` = Unpaired) |
| **`appversion`** | String | Sunshine / GeForce Experience host version |
| **`gputype`** | String | Active GPU device name (e.g. NVIDIA RTX 4090) |
| **`currentgame`** | String | Currently running active game title (if any) |
| **`uniqueid`** | String | Persistent host unique identifier (UUID) |
| **`ServerCodecModeSupport`** | Bitmask | Bit 0: H.264, Bit 1: HEVC, Bit 2: AV1, Bit 3: HEVC Main10 |
| **`SupportedDisplayModes`** | List | Supported resolutions and refresh rates (e.g. 4K 144Hz) |

## 5. Live Continuous Registry & Event Lifecycle

The `LiveHostDiscoveryEngine` maintains thread-safe state and fires events as hosts enter, update, or leave the network:

1. **`HostDiscovered`**: Fired when a previously unseen host responds to discovery probes.
2. **`HostUpdated`**: Fired when an existing host changes state (e.g. game launched, pairing status changed).
3. **`HostOffline`**: Fired when a host fails to respond within the configurable TTL timeout window (default 10s).

## 6. Zero-Allocation Discipline

- All binary DNS headers and domain labels are written into pre-allocated memory buffers (`Span<byte>`).
- Socket receive loops reuse pre-allocated 2048-byte pinned memory buffers without allocating per datagram.
- SSDP headers and HTTP status codes are matched directly over UTF-8 / ASCII byte spans.
