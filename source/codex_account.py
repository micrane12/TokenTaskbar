"""codex_account.py - OpenAI Codex rate-limit windows, the numbers `/status` shows.

The sibling of cc_account.py for the OTHER agent the deck runs. Codex CLI signs
in with a ChatGPT account and is metered on rolling windows exactly like Claude
Code is (a short window plus a weekly one on paid plans; the free tier is a single
30-day pool). This module reads those percentages so the footer chip can show
both accounts side by side.

Two sources, same order of preference as the Claude side:

* live - `GET https://chatgpt.com/backend-api/wham/usage` with the OAuth access
  token Codex stored in ~/.codex/auth.json. Same numbers `/status` prints.
* local - the `rate_limits` block Codex appends to every `token_count` event in
  ~/.codex/sessions/<y>/<m>/<d>/rollout-*.jsonl. It is a snapshot as of the
  last Codex turn, not "now", so it is labelled as such - but it costs nothing,
  needs no network, and survives an expired token.

What we do NOT do
-----------------
We never refresh or write auth.json. Codex rotates the refresh token on use, so
racing the CLI for it would log the user out of their own Codex. We use the
access token until its JWT `exp` passes and then fall back to the rollout
snapshot until Codex itself rewrites the file.

Normalised window entries (the UI renders from this list, never from the raw
payload, so the two accounts share one renderer):

    {"key": "primary" | "secondary", "label": "5h" | "week" | "30d",
     "short": "5h" | "wk" | "30d", "pct": 15.0, "severity": "normal"|"warning",
     "resets_at": epoch | None, "window_sec": 18000, "active": True}

Stdlib only, thread-safe, cached. The UI polls every few seconds; we hit the
account at most once a minute and always answer from cache.
"""
from __future__ import annotations

import base64
import glob
import json
import os
import threading
import time
from datetime import datetime, timezone
from typing import Dict, List, Optional
from urllib import error as _urlerror
from urllib import request as _urlrequest

CODEX_HOME = os.environ.get("CODEX_HOME") or os.path.join(os.path.expanduser("~"), ".codex")
AUTH_PATH = os.path.join(CODEX_HOME, "auth.json")
SESSIONS_ROOT = os.path.join(CODEX_HOME, "sessions")
USAGE_URL = "https://chatgpt.com/backend-api/wham/usage"

REFRESH_SECONDS = 60.0
ERROR_BACKOFF = 180.0
FORCE_MIN_INTERVAL = 15.0     # see cc_account.refresh_now for why this floor exists
TIMEOUT = 8.0
# Only rollouts written this recently are searched for a fallback snapshot; an
# older one would be describing windows that have long since reset.
LOCAL_LOOKBACK_DAYS = 3
_TAIL_BYTES = 256 * 1024      # the rate_limits block is in the last token_count line

_lock = threading.RLock()
_cache: Dict = {"ok": False, "state": "loading", "fetched_at": 0.0, "windows": []}
_next_fetch = 0.0
_inflight = False
_force_lock = threading.Lock()
_last_forced = 0.0


# ── credentials (read-only) ──────────────────────────────────────────────────
def _jwt_exp(tok: str) -> float:
    """`exp` claim of an unverified JWT, or 0.0 when it cannot be read. We are
    not trusting the token - only deciding whether to bother sending it."""
    try:
        payload = tok.split(".")[1]
        payload += "=" * (-len(payload) % 4)
        return float(json.loads(base64.urlsafe_b64decode(payload)).get("exp") or 0)
    except Exception:
        return 0.0


def _read_token() -> Optional[Dict]:
    try:
        with open(AUTH_PATH, encoding="utf-8") as f:
            raw = json.load(f) or {}
    except (OSError, ValueError):
        return None
    toks = raw.get("tokens") or {}
    tok = toks.get("access_token")
    if not tok:
        return None
    return {"token": tok, "account_id": toks.get("account_id") or "",
            "expires_at": _jwt_exp(tok), "auth_mode": raw.get("auth_mode") or ""}


# ── payload normalisation ────────────────────────────────────────────────────
def _labels(window_sec: Optional[float]):
    """(long, short) for a window length. Codex names them by duration, not
    purpose, so "5h" / "week" / "30d" is what /status itself prints."""
    if not window_sec:
        return "window", "?"
    h = window_sec / 3600.0
    if h < 24:
        n = int(round(h))
        return "%dh" % n, "%dh" % n
    d = int(round(h / 24.0))
    if d == 7:
        return "week", "wk"
    return "%dd" % d, "%dd" % d


