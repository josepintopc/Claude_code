#!/usr/bin/env python3
"""Testes do script da comparação online (rodam no CI e localmente: python .github/scripts/test_accept_result.py)."""
import json
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "accept_result.py")

# Corpo igual ao que o app gera (CommunityResults.IssueUrl), com campos extras que devem ser descartados.
GOOD = {
    "id": "8d3f9a62-4f1e-4b7a-9c55-0a1b2c3d4e5f", "test": "gpu.scene", "score": 1433.04, "unit": "pts",
    "timestamp": "2026-11-18T10:00:00-03:00", "cpuName": "AMD Ryzen 7 7800X3D", "gpuName": "NVIDIA GeForce RTX 4070",
    "avgFps": 143.3, "low1Fps": 98.6, "resolution": "1920x1080", "deviceName": "NVIDIA GeForce RTX 4070", "threads": 0,
    "seconds": 60.1, "samples": 0, "passes": 0, "preset": "1080p-60s", "engineVersion": "1.0", "appVersion": "0.1.0",
    "imported": False, "source": "C:\\Users\\Fulano\\x.json", "computerName": "PC-DO-FULANO",
}


def body(obj):
    return "Resultado enviado pelo Supernova Bench.\n\n```json\n" + json.dumps(obj, indent=2) + "\n```\n"


def run(issue_body, db_path, number="42"):
    env = dict(os.environ, ISSUE_BODY=issue_body, ISSUE_NUMBER=number)
    env.pop("GITHUB_OUTPUT", None)
    out = subprocess.run([sys.executable, "-I", SCRIPT, db_path], env=env, capture_output=True, text=True, check=True).stdout
    return dict(line.split("=", 1) for line in out.strip().splitlines())


class AcceptResultTests(unittest.TestCase):
    def setUp(self):
        fd, self.db = tempfile.mkstemp(suffix=".json")
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump({"format": "supernova-community", "version": 1, "updated": None, "results": []}, f)

    def tearDown(self):
        os.remove(self.db)

    def results(self):
        with open(self.db, encoding="utf-8") as f:
            return json.load(f)["results"]

    def test_accepts_valid_result_with_public_fields_only(self):
        r = run(body(GOOD), self.db)
        self.assertEqual("accepted", r["status"])
        saved = self.results()
        self.assertEqual(1, len(saved))
        e = saved[0]
        self.assertEqual(1433.0, e["score"])
        self.assertEqual(42, e["issue"])
        self.assertEqual("NVIDIA GeForce RTX 4070", e["gpuName"])
        for private in ("source", "computerName", "imported", "deviceName"):
            self.assertNotIn(private, e)

    def test_duplicate_is_not_added_twice(self):
        run(body(GOOD), self.db)
        r = run(body(GOOD), self.db, number="43")
        self.assertEqual("duplicate", r["status"])
        self.assertEqual(1, len(self.results()))

    def test_rejects_bad_input(self):
        bad = [
            "sem bloco json",
            "```json\n[1, 2]\n```",
            "```json\n{ quebrado\n```",
            body({**GOOD, "test": "cpu.hack"}),
            body({**GOOD, "score": -1}),
            body({**GOOD, "score": 1e99}),
            body({**GOOD, "id": "nao-e-uuid"}),
            body({**GOOD, "cpuName": "x" * 500}),
            body({**GOOD, "cpuName": ""}),
            body({**GOOD, "threads": True}),
            body({**GOOD, "metrics": {"k" * 50: 1}}),
            body({**GOOD, "metrics": {"a b": 1}}),
            body({**GOOD, "cpuName": "CPU\nstatus=accepted"}),
            body({**GOOD, "gpuName": "<img src=x>"}),
        ]
        for b in bad:
            with self.subTest(b=b[:40]):
                self.assertEqual("rejected", run(b, self.db)["status"])
        self.assertEqual([], self.results())

    def test_message_never_echoes_raw_input(self):
        r = run(body({**GOOD, "test": "<script>alert(1)</script>"}), self.db)
        self.assertEqual("rejected", r["status"])
        self.assertNotIn("script", r["message"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
