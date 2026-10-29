"""cc_account.py - the REAL rate-limit percentages, read from the account.

cc_usage.py estimates consumption by summing local ~/.claude transcripts and
dividing by a limit the user had to calibrate by hand. That can only ever
approximate what `/usage` prints, because Anthropic's own counters are not
derivable from transcripts: they are weighted per model in ways a token sum does
not reproduce, and the window boundaries are the account's, not ours.

This module skips the estimate and asks the account, which is what Claude Code
itself does: `GET /api/oauth/usage` with the OAuth access token Claude Code
already stored in ~/.claude/.credentials.json. The numbers it returns ARE the
numbers in the `/usage` popover - same percentages, same reset timestamps.

What we do NOT do
-----------------
We never refresh, rotate, or write the credentials file. The refresh grant
rotates the refresh token, so racing Claude Code for it would invalidate the
token the running CLI holds and log the user out of their own editor. We read
the file, use the access token until it expires (~90 min), and let Claude Code -
which re-writes the file every time it runs - be the only writer. If the token
is expired we report `state: "expired"` and the caller falls back to the local
estimate rather than silently showing stale percentages.

The payload
-----------
`limits` is a self-describing list, which is why this module renders from it
rather than from the parallel `five_hour` / `seven_day_*` keys: new pools appear
there without a client change. Each entry:

    {"kind": "session" | "weekly_all" | "weekly_scoped",
     "group": "session" | "weekly",
     "percent": 82, "severity": "normal" | "warning" | ...,
     "resets_at": "2026-08-17T02:00:00+00:00",
     "scope": {"model": {"display_name": "Fable"}} | null,
     "is_active": bool}

Stdlib only, thread-safe, cached. The UI polls every few seconds; we hit the
account at most once a minute and always answer from cache.
"""
from __future__ import annotations

import json
import os
import re
import threading
import time
from datetime import datetime, timedelta, timezone
from typing import Dict, List, Optional
from urllib import error as _urlerror
from urllib import request as _urlrequest

CRED_PATH = os.path.join(os.environ.get("CLAUDE_CONFIG_DIR") or
                         os.path.join(os.path.expanduser("~"), ".claude"), ".credentials.json")
USAGE_URL = "https://api.anthropic.com/api/oauth/usage"

REFRESH_SECONDS = 60.0          # how often we may hit the account
ERROR_BACKOFF = 180.0           # after an auth/network failure, wait this long
# Floor between USER-triggered refreshes. Not a politeness number: this endpoint
# answers 429, so a button wired straight through would rate-limit the readout
# it exists to update. 15s is short enough to feel instant on the second look
# and long enough that leaning on the key cannot trip the limit.
FORCE_MIN_INTERVAL = 15.0
TIMEOUT = 8.0

_lock = threading.RLock()
_cache: Dict = {"ok": False, "state": "loading", "fetched_at": 0.0, "windows": []}
_next_fetch = 0.0
_inflight = False
_force_lock = threading.Lock()  # serialises manual refreshes; see refresh_now
_last_forced = 0.0
_credential_stamp = None


def _credentials_changed():
    """Watch metadata only; never copy tokens into diagnostics or usage caches."""
    global _credential_stamp
    try:
        info = os.stat(CRED_PATH)
        stamp = (info.st_mtime_ns, info.st_size)
    except OSError:
        stamp = None
    changed = stamp != _credential_stamp
    _credential_stamp = stamp
    return changed


# ── credentials (read-only) ──────────────────────────────────────────────────
def _read_token() -> Optional[Dict]:
    try:
        with open(CRED_PATH, encoding="utf-8") as f:
            oauth = (json.load(f) or {}).get("claudeAiOauth") or {}
    except (OSError, ValueError):
        return None
    tok = oauth.get("accessToken")
    if not tok:
        return None
    # expiresAt is epoch MILLIseconds.
    exp = oauth.get("expiresAt")
    try:
        exp = float(exp) / 1000.0
    except (TypeError, ValueError):
        exp = 0.0
    return {"token": tok, "expires_at": exp,
            "plan": oauth.get("subscriptionType") or "",
            "tier": oauth.get("rateLimitTier") or ""}


# ── payload normalisation ────────────────────────────────────────────────────
_ISO = re.compile(r"^(\d{4})-(\d\d)-(\d\d)[T ](\d\d):(\d\d):(\d\d)(?:\.(\d+))?"
                  r"(Z|[+-]\d\d:?\d\d)?$")


