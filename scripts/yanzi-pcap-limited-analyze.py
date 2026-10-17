#!/usr/bin/env python3
"""Offline analysis of a bounded PCAPdroid capture for Yanzi's own app.

No TLS interception, certificate installation, body decoding, or network upload.
Extract IP, transport, DNS query and TLS SNI if exposed in a single captured
packet. Raw packets never leave the user's machine.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import struct
import subprocess
import time

CAP_MAX = 2 * 1024 * 1024
APP_PCAP_NAME = "yanzi-app-20261017-limited.pcap"
DNS_LIMIT = 32
def read_dns_qname(data):
    if len(data) < 13:
        return None
    try:
        qdcount = struct.unpack_from("!H", data, 4)[0]
        if qdcount < 1:
            return None
        offset = 12
        labels = []
        for _ in range(24):
            if offset >= len(data):
                return None
            size = data[offset]
            offset += 1
            if size == 0:
                return ".".join(labels).lower() if labels else None
            if size > 63 or offset + size > len(data):
                return None
            label = data[offset:offset+size].decode("ascii")
            if not label or any(ch not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_"
                                for ch in label):
                return None
            labels.append(label)
            offset += size
    except (UnicodeDecodeError, ValueError, struct.error):
        return None
    return None

def tls_sni(payload):
    """Best effort single-record ClientHello parser; fragmented TLS unsupported."""
    if len(payload) < 43 or payload[0] != 0x16 or payload[1] != 0x03:
        return None
    length = int.from_bytes(payload[3:5], "big")
    record = payload[5:5+length]
    if len(record) < 42 or record[0] != 1:
        return None
    try:
        pos = 4 + 2 + 32
        sid_len = record[pos]
        pos += 1 + sid_len
        cipher_len = int.from_bytes(record[pos:pos+2], "big")
        pos += 2 + cipher_len
        comp_len = record[pos]
        pos += 1 + comp_len
        total = int.from_bytes(record[pos:pos+2], "big")
        pos += 2
        end = min(len(record), pos + total)
        while pos + 4 <= end:
            typ = int.from_bytes(record[pos:pos+2], "big")
            size = int.from_bytes(record[pos+2:pos+4], "big")
            pos += 4
            if pos + size > len(record):
                return None
            if typ == 0 and size >= 5:
                sni = record[pos:pos+size]
                count = int.from_bytes(sni[0:2], "big")
                if count+2 > len(sni) or sni[2] != 0:
                    return None
                n = int.from_bytes(sni[3:5], "big")
                host = sni[5:5+n].decode("ascii").lower()
                if 0 < len(host) <= 250 and all(c.isalnum() or c in ".-_" for c in host):
                    return host
                return None
            pos += size
    except (IndexError, UnicodeDecodeError, ValueError):
        return None
    return None

def packets(path):
    with path.open("rb") as f:
        header = f.read(24)
        if len(header) != 24:
            raise ValueError("Invalid PCAP header")
        magic = header[:4]
        if magic in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1"):
            endian = "<"
        elif magic in (b"\xa1\xb2\xc3\xd4", b"\xa1\xb2\x3c\x4d"):
            endian = ">"
        else:
            raise ValueError("Expected classic PCAP; PCAPNG is not handled")
        _, _, _, _, _, linktype = struct.unpack(endian + "HHiiii", header[4:24])
        if linktype not in {1,101,113,276}:
            raise ValueError(f"Unsupported linktype {linktype}")
        for index in range(200000):
            frame = f.read(16)
            if not frame:
                break
            if len(frame) != 16:
                raise ValueError("Truncated PCAP record header")
            ts_sec, ts_frac, incl, orig = struct.unpack(endian+"IIII", frame)
            if incl > 65535 or incl > orig or incl > CAP_MAX:
                raise ValueError("Invalid packet length")
            payload = f.read(incl)
            if len(payload) != incl:
                raise ValueError("Truncated PCAP record")
            yield linktype, payload, incl, orig
        else:
            raise ValueError("Too many packets")

def ip_transport(linktype, frame):
    start = 0
    if linktype == 1:
        if len(frame) < 14:
            return None
        proto = int.from_bytes(frame[12:14], "big")
        start = 14
        while proto in (0x8100, 0x88a8):
            if len(frame) < start+4:
                return None
            proto = int.from_bytes(frame[start+2:start+4], "big")
            start += 4
        if proto not in (0x0800, 0x86dd):
            return None
    elif linktype == 113:
        start = 16
    elif linktype == 276:
        start = 20
    raw = frame[start:]
    if len(raw) < 20:
        return None
    version = raw[0] >> 4
    if version == 4:
        ihl = (raw[0]&15)*4
        if ihl < 20 or len(raw) < ihl:
            return None
        protocol = raw[9]
        dst = str(ipaddress.IPv4Address(raw[16:20]))
        body = raw[ihl:]
        ipver = "ipv4"
    elif version == 6:
        if len(raw) < 40:
            return None
        protocol = raw[6]
        dst = str(ipaddress.IPv6Address(raw[24:40]))
        body = raw[40:]
        ipver = "ipv6"
    else:
        return None
    if protocol == 6:
        if len(body) < 20: return None
        sport, dport = struct.unpack_from("!HH",body,0)
        offset = (body[12] >> 4)*4
        if offset < 20 or offset > len(body):
            return None
        content = body[offset:]
        trans = "tcp"
    elif protocol == 17:
        if len(body) < 8: return None
        sport, dport = struct.unpack_from("!HH", body, 0)
        content = body[8:]
        trans = "udp"
    else:
        return {"ip_version": ipver,"protocol":str(protocol),"destination":dst,
                "port":None,"payload":b""}
    return {"ip_version":ipver,"protocol":trans,"destination":dst,
            "port":dport,"source_port":sport,"payload":content}

def prune(root, days):
    cutoff = time.time() - days*86400
    deleted = 0
    for folder in root.glob("pcap-limited-*"):
        if not folder.is_dir() or folder.stat().st_mtime >= cutoff:
            continue
        files = list(folder.iterdir())
        if not all(f.is_file() and f.name in {"capture.pcap","summary.json"} for f in files):
            continue
        for f in files: f.unlink()
        folder.rmdir()
        deleted += 1
    return deleted

def main():
    p = argparse.ArgumentParser()
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--pull",action="store_true")
    g.add_argument("--pcap",type=Path)
    p.add_argument("--adb",default="adb")
    p.add_argument("--serial",default="e3e68545")
    p.add_argument("--remote-path",default="/sdcard/Download/PCAPdroid/"+APP_PCAP_NAME)
    p.add_argument("--retention-days",type=int,default=7)
    p.add_argument("--output-root",type=Path,default=Path(os.getenv("LOCALAPPDATA","."))/
                   "OpenQuickHost"/"NetworkCaptures"/"K70")
    args=p.parse_args()
    if not 1<=args.retention_days<=30: p.error("retention-days 1..30")
    root=args.output_root.resolve()
    root.mkdir(parents=True,exist_ok=True)
    folder=root/("pcap-limited-"+datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S"))
    folder.mkdir(parents=True,exist_ok=False)
    capture=folder/"capture.pcap"
    if args.pull:
        result=subprocess.run([args.adb,"-s",args.serial,"pull",args.remote_path,str(capture)],
                              text=True, capture_output=True, timeout=20)
        if result.returncode:
            capture.unlink(missing_ok=True)
            folder.rmdir()
            raise RuntimeError("ADB pull failed: confirm PCAP exported and path correct")
    else:
        capture.write_bytes(args.pcap.read_bytes())
    size=capture.stat().st_size
    if size<24 or size>CAP_MAX:
        raise ValueError("capture size not within 24..2MB safety envelope")
    digest=hashlib.sha256(capture.read_bytes()).hexdigest()
    # Avoid indefinitely duplicating the exact same user-captured PCAP on repeat pulls.
    for prior in sorted(root.glob("pcap-limited-*"), reverse=True):
        if prior==folder or not prior.is_dir():
            continue
        summary_file=prior/"summary.json"
        original=prior/"capture.pcap"
        try:
            if (summary_file.is_file() and original.is_file()
                and json.loads(summary_file.read_text(encoding="utf-8")).get("pcap_sha256")==digest
                and hashlib.sha256(original.read_bytes()).hexdigest()==digest):
                capture.unlink()
                folder.rmdir()
                print("PCAP_ALREADY_IMPORTED=True")
                print("EXISTING_PCAP="+str(original))
                print("EXISTING_SUMMARY="+str(summary_file))
                return
        except (OSError, ValueError, TypeError):
            pass
    protocols=Counter()
    ports=Counter()
    ipversions=Counter()
    dns=set()
    sni=set()
    http_methods=Counter()
    pktcount=0
    invalid=0
    unique_endpoints=set()
    for linktype,data,actual,original in packets(capture):
        pktcount += 1
        item=ip_transport(linktype,data)
        if not item:
            invalid += 1
            continue
        protocols[item["protocol"]]+=1
        ipversions[item["ip_version"]]+=1
        port=item.get("port")
        if port is not None:
            ports[str(port)]+=1
            unique_endpoints.add((item["protocol"],item["destination"],port))
        payload=item["payload"]
        if item["protocol"]=="udp" and port==53:
            host=read_dns_qname(payload)
            if host: dns.add(host)
        if item["protocol"]=="tcp" and payload:
            host=tls_sni(payload)
            if host: sni.add(host)
            for method in (b"GET ",b"POST ",b"PUT ",b"DELETE ",b"HEAD "):
                if payload.startswith(method):
                    http_methods[method.strip().decode()]+=1
                    break
    summary={
        "schema_version":1,"source":"PCAPdroid filtered app PCAP, no TLS decryption",
        "target_application_package":"cc.luoluoluo.yanzi.mobile",
        "pcap_sha256":digest,
        "pcap_bytes":size,"packet_count":pktcount,"unparsed_packets":invalid,
        "ip_versions":dict(ipversions),"protocols":dict(protocols),
        "observed_destination_ports":dict(ports.most_common(15)),
        "unique_destination_endpoints":len(unique_endpoints),
        "dns_query_names_seen_count":len(dns),
        "tls_sni_seen_count":len(sni),"http_method_counts":dict(http_methods),
        "dns_query_names_private":sorted(dns)[:DNS_LIMIT],
        "tls_sni_private":sorted(sni)[:DNS_LIMIT],
        "capture_local_path":str(capture),
        "retention_days":args.retention_days,
        "caveats":[
            "DNS/TLS SNI best effort; encrypted DNS and fragmented TLS may not be readable",
            "Destination ports count both directions, not only outbound flows",
            "Contains first bytes of raw app packets: can be sensitive; keep local",
            "No decryption of HTTPS; no application response body reconstruction",
            "Linktype support is limited to Ethernet, RAW IP and Linux cooked",
        ]}
    (folder/"summary.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    summary["previous_sessions_pruned"]=prune(root,args.retention_days)
    print("PCAP_PACKET_COUNT="+str(pktcount))
    print("PCAP_BYTES="+str(size))
    print("PROTOCOL_COUNTS="+json.dumps(dict(protocols)))
    print("DNS_SNI_COUNTS="+str(len(dns))+","+str(len(sni)))
    print("LOCAL_PCAP="+str(capture))
    print("LOCAL_SUMMARY="+str(folder/"summary.json"))
    print("RAW_PCAP_LOCAL_ONLY=True")
if __name__=="__main__":
    main()
