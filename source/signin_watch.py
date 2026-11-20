"""Watch Claude Code's sign-in file and record what happens to it. Read-only.

Claude Code keeps two expiries in ~/.claude/.credentials.json: an access token
(about 8 hours, renewed whenever Claude Code runs) and the long-lived sign-in
(refreshTokenExpiresAt; once it passes, a real /login is needed). This module
reads both, reports the sign-in expiry for the usage panel, and appends one
line to data/claude-signin.log whenever the file changes:

  renewed     new access token, same sign-in expiry (normal, ~every 8 h)
  signed in   the sign-in expiry moved: someone ran /login
  signed out  the file or its refresh token disappeared
  early       a sign-out or new sign-in while the old sign-in had not expired

"Early" events are the ones worth investigating; each line also lists the
Claude processes that were running, to spot two programs renewing at once.
Tokens are never logged or stored; only an 8-character hash is kept to tell
one refresh token from the next.
"""
import hashlib
import json
import os
import subprocess
import time
from pathlib import Path

import cc_account

LOG_LIMIT = 256 * 1024  # bytes; older half is dropped past this


def read_credentials():
    """Returns a summary dict, None when the file is missing, or "unreadable"."""
    path = cc_account.CRED_PATH
    if not os.path.exists(path):
        return None
    try:
        with open(path, encoding="utf-8") as f:
            oauth = (json.load(f) or {}).get("claudeAiOauth") or {}
    except (OSError, ValueError, AttributeError):
        return "unreadable"  # Claude Code may be mid-write; try again next tick.

    def seconds(value):
        try:
            return float(value) / 1000.0
        except (TypeError, ValueError):
            return 0.0
    refresh = oauth.get("refreshToken") or ""
    return {"refresh": hashlib.sha256(refresh.encode("utf-8")).hexdigest()[:8] if refresh else "",
            "expires": seconds(oauth.get("expiresAt")),
            "login_expires": seconds(oauth.get("refreshTokenExpiresAt"))}


def claude_processes():
    """Image names of running Claude programs, e.g. 'claude.exe x2, Claude.exe x5'."""
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True,
                             timeout=10, errors="replace",
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)).stdout
    except (OSError, subprocess.SubprocessError):
        return "unknown"
    counts = {}
    for line in out.splitlines():
        name = line.split(",", 1)[0].strip('"')
        if "claude" in name.lower() and not name.lower().startswith("tokentaskbar"):
            counts[name] = counts.get(name, 0) + 1
    return ", ".join("%s x%d" % item for item in sorted(counts.items())) or "none"


def _date(stamp):
    return time.strftime("%a %d %b %H:%M", time.localtime(stamp)) if stamp else "unknown"


def classify(old, new, now):
    """Describe a change between two summaries (None = file missing)."""
    if new is None or not new["refresh"]:
        early = old and old["login_expires"] > now
        return ("signed out EARLY (sign-in was valid until %s)" % _date(old["login_expires"])
                if early else "signed out")
    if old is None or not old["refresh"]:
        return "signed in · valid until %s" % _date(new["login_expires"])
    if abs(new["login_expires"] - old["login_expires"]) > 60:
        if old["login_expires"] > now + 3600:
            return "signed in again EARLY (old sign-in was valid until %s) · now valid until %s" % (
                _date(old["login_expires"]), _date(new["login_expires"]))
        return "signed in · valid until %s" % _date(new["login_expires"])
    if new["refresh"] != old["refresh"] or abs(new["expires"] - old["expires"]) > 60:
        return "renewed · token until %s" % _date(new["expires"])
    return ""


class SignInWatch:
    def __init__(self, data_dir):
        self.state_path = Path(data_dir) / "claude-signin.json"
        self.log_path = Path(data_dir) / "claude-signin.log"
        self.known = "unset"   # summary, None (missing) or "unset" before the first read
        self.missing_ticks = 0
        self.last_event = ""
        self.last_event_at = 0.0
        try:
            saved = json.loads(self.state_path.read_text(encoding="utf-8"))
            self.known = saved.get("known", "unset")
            self.last_event = str(saved.get("last_event") or "")
            self.last_event_at = float(saved.get("last_event_at") or 0)
        except (OSError, ValueError, TypeError, AttributeError):
            pass

    def _save(self):
        try:
            self.state_path.parent.mkdir(parents=True, exist_ok=True)
            self.state_path.write_text(json.dumps({"known": self.known, "last_event": self.last_event,
                                                   "last_event_at": self.last_event_at}), encoding="utf-8")
        except OSError:
            pass

    def _log(self, now, text, note=""):
        line = "%s  %s%s  [Claude programs running: %s]\n" % (
            time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(now)), text, note, claude_processes())
        try:
            self.log_path.parent.mkdir(parents=True, exist_ok=True)
            if self.log_path.exists() and self.log_path.stat().st_size > LOG_LIMIT:
                kept = self.log_path.read_text(encoding="utf-8", errors="replace")[-LOG_LIMIT // 2:]
                self.log_path.write_text(kept[kept.find("\n") + 1:], encoding="utf-8")
            with open(self.log_path, "a", encoding="utf-8") as f:
                f.write(line)
        except OSError:
            pass

    def check(self, now=None, renewing=False):
        """Called once per worker tick. Returns the fields for the Claude provider."""
        now = now or time.time()
        current = read_credentials()
        if current == "unreadable":
            return self.info()
        if current is None and self.known not in (None, "unset"):
            self.missing_ticks += 1
            if self.missing_ticks < 2:   # tolerate a replace-in-progress
                return self.info()
        self.missing_ticks = 0
        if self.known == "unset":
            self.known = current
            self._log(now, "watching · " + (classify(None, current, now) if current else "no sign-in file"))
            self._save()
        elif current != self.known:
            text = classify(self.known, current, now)
            self.known = current
            if text:
                self.last_event, self.last_event_at = text.split(" · ")[0].split(" (")[0], now
                self._log(now, text, " (by TokenTaskbar's renewal)" if renewing else "")
            self._save()
        return self.info()

    def info(self):
        known = self.known if isinstance(self.known, dict) else None
        return {"loginExpires": known["login_expires"] if known and known["refresh"] else 0,
                "signinEvent": self.last_event, "signinEventAt": self.last_event_at}
