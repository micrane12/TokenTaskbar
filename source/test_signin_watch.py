import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import cc_account
import signin_watch

NOW = 1_790_900_000.0
DAY = 86400


class SignInWatchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.cred = self.root / ".credentials.json"
        for p in (mock.patch.object(cc_account, "CRED_PATH", str(self.cred)),
                  mock.patch.object(signin_watch, "claude_processes", return_value="claude.exe x1")):
            p.start()
            self.addCleanup(p.stop)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, refresh="r1", expires=NOW + 8 * 3600, login=NOW + 20 * DAY):
        self.cred.write_text(json.dumps({"claudeAiOauth": {
            "accessToken": "secret-access", "refreshToken": refresh,
            "expiresAt": expires * 1000, "refreshTokenExpiresAt": login * 1000}}))

    def log(self):
        return (self.root / "data" / "claude-signin.log").read_text(encoding="utf-8")

    def watch(self):
        return signin_watch.SignInWatch(self.root / "data")

    def test_reports_login_expiry_and_never_logs_tokens(self):
        self.write(refresh="secret-refresh")
        info = self.watch().check(NOW)
        self.assertEqual(info["loginExpires"], NOW + 20 * DAY)
        self.assertNotIn("secret", self.log())
        self.assertNotIn("secret", (self.root / "data" / "claude-signin.json").read_text())

    def test_renewal_is_logged_as_renewed(self):
        w = self.watch()
        self.write()
        w.check(NOW)
        self.write(refresh="r2", expires=NOW + 16 * 3600)
        info = w.check(NOW + 8 * 3600, renewing=True)
        self.assertEqual(info["signinEvent"], "renewed")
        self.assertIn("by TokenTaskbar's renewal", self.log())

    def test_sign_out_before_expiry_is_early(self):
        w = self.watch()
        self.write()
        w.check(NOW)
        self.cred.unlink()
        self.assertEqual(w.check(NOW + 10)["signinEvent"], "")   # one missing tick tolerated
        info = w.check(NOW + 20)
        self.assertEqual(info["signinEvent"], "signed out EARLY")
        self.assertEqual(info["loginExpires"], 0)

    def test_new_login_while_old_valid_is_early(self):
        w = self.watch()
        self.write()
        w.check(NOW)
        self.write(refresh="r2", login=NOW + 30 * DAY)
        self.assertEqual(w.check(NOW + 60)["signinEvent"], "signed in again EARLY")

    def test_change_while_app_was_closed_is_detected(self):
        self.write()
        self.watch().check(NOW)
        self.write(refresh="r2", expires=NOW + 2 * DAY)
        self.assertEqual(self.watch().check(NOW + DAY)["signinEvent"], "renewed")

    def test_unreadable_file_is_ignored(self):
        w = self.watch()
        self.write()
        w.check(NOW)
        self.cred.write_text("{partial")
        self.assertEqual(w.check(NOW + 10)["signinEvent"], "")


if __name__ == "__main__":
    unittest.main()
