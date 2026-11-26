"""Offline regressions for AP's whole-buffer hash policy."""
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "vendor/efmi-mcp/server"))
from efmi_mcp.fa_dump import DumpDraw, DumpSlot, FrameDump
from efmi_mcp.parity import compare_sets


class DumpParityTests(unittest.TestCase):
    def compare(self, captured_hash, dump_hash, full_hash=None, count=3):
        captured = {"origin": "game", "shaders": {}, "tex": {},
                    "ib": {"hash": captured_hash, "offset": 0},
                    "args": {"index_count": 3, "first_index": 0}}
        dump = FrameDump(Path("unused"), None, {
            1: DumpDraw(1, "DrawIndexed", {"IndexCount": count, "StartIndexLocation": 0},
                        slots={"ib": DumpSlot("ib", dump_hash, full_hash)})
        }, 1, True)
        return compare_sets(SimpleNamespace(draws=lambda: iter([captured])), dump)

    def test_whole_buffer_match_is_compared(self):
        result = self.compare("978fe28d", "978fe28d")
        self.assertTrue(result.ok)
        self.assertEqual(result.checks, 1)

    def test_whole_buffer_mismatch_is_reported(self):
        result = self.compare("978fe28d", "a40a9e6a")
        self.assertFalse(result.ok)
        self.assertEqual(result.mismatches, 1)

    def test_unseen_mesh_is_not_a_mismatch(self):
        result = self.compare("978fe28d", "a40a9e6a", count=6)
        self.assertEqual((result.checks, result.mismatches), (0, 0))
        self.assertIn("1 meshes of the dump not drawn", result.messages[0])

    def test_region_hash_comparison_still_works(self):
        result = self.compare("978fe28d", "978fe28d", full_hash="a40a9e6a")
        self.assertTrue(result.ok)


if __name__ == "__main__":
    unittest.main()
