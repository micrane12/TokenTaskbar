import json
import tempfile
import threading
import unittest
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from collector import Collector, make_server

NOW = 1_790_900_000


def event(kind, payload, at=NOW):
    return {"type": kind, "timestamp": datetime.fromtimestamp(at, timezone.utc).isoformat(), "payload": payload}


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.home = Path(self.temp.name)
        self.collector = Collector(self.home, self.home / ".codex")
        self.state = {"session": "session-a", "total": 0}

    def tearDown(self):
        self.temp.cleanup()

    def test_explicit_codex_records_replace_legacy_and_dedupe(self):
        usage = {"input_tokens": 100, "cached_input_tokens": 80, "output_tokens": 20, "total_tokens": 120}
        self.collector.ingest(event("event_msg", {"type": "token_count", "info": {
            "total_token_usage": usage, "last_token_usage": usage}}), self.state, "codex", NOW)
        record = event("token_usage_record", {"session_id": "session-a", "response_id": "response-a", "usage": usage})
        self.collector.ingest(record, self.state, "codex", NOW)
        self.collector.ingest(record, self.state, "codex", NOW)
        result = self.collector.snapshot(NOW)["providers"][0]
        self.assertEqual(120, result["tokens_5h"])
        self.assertEqual(120, result["tokens_week"])

    def test_claude_counts_cache_once_and_keeps_only_usage(self):
        entry = {"type": "assistant", "timestamp": datetime.fromtimestamp(NOW, timezone.utc).isoformat(),
                 "message": {"id": "m", "content": "PRIVATE CONTENT", "usage": {
                     "input_tokens": 10, "output_tokens": 5, "cache_read_input_tokens": 100,
                     "cache_creation_input_tokens": 20}}}
        self.collector.ingest(entry, self.state, "claude", NOW)
        self.collector.ingest(entry, self.state, "claude", NOW)
        result = self.collector.snapshot(NOW)
        self.assertEqual(135, result["providers"][1]["tokens_5h"])
        self.assertIsNone(result["providers"][1]["five_hour"])
        self.assertNotIn("PRIVATE CONTENT", json.dumps(result))

    def test_period_totals_and_reported_limits(self):
        self.collector.ingest(event("token_usage_record", {"response_id": "old", "usage": {"total_tokens": 300}}, NOW - 6 * 3600),
                              self.state, "codex", NOW)
        self.collector.ingest(event("event_msg", {"type": "token_count", "rate_limits": {
            "limit_id": "codex", "primary": {"window_minutes": 300, "used_percent": 25, "resets_at": NOW + 600}}}),
                              self.state, "codex", NOW)
        result = self.collector.snapshot(NOW)["providers"][0]
        self.assertEqual(0, result["tokens_5h"])
        self.assertEqual(300, result["tokens_week"])
        self.assertEqual(25, result["five_hour"]["used_percent"])
        self.assertEqual((NOW + 600) * 1000, result["five_hour"]["resets_at"])
        self.assertIsNone(result["weekly"])

    def test_partial_line_is_read_after_writer_finishes(self):
        folder = self.home / ".codex" / "sessions"
        folder.mkdir(parents=True)
        path = folder / "session.jsonl"
        raw = json.dumps(event("token_usage_record", {"response_id": "r", "usage": {"total_tokens": 20}}))
        path.write_text(raw[:30])
        self.assertEqual(0, self.collector.snapshot(NOW)["providers"][0]["tokens_5h"])
        with path.open("a") as stream:
            stream.write(raw[30:] + "\n")
        self.assertEqual(20, self.collector.snapshot(NOW + 3)["providers"][0]["tokens_5h"])
        self.assertEqual(20, self.collector.snapshot(NOW + 6)["providers"][0]["tokens_5h"])

    def test_endpoint_requires_pairing_secret(self):
        server = make_server("127.0.0.1", 0, "test-secret", self.collector)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        url = f"http://127.0.0.1:{server.server_port}/snapshot"
        try:
            with self.assertRaises(HTTPError) as error:
                urlopen(url)
            self.assertEqual(401, error.exception.code)
            with urlopen(Request(url, headers={"Authorization": "Bearer test-secret"})) as response:
                result = json.load(response)
            self.assertEqual(1, result["schema"])
            self.assertEqual(2, len(result["providers"]))
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == "__main__":
    unittest.main()
