"""Baidu exact-name cloud search: offline safety and semantic row tests.

No GUI interaction in this suite: controls are lightweight synthetic UIA nodes.
"""
from pathlib import Path
from types import SimpleNamespace
from unittest import TestCase
from unittest.mock import patch
import sys

root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/"src"/"OpenQuickHost"/"CapabilityScripts"/"BaiduNetdisk"))
from baidu_desktop_search import (
    validate_filename, _semantic_result_snapshot,
    BaiduSearchError,
)


class FakeControl:
    def __init__(self, role, name="", children=None, rect=None):
        self.ControlTypeName=role
        self.Name=name
        self._children=children or []
        self.BoundingRectangle=SimpleNamespace(
            left=(rect or (0,0,0,0))[0],
            top=(rect or (0,0,0,0))[1],
            right=(rect or (0,0,0,0))[2],
            bottom=(rect or (0,0,0,0))[3],
        )

    def GetChildren(self):
        return self._children


class BaiduSearchSafetyTests(TestCase):
    target="AI-baidu-desktop-roundtrip-20261009.txt"

    def test_valid_name_is_preserved(self):
        self.assertEqual(validate_filename(self.target),self.target)

    def test_reject_paths_and_control_characters(self):
        for name in ("a\\b.txt","a/b.txt","abc\n.txt","a\0bc.txt"):
            with self.subTest(name=repr(name)):
                with self.assertRaises(ValueError):
                    validate_filename(name)

    def test_reject_empty_oversize_nonbmp(self):
        for name in ("", "ab", "a"*181, "AI-test-😀.txt"):
            with self.subTest(name=repr(name)):
                with self.assertRaises(ValueError):
                    validate_filename(name)

    def _snapshot(self,heading=None,groups=None):
        children=[FakeControl("TextControl",heading or "")]
        for name,rect in groups or []:
            children.append(FakeControl("GroupControl",name,rect=rect))
        parent=FakeControl("WindowControl",children=children)
        with patch("baidu_desktop_search.u.ControlFromHandle",return_value=parent), \
             patch("baidu_desktop_search.win32gui.GetWindowRect",
                   return_value=(1396,215,2496,915)):
            return _semantic_result_snapshot(2360040,self.target)

    def test_exact_heading_and_one_visible_result(self):
        heading=f'“{self.target}”搜索结果，共4项'
        found,n,rows=self._snapshot(heading,[(self.target,(1580,742,1870,798))])
        self.assertTrue(found)
        self.assertEqual(n,4)
        self.assertEqual(len(rows),1)

    def test_stale_header_not_trusted(self):
        heading='“different-file.txt”搜索结果，共4项'
        found,_,rows=self._snapshot(heading,[(self.target,(1580,742,1870,798))])
        self.assertFalse(found)
        self.assertEqual(len(rows),1)  # Caller must check the header before using it.

    def test_partial_filename_not_accepted(self):
        heading=f'“{self.target}”搜索结果，共4项'
        found,_,rows=self._snapshot(heading,[
            ("AI-baidu-desktop-roundtrip-20261009.txt-copy",(1580,742,1870,798))
        ])
        self.assertTrue(found)
        self.assertEqual(rows,[])

    def test_hidden_or_offscreen_groups_are_excluded(self):
        heading=f'“{self.target}”搜索结果，共4项'
        found,_,rows=self._snapshot(heading,[
            (self.target,(0,0,0,0)),
            (self.target,(1800,0,1900,50)),
        ])
        self.assertTrue(found)
        self.assertEqual(len(rows),0)

    def test_two_visible_exact_matches_are_preserved(self):
        heading=f'“{self.target}”搜索结果，共2项'
        found,_,rows=self._snapshot(heading,[
            (self.target,(1580,610,1870,666)),
            (self.target,(1580,742,1870,798)),
        ])
        self.assertTrue(found)
        self.assertEqual(len(rows),2)
