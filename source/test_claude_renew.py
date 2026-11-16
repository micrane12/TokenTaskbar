import json
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

import cc_account
import claude_renew

NOW = 1_790_900_000.0


class RenewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = Path(self.temp.name)
        self.cred = root / ".credentials.json"
        self.settings = root / "widget-settings.json"
        self.state = root / "data" / "claude-renew.json"
        self.write_cred(expires=NOW - 60, login=NOW + 20 * 86400)
        self.enable(True)
        patches = [mock.patch.object(cc_account, "CRED_PATH", str(self.cred)),
                   mock.patch.object(claude_renew, "find_cli", return_value="claude"),
                   mock.patch.object(claude_renew.threading, "Thread", Immediate)]
        for p in patches:
            p.start()
            self.addCleanup(p.stop)
        self.calls = []

    def tearDown(self):
        self.temp.cleanup()

    def write_cred(self, expires, login):
        self.cred.write_text(json.dumps({"claudeAiOauth": {
            "accessToken": "x", "expiresAt": expires * 1000, "refreshTokenExpiresAt": login * 1000}}))

    def enable(self, on):
        self.settings.write_text(json.dumps({"RenewClaude": on}))

    def renewer(self):
        return claude_renew.Renewer(self.state, self.settings)

    def fake_run(self, renews=True, output=""):
        def run(cli, args):
            self.calls.append(args)
            if renews:
                self.write_cred(expires=time.time() + 8 * 3600, login=NOW + 20 * 86400)
            return output
        return mock.patch.object(claude_renew, "_run", side_effect=run)

    def test_off_by_default_never_runs(self):
        self.settings.write_text("{}")
        with self.fake_run():
            self.assertEqual(claude_renew.REFRESH_HINT, self.renewer().check({"state": "expired"}, NOW))
        self.assertEqual([], self.calls)

    def test_only_when_expired(self):
        with self.fake_run():
            self.assertEqual("", self.renewer().check({"state": "ok"}, NOW))
            self.assertEqual("", self.renewer().check({"state": "rate-limited"}, NOW))
        self.assertEqual([], self.calls)

    def test_runs_once_then_waits_eight_hours_across_restarts(self):
        with self.fake_run(renews=False, output="boom"):
            self.renewer().check({"state": "expired"}, NOW)
            self.assertEqual(1, len(self.calls))
            self.assertEqual(claude_renew.REFRESH_HINT, self.renewer().check({"state": "expired"}, NOW + 3600))
            self.renewer().check({"state": "expired"}, NOW + 7.9 * 3600)
            self.assertEqual(1, len(self.calls))
            self.renewer().check({"state": "expired"}, NOW + 8.1 * 3600)
            self.assertEqual(2, len(self.calls))

    def test_success_is_judged_by_a_renewed_token(self):
        renewer = self.renewer()
        with self.fake_run():
            renewer.check({"state": "expired"}, NOW)
        self.assertEqual("ok", renewer.result)
        self.assertTrue(claude_renew.token_valid())

    def test_expired_login_needs_a_real_sign_in(self):
        self.write_cred(expires=NOW - 60, login=NOW - 1)
        with self.fake_run():
            self.assertEqual(claude_renew.SIGN_IN_HINT, self.renewer().check({"state": "expired"}, NOW))
        self.assertEqual([], self.calls)

    def test_signed_out_reply_asks_for_sign_in(self):
        with self.fake_run(renews=False, output="Invalid API key · Please run /login"):
            renewer = self.renewer()
            renewer.check({"state": "expired"}, NOW)
            self.assertEqual(claude_renew.SIGN_IN_HINT, renewer.check({"state": "expired"}, NOW + 60))

    def test_already_renewed_does_not_run(self):
        self.write_cred(expires=NOW + 3600, login=NOW + 20 * 86400)
        with self.fake_run():
            self.assertEqual("", self.renewer().check({"state": "expired"}, NOW))
        self.assertEqual([], self.calls)

    def test_older_cli_gets_plain_retry(self):
        outputs = iter(["error: unknown option '--tools'", ""])
        def run(cli, args):
            self.calls.append(args)
            text = next(outputs)
            if args == claude_renew.PLAIN:
                self.write_cred(expires=time.time() + 8 * 3600, login=NOW + 20 * 86400)
            return text
        with mock.patch.object(claude_renew, "_run", side_effect=run):
            renewer = self.renewer()
            renewer.check({"state": "expired"}, NOW)
        self.assertEqual([claude_renew.PING, claude_renew.PLAIN], self.calls)
        self.assertEqual("ok", renewer.result)


class Immediate:
    """Runs the renewal inline so tests are deterministic."""
    def __init__(self, target, args=(), daemon=None):
        self.target, self.args = target, args

    def start(self):
        self.target(*self.args)


if __name__ == "__main__":
    unittest.main()
