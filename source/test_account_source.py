import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import worker


class AccountSourceTests(unittest.TestCase):
    def test_bridge_reading_avoids_duplicate_provider_requests(self):
        quota = {"ok": True, "state": "rate-limited", "stale": True,
                 "fetched_at": 100, "retry_at": 9999999999,
                 "windows": [{"key": "session", "pct": 16, "resets_at": 200, "active": False}]}
        shared = {"claude": dict(quota), "codex": dict(quota)}
        with patch.object(worker, "bridge_accounts", return_value=shared), \
                patch.object(worker.cc_account, "status") as claude, \
                patch.object(worker.codex_account, "status") as codex:
            self.assertEqual(shared, worker.account_status(block=True))
            claude.assert_not_called()
            codex.assert_not_called()
        window = worker.display_windows(worker.normalize(quota, 100))[0]
        self.assertEqual(16, window["used"])
        self.assertTrue(window["valid"])

    def test_cache_does_not_store_credential_or_error_fields(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/"quota.json"
            worker.save_accounts(path, {"claude": {"ok": True, "windows": [],
                                  "access_token": "PRIVATE", "detail": "PRIVATE ERROR"}})
            raw = path.read_text()
            self.assertNotIn("PRIVATE", raw)
            self.assertEqual([], json.loads(raw)["claude"]["windows"])

    def test_bridge_failure_falls_back_to_standalone_readers(self):
        with patch.object(worker, "bridge_accounts", return_value=None), \
                patch.object(worker.cc_account, "status", return_value={"state": "ok"}), \
                patch.object(worker.codex_account, "status", return_value={"state": "ok"}):
            self.assertEqual({"claude": {"state": "ok"}, "codex": {"state": "ok"}}, worker.account_status())


if __name__ == "__main__":
    unittest.main()