def _window(key: str, w: Optional[Dict], now: float, limit_reached: bool) -> Optional[Dict]:
    if not isinstance(w, dict):
        return None
    try:
        pct = round(float(w.get("used_percent") or 0), 1)
    except (TypeError, ValueError):
        return None
    # The live payload speaks seconds; the rollout snapshot speaks minutes.
    sec = w.get("limit_window_seconds")
    if sec is None and w.get("window_minutes") is not None:
        sec = float(w["window_minutes"]) * 60.0
    resets = w.get("reset_at") or w.get("resets_at")
    if resets is None and w.get("reset_after_seconds") is not None:
        resets = now + float(w["reset_after_seconds"])
    long, short = _labels(sec)
    sev = "normal"
    if pct >= 100 or (limit_reached and key == "primary"):
        sev = "critical"
    elif pct >= 80:
        sev = "warning"
    return {"key": key, "label": long, "short": short, "pct": pct,
            "severity": sev, "resets_at": float(resets) if resets else None,
            "window_sec": sec, "active": True}


def _normalise_live(raw: Dict) -> List[Dict]:
    rl = raw.get("rate_limit") or {}
    reached = bool(rl.get("limit_reached"))
    now = time.time()
    out = [_window("primary", rl.get("primary_window"), now, reached),
           _window("secondary", rl.get("secondary_window"), now, reached)]
    return [w for w in out if w]


def _normalise_local(rl: Dict) -> List[Dict]:
    reached = bool(rl.get("rate_limit_reached_type"))
    now = time.time()
    out = [_window("primary", rl.get("primary"), now, reached),
           _window("secondary", rl.get("secondary"), now, reached)]
    return [w for w in out if w]


def _credits(c) -> Optional[Dict]:
    if not isinstance(c, dict) or not c.get("has_credits"):
        return None
    return {"balance": c.get("balance"), "unlimited": bool(c.get("unlimited"))}


# ── local fallback: last rate_limits snapshot in the newest rollout ──────────
def _newest_rollouts() -> List[str]:
    if not os.path.isdir(SESSIONS_ROOT):
        return []
    cutoff = time.time() - LOCAL_LOOKBACK_DAYS * 86400
    out = []
    for fp in glob.glob(os.path.join(SESSIONS_ROOT, "*", "*", "*", "rollout-*.jsonl")):
        try:
            mt = os.path.getmtime(fp)
        except OSError:
            continue
        if mt >= cutoff:
            out.append((mt, fp))
    out.sort(reverse=True)
    return [fp for _, fp in out]


def _last_rate_limits(fp: str):
    """(rate_limits dict, event epoch) from the tail of one rollout, or None."""
    try:
        size = os.path.getsize(fp)
        with open(fp, "rb") as f:
            f.seek(max(0, size - _TAIL_BYTES))
            data = f.read()
    except OSError:
        return None
    for raw in reversed(data.split(b"\n")):
        if b'"rate_limits"' not in raw:
            continue
        try:
            o = json.loads(raw.decode("utf-8", "replace"))
        except ValueError:
            continue
        rl = (o.get("payload") or {}).get("rate_limits")
        if isinstance(rl, dict) and (rl.get("primary") or rl.get("secondary")):
            ts = o.get("timestamp") or ""
            return rl, _iso_epoch(ts)
    return None


def _iso_epoch(ts: str) -> float:
    """Codex's rollout timestamps are UTC ("2026-09-17T05:42:00.764Z")."""
    try:
        return datetime.strptime(ts[:19], "%Y-%m-%dT%H:%M:%S")             .replace(tzinfo=timezone.utc).timestamp()
    except Exception:
        return 0.0


def local_snapshot() -> Optional[Dict]:
    """The rate-limit block from the most recent Codex turn on disk."""
    for fp in _newest_rollouts()[:5]:
        hit = _last_rate_limits(fp)
        if hit:
            rl, ts = hit
            return {"windows": _normalise_local(rl), "plan": rl.get("plan_type") or "",
                    "credits": _credits(rl.get("credits")), "as_of": ts, "source": fp}
    return None