def _epoch(ts) -> Optional[float]:
    """ISO-8601 (with offset) or epoch number -> epoch seconds."""
    if ts is None:
        return None
    if isinstance(ts, (int, float)):
        # Some fields are milliseconds; anything past year ~5138 must be ms.
        return float(ts) / 1000.0 if ts > 1e11 else float(ts)
    m = _ISO.match(str(ts).strip())
    if not m:
        return None
    y, mo, d, h, mi, s, _frac, off = m.groups()
    dt = datetime(int(y), int(mo), int(d), int(h), int(mi), int(s),
                  tzinfo=timezone.utc)
    if off and off not in ("Z", "z"):
        sign = 1 if off[0] == "+" else -1
        off = off[1:].replace(":", "")
        dt -= sign * timedelta(hours=int(off[:2]), minutes=int(off[2:4]))
    return dt.timestamp()


def _label(entry: Dict) -> str:
    """Long label for the breakdown panel."""
    kind = entry.get("kind") or ""
    if kind == "session":
        return "session (5h)"
    scope = (entry.get("scope") or {}).get("model") or {}
    name = scope.get("display_name")
    if name:
        return "week · " + name.lower()
    return "week (all models)"


def _short(entry: Dict) -> str:
    """Chip label. Scoped pools carry the model name; the rest are the window."""
    kind = entry.get("kind") or ""
    if kind == "session":
        return "5h"
    scope = (entry.get("scope") or {}).get("model") or {}
    return scope.get("display_name") or "wk"


def _normalise(raw: Dict) -> List[Dict]:
    out = []
    entries = list(raw.get("limits") or [])
    # Older responses expose the same quota pools as named objects.
    known = {e.get("kind") for e in entries if isinstance(e, dict)}
    for field, kind, group in (("five_hour", "session", "session"),
                               ("seven_day", "weekly_all", "weekly")):
        value = raw.get(field)
        if isinstance(value, dict) and kind not in known:
            entries.append(dict(value, kind=kind, group=group))
    for e in entries:
        if not isinstance(e, dict):
            continue
        pct = e.get("percent")
        if pct is None:
            pct = e.get("utilization")
        if pct is None or isinstance(pct, bool):
            continue
        try:
            pct = round(float(pct), 1)
        except (TypeError, ValueError):
            continue
        out.append({
            "key": e.get("kind") or "",
            "group": e.get("group") or "",
            "label": _label(e),
            "short": _short(e),
            "pct": pct,
            "severity": e.get("severity") or "normal",
            "resets_at": _epoch(e.get("resets_at")),
            "active": e.get("is_active") is not False,
        })
    # session first, then weekly, then scoped pools - the order the popover uses.
    order = {"session": 0, "weekly_all": 1, "weekly_scoped": 2}
    out.sort(key=lambda w: (order.get(w["key"], 9), -w["pct"]))
    return out


def _extra_usage(raw: Dict) -> Optional[Dict]:
    """Pay-as-you-go credits, when the account has them enabled."""
    x = raw.get("extra_usage")
    if not isinstance(x, dict) or not x.get("is_enabled"):
        return None
    return {"used_credits": x.get("used_credits"), "limit": x.get("monthly_limit"),
            "pct": x.get("utilization"), "currency": x.get("currency")}


# ── fetch ────────────────────────────────────────────────────────────────────
def _fetch() -> Dict:
    cred = _read_token()
    if not cred:
        return {"ok": False, "state": "no-credentials",
                "detail": "no ~/.claude/.credentials.json - run `claude` once to sign in"}
    if cred["expires_at"] and cred["expires_at"] <= time.time():
        # Deliberately not refreshed here - see the module docstring.
        return {"ok": False, "state": "expired",
                "detail": "the stored access token has expired; run any Claude Code "
                          "command to refresh it"}
    req = _urlrequest.Request(USAGE_URL, headers={
        "Authorization": "Bearer " + cred["token"],
        "Content-Type": "application/json",
        "anthropic-beta": "oauth-2025-04-20",
        "User-Agent": "chat-bridge",
    })
    try:
        with _urlrequest.urlopen(req, timeout=TIMEOUT) as r:
            raw = json.loads(r.read().decode("utf-8", "replace"))
    except _urlerror.HTTPError as e:
        if e.code == 429:
            # This endpoint really does rate-limit, and it is the one failure a
            # manual refresh can CAUSE, so it gets its own state instead of
            # being flattened into "the account rejected the request". Honour
            # Retry-After when it is sent so the backoff matches what the server
            # actually asked for rather than our generic three minutes.
            try:
                retry = float(e.headers.get("Retry-After") or 0)
            except (TypeError, ValueError):
                retry = 0.0
            return {"ok": False, "state": "rate-limited",
                    "retry_after": retry or ERROR_BACKOFF,
                    "detail": "HTTP 429 - the usage API is rate-limiting"}
        state = {401: "auth-rejected", 403: "forbidden"}.get(e.code, "http-error")
        return {"ok": False, "state": state, "detail": "HTTP %d" % e.code}
    except Exception as e:                    # network down, DNS, timeout, bad JSON
        return {"ok": False, "state": "offline", "detail": str(e)[:120]}
    if not isinstance(raw, dict):
        return {"ok": False, "state": "http-error", "detail": "unexpected payload"}
    return {"ok": True, "state": "ok", "windows": _normalise(raw),
            "extra_usage": _extra_usage(raw), "plan": cred["plan"],
            "tier": cred["tier"], "fetched_at": time.time()}


