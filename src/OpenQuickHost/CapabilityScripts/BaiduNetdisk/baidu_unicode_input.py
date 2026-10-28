"""Windows Unicode keyboard input, bypassing Chinese IME candidate interception.

Only call while a previously verified Baidu search control owns focus.
No clipboard access. This module has no dependency on the Quark adapter.
"""
from ctypes import *
from ctypes import wintypes

ULONG_PTR=c_ulonglong if sizeof(c_void_p)==8 else c_ulong
class MOUSEINPUT(Structure):
    _fields_=[("dx",wintypes.LONG),("dy",wintypes.LONG),
              ("mouseData",wintypes.DWORD),("dwFlags",wintypes.DWORD),
              ("time",wintypes.DWORD),("dwExtraInfo",ULONG_PTR)]
class KEYBDINPUT(Structure):
    _fields_=[("wVk",wintypes.WORD),("wScan",wintypes.WORD),
              ("dwFlags",wintypes.DWORD),("time",wintypes.DWORD),
              ("dwExtraInfo",ULONG_PTR)]
class HARDWAREINPUT(Structure):
    _fields_=[("uMsg",wintypes.DWORD),("wParamL",wintypes.WORD),("wParamH",wintypes.WORD)]
class UNION(Union):
    _fields_=[("mi",MOUSEINPUT),("ki",KEYBDINPUT),("hi",HARDWAREINPUT)]
class INPUT(Structure):
    _fields_=[("type",wintypes.DWORD),("union",UNION)]
SendInput=WINFUNCTYPE(wintypes.UINT,wintypes.UINT,POINTER(INPUT),c_int)(("SendInput",windll.user32))

def send_unicode(txt):
    events=[]
    for ch in txt:
        if ord(ch)>0xffff:raise ValueError("BMP only")
        for flag in (0x4,0x6):
            ev=INPUT();ev.type=1;ev.union.ki=KEYBDINPUT(0,ord(ch),flag,0,0)
            events.append(ev)
    seq=(INPUT*len(events))(*events)
    n=SendInput(len(events),seq,sizeof(INPUT))
    if n!=len(events):raise OSError(f"Windows SendInput processed {n}/{len(events)} events")
    return n
