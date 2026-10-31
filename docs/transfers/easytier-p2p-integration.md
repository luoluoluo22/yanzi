# EasyTier P2P-only + KCP direct file delivery (opt-in)

This transport integrates with the existing `files.temporary.plan`, `publish`, and `receive` capabilities. It is **disabled by default**. No Cloudflare R2, SFTP or public upload is used implicitly if a P2P transfer fails.

## Verified design

- A session started via `files.p2p.connect` owns an independent `easytier-core` child process with `--p2p-only true --need-p2p true --enable-kcp-proxy true --no-tun true --bind-device true`.
- EasyTier's `--socks5` listener is protected by a temporary Windows inbound-block firewall rule. If firewall setup fails, session startup fails closed. The rule is removed by `files.p2p.stop`.
- The process is stopped when Yanzi exits normally. No system proxy, route, browser or existing EasyTier process is modified by the module.
- A peer is accepted only if EasyTier CLI shows its expected hostname, `cost=p2p` and `tunnel_proto=udp`. This is rechecked after file delivery.
- Publisher starts an ephemeral HTTP file endpoint protected by a 256-bit unguessable bearer secret, expires the receipt in 90 minutes and never exposes an arbitrary filesystem browsing API. Receipts contain no local source path or public file URL.
- Receiver uses localhost SOCKS5, downloads to an isolated `.part` file, verifies length and SHA-256, then atomically moves into the inbox without overwriting existing files.
- For P2P receives, the `.p2p-<receipt-hash>.part` name is stable across retries. HTTP Range / 206 downloads only the missing suffix. On transient loss a nonempty incomplete part is preserved; the same receipt can be passed to `files.temporary.receive` again after direct connectivity returns. Invalid completed parts are removed rather than accepted.
- Stop terminates only the Yanzi-owned process and invalidates local shares. There is no automatic public relay fallback.
- Download deadline is 45 minutes for direct P2P (other providers keep their existing limits).
- **Important:** sender and receiver must remain online with running Yanzi for the live transfer. The existing cloud channels are separate opt-in paths for asynchronous delivery.

## Per-device setup

Download the signed/checksummed EasyTier v2.6.4 Windows CLI build from the official project on **each** machine; keep binaries outside Git. Verify the release SHA256. Do not re-use an unrelated EasyTier instance because this module requires a process it owns.

Store a shared random network secret in a **user-private text file on each machine**, never in source code, Git, receipts or logs. Protect the file with Windows ACLs. Each device must use the same `networkName` and `networkSecretFile` content but **different** `localIpv4`.

In each device's `%LOCALAPPDATA%\OpenQuickHost\temporary-transfer\easytier.json`:

```json
{
  "corePath": "C:\\Tools\\EasyTier\\easytier-core.exe",
  "networkName": "yanzi-direct-private",
  "networkSecretFile": "C:\\Users\\YOUR_USER\\AppData\\Local\\OpenQuickHost\\private\\easytier-secret.txt",
  "localIpv4": "10.244.36.1",
  "coordinationPeers": [
    "tcp://YOUR_COORDINATION_HOST:11010"
  ],
  "listenerPort": 23332,
  "rpcPort": 15992,
  "socksPort": 19089
}
```

The office device uses `10.244.36.2`. These addresses are examples; choose an overlay subnet not used by either physical LAN. Coordination nodes must be trusted and permitted by your local network policy. They are not authorized to relay file payloads under `p2p-only`.

The `%LOCALAPPDATA%\OpenQuickHost\temporary-transfer\channels.json` array supports an entry of this form (the other channels can be left absent):

```json
[
  {
    "id": "easytier-p2p",
    "enabled": true,
    "p2pCliPath": "C:\\Tools\\EasyTier\\easytier-cli.exe",
    "p2pPeerName": "CHINAMI-3IF988E",
    "p2pRemoteIp": "10.244.36.2",
    "p2pRpcPort": 15992,
    "p2pSocksPort": 19089,
    "p2pListenPort": 39421
  }
]
```

On the office device, use `p2pPeerName=PC-202409131508` and `p2pRemoteIp=10.244.36.1`. The HTTP listen port must match both devices' channel profiles. Do not confuse this with EasyTier's UDP listener port.

## Invoking in Yanzi

1. `files.p2p.connect` on **both** devices; it starts an owned, no-relay EasyTier process. The call only means the process started; it does **not** confirm a direct link.
2. Repeat `files.p2p.status` until `connected=true`, `transport=p2p/udp`. Initial NAT punching may take tens of seconds.
3. Call `files.temporary.publish` on the sending machine with `source=home`, `destination=office`, `preferredChannel=easytier-p2p`, `allowPublicUpload=false`, `backupCount=0`. Keep the source file and sender running.
4. Pass the returned opaque receipt privately to the other device, then call `files.temporary.receive` there. The receiver validates exact length and SHA-256 before exposing the file.
5. Call `files.p2p.stop` on both devices to release the P2P session, local file shares and firewall exception.

If the direct link disappears, the adapter **fails** instead of silently using a cloud or SFTP path. An operator must explicitly initiate a different channel if needed.

## Network prerequisites and security

The current home/office pair is behind CGNAT/symmetric NAT. Prior isolated tests showed EasyTier's UDP P2P link can work without public IPv4 and KCP materially improves throughput. However, Clash Meta/Mihomo TUN may interfere with coordination and NAT discovery. This module never silently changes routes: configure routing/Clash DIRECT exceptions for the chosen coordination/STUN addresses as a separate, audited operation.

An administrator-capable process may be necessary to create the local-only SOCKS5 firewall rule. If permissions are insufficient, the module reports `p2p_socks5_inbound_firewall_required` and refuses to start.

**Not yet a full background transfer manager:** The adapter supports receipt-bound resume while the sender stays online, but not sender-restart recovery, indefinite retention of partial files, automatic reconnect loops inside one invocation, adaptive multipart scheduling, automatic installation/update of EasyTier binaries, mobile clients, or asynchronous/offline delivery. Receipts expire after 90 minutes. Keep the feature disabled by default until independent two-device QA and the installed-host integration gate pass.

## Validation

Run `scripts/verify-easytier-p2p-integration.ps1`. The isolated verification harness uses a mock EasyTier CLI plus a local SOCKS5 test proxy; it exercises authentication, fail-closed selection, Range resumption after simulated disconnect, rejection of corrupt parts, length and SHA-256 checking and owned-session lifetime without modifying production routes or services. This is distinct from the earlier real two-device EasyTier throughput tests.