def _refresh_locked() -> None:
    global _cache, _next_fetch, _inflight
    res = _fetch()
    now = time.time()
    with _lock:
        _inflight = False
        if res.get("ok"):
            wait = REFRESH_SECONDS
        else:
            # A 429 tells us how long to wait; everything else gets the generic
            # backoff. Never shorter than ERROR_BACKOFF is wrong here - if the
            # server says 20s, waiting 180s just keeps the readout stale longer.
            wait = float(res.get("retry_after") or ERROR_BACKOFF)
        _next_fetch = now + wait
        if res.get("ok"):
            _cache = res
        else:
            # Keep the last good numbers visible, but mark them stale so the UI
            # can say so rather than presenting an old percentage as current.
            prev = _cache if _cache.get("ok") else {}
            _cache = dict(prev)
            _cache.update({"ok": bool(prev), "state": res["state"],
                           "detail": res.get("detail", ""), "stale": bool(prev),
                           "retry_at": _next_fetch})


def status(block: bool = False) -> Dict:
    """Cached account usage. Never blocks a request unless `block` is set (used
    by the warm-up thread), so the footer poll stays instant."""
    global _inflight
    with _lock:
        changed = _credentials_changed()
        if changed and _cache.get("state") in ("expired", "no-credentials", "auth-rejected"):
            # A CLI renewal should recover on the next poll, not after the
            # previous authentication backoff. Never bypass a provider 429.
            _next_due = 0.0
        else:
            _next_due = _next_fetch
        due = time.time() >= _next_due and not _inflight
        if due:
            _inflight = True
    if due:
        if block:
            _refresh_locked()
        else:
            threading.Thread(target=_refresh_locked, daemon=True).start()
    with _lock:
        out = dict(_cache)
    out["age_sec"] = round(time.time() - (out.get("fetched_at") or 0), 1) \
        if out.get("fetched_at") else None
    return out


def refresh_now() -> Dict:
    """Fetch the account counters immediately, ignoring the polling throttle.

    Backs the UI's manual refresh button. The automatic path deliberately reaches
    the account only once a minute - and after an auth or network failure, only
    once every three - which is exactly the wrong cadence in the two moments you
    most want a number you can trust: right after a big job finished, and right
    after you re-authed and want to confirm the readout recovered. Waiting out a
    three-minute backoff to find out whether a fix worked is the whole reason
    this exists.

    Still floored at FORCE_MIN_INTERVAL so holding the button cannot turn into an
    API hammer, and serialised on _force_lock so a burst of clicks shares one
    round trip instead of starting one each. A refusal is reported as
    `throttled: True` on an otherwise normal payload rather than as an error -
    the caller still gets the current numbers, just not newer ones.
    """
    global _last_forced
    with _force_lock:
        # Re-checked INSIDE the lock: clicks that queued behind another refresh
        # want that refresh's result, not another round trip of their own.
        with _lock:
            now = time.time()
            too_soon = (now - _last_forced) < FORCE_MIN_INTERVAL
            # The endpoint 429s, and a manual refresh is the one thing that can
            # provoke it. If it has already told us to back off, honour that -
            # forcing another call inside the window earns a longer penalty and
            # returns nothing newer.
            limited = _cache.get("state") == "rate-limited" and now < _next_fetch
            retry_in = max(0.0, _next_fetch - now) if limited else 0.0
        if too_soon or limited:
            out = status()
            out["throttled"] = True
            if limited:
                out["retry_in"] = round(retry_in, 1)
            return out
        with _lock:
            _last_forced = time.time()
        _refresh_locked()
    return status()


if __name__ == "__main__":
    s = status(block=True)
    print("state=%s plan=%s tier=%s" % (s.get("state"), s.get("plan"), s.get("tier")))
    for w in s.get("windows") or []:
        left = ""
        if w["resets_at"]:
            secs = max(0, w["resets_at"] - time.time())
            left = "  resets in %dh %dm" % (secs // 3600, (secs % 3600) // 60)
        print("  %-18s %5.1f%%  %-8s%s" % (w["label"], w["pct"], w["severity"], left))
