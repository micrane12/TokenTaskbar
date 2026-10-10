import unittest
from worker import normalize


class WindowTests(unittest.TestCase):
    def test_expired_is_not_reset_to_zero(self):
        result = normalize({"windows": [{"pct": 85, "resets_at": 90}]}, 100)
        self.assertEqual(85, result[0]["used"])
        self.assertFalse(result[0]["valid"])

    def test_invalid_percentages_are_rejected(self):
        self.assertEqual([], normalize({"windows": [
            {"pct": float("nan")}, {"pct": -1}, {"pct": 101}]}, 100))

    def test_inactive_reported_pool_is_visible_but_missing_reset_unavailable(self):
        result = normalize({"windows": [{"pct": 0},
            {"pct": 20, "resets_at": 200, "active": False}]}, 100)
        self.assertFalse(result[0]["valid"])
        self.assertTrue(result[1]["valid"])
        self.assertFalse(result[1]["active"])

    def test_valid_zero_and_full_quota(self):
        result = normalize({"windows": [{"pct": 0, "resets_at": 200},
            {"pct": 100, "resets_at": 200}]}, 100)
        self.assertTrue(all(w["valid"] for w in result))


if __name__ == "__main__":
    unittest.main()