# ── fetch ────────────────────────────────────────────────────────────────────
def _fetch() -> Dict:
    cred = _read_token()
    if not cred:
        return {"ok": False, "state": "no-credentials",
                "detail": "no ~/.codex/auth.json - run `codex` once to sign in"}
    if cred["auth_mode"] and cred["auth_mode"] != "chatgpt":
        return {"ok": False, "state": "api-key",
                "detail": "Codex is using an API key, which has no rate-limit windows"}
    if cred["expires_at"] and cred["expires_at"] <= time.time():
        return {"ok": False, "state": "expired",
                "detail": "the stored Codex token has expired; run any codex "
                          "command to refresh it"}
    hdr = {"Authorization": "Bearer " + cred["token"],
           "User-Agent": "chat-bridge"}
    if cred["account_id"]:
        hdr["ChatGPT-Account-Id"] = cred["account_id"]
    req = _urlrequest.Request(USAGE_URL, headers=hdr)
    try:
        with _urlrequest.urlopen(req, timeout=TIMEOUT) as r:
            raw = json.loads(r.read().decode("utf-8", "replace"))
    except _urlerror.HTTPError as e:
        if e.code == 429:
            try:
                retry = float(e.headers.get("Retry-After") or 0)
            except (TypeError, ValueError):
                retry = 0.0
            return {"ok": False, "state": "rate-limited",
                    "retry_after": retry or ERROR_BACKOFF,
                    "detail": "HTTP 429 - the usage API is rate-limiting"}
        state = "expired" if e.code in (401, 403) else "http-error"
        return {"ok": False, "state": state, "detail": "HTTP %d" % e.code}
    except Exception as e:
        return {"ok": False, "state": "offline", "detail": str(e)[:120]}
    if not isinstance(raw, dict) or "rate_limit" not in raw:
        return {"ok": False, "state": "http-error", "detail": "unexpected payload"}
    return {"ok": True, "state": "ok", "windows": _normalise_live(raw),
            "plan": raw.get("plan_type") or "", "credits": _credits(raw.get("credits")),
            "limit_reached": bool((raw.get("rate_limit") or {}).get("limit_reached")),
            "fetched_at": time.time(), "local": False}


def _refresh_locked() -> None:
    global _cache, _next_fetch, _inflight
    res = _fetch()
    now = time.time()
    with _lock:
        _inflight = False
        _next_fetch = now + (REFRESH_SECONDS if res.get("ok")
                             else float(res.get("retry_after") or ERROR_BACKOFF))
        if res.get("ok"):
            _cache = res
            return
        prev = _cache if _cache.get("ok") and not _cache.get("local") else {}
        if prev:
            # Keep the last live numbers, marked stale.
            _cache = dict(prev)
            _cache.update({"state": res["state"], "detail": res.get("detail", ""),
                           "stale": True, "retry_at": _next_fetch})
            return
        # No live reading at all: the rollout snapshot is better than nothing,
        # and is labelled `local` so the UI can say "as of the last Codex turn".
        snap = None
        try:
            snap = local_snapshot()
        except Exception:
            snap = None
        if snap and snap["windows"]:
            _cache = {"ok": True, "state": res["state"], "detail": res.get("detail", ""),
                      "local": True, "windows": snap["windows"], "plan": snap["plan"],
                      "credits": snap["credits"], "fetched_at": snap["as_of"] or now,
                      "retry_at": _next_fetch}
        else:
            _cache = {"ok": False, "state": res["state"], "detail": res.get("detail", ""),
                      "windows": [], "fetched_at": 0.0, "retry_at": _next_fetch}


def status(block: bool = False) -> Dict:
    """Cached Codex usage. Never blocks a request unless `block` is set."""
    global _inflight
    with _lock:
        due = time.time() >= _next_fetch and not _inflight
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
    """Fetch immediately, ignoring the polling throttle - the manual ↻ button.
    Same floor and serialisation as cc_account.refresh_now, same `throttled`
    contract on refusal."""
    global _last_forced
    with _force_lock:
        with _lock:
            now = time.time()
            too_soon = (now - _last_forced) < FORCE_MIN_INTERVAL
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
    print("state=%s plan=%s local=%s" % (s.get("state"), s.get("plan"), s.get("local")))
    for w in s.get("windows") or []:
        left = ""
        if w["resets_at"]:
            secs = max(0, w["resets_at"] - time.time())
            left = "  resets in %dh %dm" % (secs // 3600, (secs % 3600) // 60)
        print("  %-8s %5.1f%%  %-8s%s" % (w["label"], w["pct"], w["severity"], left))
