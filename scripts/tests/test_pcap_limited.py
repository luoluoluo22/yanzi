"""Deterministic parser self-test with synthetic, non-personal PCAP packets."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest

MODULE=Path(__file__).resolve().parents[1]/"yanzi-pcap-limited-analyze.py"
spec=importlib.util.spec_from_file_location("yanzi_pcap_limited",MODULE)
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def ipv4_udp_dns():
    name=b"\x07example\x03com\x00"
    dns=(b"\x12\x34\x01\x00\x00\x01\x00\x00\x00\x00\x00\x00" +
         name + b"\x00\x01\x00\x01")
    udp=struct.pack("!HHHH",39000,53,8+len(dns),0)+dns
    ip=(b"\x45\x00"+struct.pack("!H",20+len(udp))+
        b"\x00\x00\x00\x00\x40\x11\x00\x00"+
        b"\x0a\x00\x00\x02"+b"\x08\x08\x08\x08")
    return ip+udp

class PcapLimitedTest(unittest.TestCase):
    def test_ipv4_udp_dns(self):
        pkt=ipv4_udp_dns()
        result=m.ip_transport(101,pkt)
        self.assertEqual(result["protocol"],"udp")
        self.assertEqual(result["port"],53)
        self.assertEqual(result["destination"],"8.8.8.8")
        self.assertEqual(m.read_dns_qname(result["payload"]),"example.com")
        self.assertIsNone(m.read_dns_qname(b"\x01"))
        self.assertIsNone(m.tls_sni(b""))
    def test_pcap_file(self):
        pkt=ipv4_udp_dns()
        with tempfile.TemporaryDirectory() as folder:
            f=Path(folder)/"synthetic.pcap"
            header=b"\xd4\xc3\xb2\xa1"+struct.pack("<HHiiii",2,4,0,0,65535,101)
            record=struct.pack("<IIII",1,0,len(pkt),len(pkt))+pkt
            f.write_bytes(header+record)
            entries=list(m.packets(f))
            self.assertEqual(len(entries),1)
            self.assertEqual(entries[0][0],101)
            self.assertEqual(m.ip_transport(*entries[0][:2])["port"],53)
    def test_invalid_dns(self):
        self.assertIsNone(m.read_dns_qname(b"\x00"*12+b"\xc0\x0c"))
        self.assertIsNone(m.ip_transport(101,b""))
if __name__=="__main__":
    unittest.main()
