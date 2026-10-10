import unittest
from cc_account import _normalise
from worker import normalize, display_windows


class ClaudeWindowsTests(unittest.TestCase):
    def test_named_response_has_short_and_weekly(self):
        result = _normalise({"five_hour": {"utilization": 16, "resets_at": 200},
                             "seven_day": {"utilization": 37, "resets_at": 400}})
        windows = display_windows(normalize({"windows": result}, 100))
        self.assertEqual([16, 37], [w["used"] for w in windows])
        self.assertTrue(all(w["valid"] for w in windows))

    def test_missing_short_does_not_shift_weekly(self):
        result = _normalise({"limits": [{"kind": "weekly_all", "percent": 37,
                                         "resets_at": 400}]})
        windows = display_windows(normalize({"windows": result}, 100))
        self.assertFalse(windows[0]["valid"])
        self.assertEqual("5h", windows[0]["label"])
        self.assertEqual(37, windows[1]["used"])

    def test_scoped_pool_does_not_replace_all_models(self):
        result = _normalise({"limits": [{"kind": "weekly_scoped", "percent": 80,
                                         "resets_at": 400, "scope": {"model": {"display_name": "Opus"}}}]})
        windows = display_windows(normalize({"windows": result}, 100))
        self.assertTrue(all(not w["valid"] for w in windows))

    def test_explicit_inactive_and_missing_percentage(self):
        result = _normalise({"limits": [{"kind": "session", "percent": 10,
                                         "resets_at": 200, "is_active": False},
                                        {"kind": "weekly_all", "resets_at": 400}]})
        self.assertEqual(1, len(result))
        window = display_windows(normalize({"windows": result}, 100))[0]
        self.assertTrue(window["valid"])
        self.assertFalse(window["active"])

    def test_new_response_wins_over_named_fallback(self):
        result = _normalise({"limits": [{"kind": "session", "percent": 25, "resets_at": 200}],
                             "five_hour": {"utilization": 16, "resets_at": 200}})
        self.assertEqual(1, len(result))
        self.assertEqual(25, result[0]["pct"])


if __name__ == "__main__":
    unittest.main()
