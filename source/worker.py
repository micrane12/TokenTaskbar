"""Private stdout transport for Token Taskbar. No listening server or chat export."""
import argparse
import json
import math
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.request import urlopen

import cc_account
import codex_account
from collector import Collector


def normalize(account, now):
    result = []
    for item in account.get("windows", []):
        try:
            pct = float(item["pct"])
            reset = float(item.get("resets_at") or 0)
            if not math.isfinite(pct) or not 0 <= pct <= 100:
                continue
            result.append({"label": item.get("short") or item.get("label") or "quota",
                           "used": pct, "reset": reset,
                           "slot": ("short" if item.get("key") in ("session", "primary")
                                    or item.get("short") == "5h" else "weekly"
                                    if item.get("key") in ("weekly_all", "secondary")
                                    or item.get("short") == "wk" else "other"),
                           "active": item.get("active", True),
                           "valid": reset > now})
        except (KeyError, TypeError, ValueError):
            continue
    return result


def display_windows(windows):
    """Keep unavailable short/weekly pools in place; never shift scoped pools."""
    return [next((w for w in windows if w["slot"] == slot),
                 {"label": label, "slot": slot, "used": 0, "reset": 0, "valid": False})
            for slot, label in (("short", "5h"), ("weekly", "wk"))]


def snapshot(collector, accounts, now=None):
    now = now or time.time()
    local = collector.snapshot(now)
    providers = []
    for p in local["providers"]:
        a = accounts.get(p["id"], {})
        windows = display_windows(normalize(a, now))
        providers.append({"id": p["id"], "name": p["name"],
                          "tokens5h": p["tokens_5h"], "tokensWeek": p["tokens_week"],
                          "found": p["found"], "windows": windows,
                          "state": a.get("state", "loading"),
                          "source": a.get("_origin") or ("last session" if a.get("local") else "account"),
                          "stale": bool(a.get("stale") or a.get("local")),
                          "observed": a.get("fetched_at", 0) or 0})
    return {"generated": now, "machine": os.environ.get("COMPUTERNAME", "This PC"),
            "providers": providers}


def bridge_accounts():
    """Share the local Bridge's authoritative counters; never its estimates."""
    try:
        with urlopen("http://127.0.0.1:5765/ccusage", timeout=2) as response:
            raw = json.load(response)
        accounts = {"claude": raw.get("account"), "codex": raw.get("codex")}
        if not all(isinstance(a, dict) and isinstance(a.get("windows", []), list)
                   for a in accounts.values()):
            return None
        return {key: dict(a, _origin="Bridge cache") for key, a in accounts.items()}
    except (OSError, ValueError, TypeError, AttributeError):
        return None


def account_status(block=False):
    shared = bridge_accounts()
    if shared is not None:
        # Seed the standalone readers too, preserving the provider's retry deadline
        # if Bridge disappears. Do not trigger a separate provider request here.
        for key, module in (("claude", cc_account), ("codex", codex_account)):
            if key == "claude" and shared[key].get("state") in (
                    "expired", "no-credentials", "auth-rejected"):
                # An older Bridge may retain an auth failure after Claude Code
                # renews. Let the standalone reader observe the new credentials.
                shared[key] = cc_account.status(block)
                continue
            with module._lock:
                module._cache = dict(shared[key])
                module._next_fetch = max(time.time() + module.REFRESH_SECONDS,
                                         float(shared[key].get("retry_at") or 0))
        return shared
    if block:
        with ThreadPoolExecutor(max_workers=2) as pool:
            jobs = {"codex": pool.submit(codex_account.status, True),
                    "claude": pool.submit(cc_account.status, True)}
            return {key: job.result() for key, job in jobs.items()}
    return {"codex": codex_account.status(), "claude": cc_account.status()}


def restore_accounts(path):
    try:
        saved = json.loads(path.read_text(encoding="utf-8"))
        for key, module in (("claude", cc_account), ("codex", codex_account)):
            account = saved.get(key)
            if not isinstance(account, dict) or not isinstance(account.get("windows", []), list):
                continue
            observed = float(account.get("fetched_at") or 0)
            if observed > time.time() or observed < time.time() - 7 * 86400:
                continue
            with module._lock:
                module._cache = dict(account, stale=True, _origin="Saved reading")
                module._next_fetch = float(account.get("retry_at") or 0)
    except (OSError, ValueError, TypeError, AttributeError):
        pass


def save_accounts(path, accounts):
    fields = ("ok", "state", "windows", "stale", "fetched_at", "retry_at", "local")
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix(".tmp")
        temporary.write_text(json.dumps({key: {field: value.get(field) for field in fields
                                              if field in value}
                                         for key, value in accounts.items()}), encoding="utf-8")
        temporary.replace(path)
    except OSError:
        pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--once", action="store_true")
    args = parser.parse_args()
    collector = Collector()
    root = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent.parent
    cache = root / "data" / "quota-cache.json"
    restore_accounts(cache)
    accounts = account_status(block=True)
    while True:
        try:
            save_accounts(cache, accounts)
            print(json.dumps(snapshot(collector, accounts), separators=(",", ":")), flush=True)
        except BrokenPipeError:
            return
        if args.once:
            return
        time.sleep(10)
        accounts = account_status()


if __name__ == "__main__":
    main()
