"""Renew an expired Claude Code sign-in by running Claude Code once. Opt-in.

Claude Code's access token lasts about 8 hours and only Claude Code renews it,
whenever it runs. This module never touches the credentials itself: it runs the
smallest possible Claude Code request (Haiku, one-character reply, no tools, MCP
or settings; about 600 tokens) and lets Claude Code renew its own sign-in.

It runs only when ALL of these hold:
  * "Renew Claude sign-in" is on in Settings (RenewClaude, off by default);
  * the stored access token has expired, so Claude Code has not run since;
  * the long-lived sign-in has not expired (that needs a real sign-in);
  * no attempt in the last MIN_GAP seconds, persisted across restarts.
Success means the credentials file now holds an unexpired token.
"""
import json
import os
import shutil
import subprocess
import tempfile
import threading
import time
from pathlib import Path

import cc_account

MIN_GAP = 8 * 3600.0
TIMEOUT = 120
PING = ["-p", ".", "--model", "haiku", "--system-prompt", "Reply with one character.",
        "--tools", "", "--strict-mcp-config", "--setting-sources", "",
        "--no-session-persistence", "--output-format", "json"]
PLAIN = ["-p", ".", "--model", "haiku", "--output-format", "json"]  # older Claude Code

REFRESH_HINT = "Session expired · Reconnect in Claude Code"
SIGN_IN_HINT = "Sign-in required · Reconnect Claude"


def _credentials():
    try:
        with open(cc_account.CRED_PATH, encoding="utf-8") as f:
            oauth = (json.load(f) or {}).get("claudeAiOauth") or {}
    except (OSError, ValueError, AttributeError):
        return None
    def seconds(value):
        try:
            return float(value) / 1000.0
        except (TypeError, ValueError):
            return 0.0
    return {"expires": seconds(oauth.get("expiresAt")),
            "login_expires": seconds(oauth.get("refreshTokenExpiresAt"))}


def token_valid(now=None):
    cred = _credentials()
    return bool(cred and cred["expires"] > (now or time.time()))


def find_cli():
    found = shutil.which("claude")
    if found:
        return found
    home = Path.home()
    for candidate in (Path(os.environ.get("APPDATA", "")) / "npm" / "claude.cmd",
                      home / ".local" / "bin" / "claude.exe",
                      home / ".claude" / "local" / "claude.exe"):
        if candidate.is_file():
            return str(candidate)
    return None


def _run(cli, args):
    folder = Path(tempfile.gettempdir()) / "tokentaskbar-renew"
    folder.mkdir(exist_ok=True)  # Empty folder: no project CLAUDE.md is loaded.
    try:
        result = subprocess.run([cli] + args, cwd=folder, capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=TIMEOUT,
                                stdin=subprocess.DEVNULL,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                                env=dict(os.environ, MAX_THINKING_TOKENS="0",
                                         DISABLE_NON_ESSENTIAL_MODEL_CALLS="1"))
        return (result.stdout or "") + (result.stderr or "")
    except (OSError, subprocess.SubprocessError):
        return ""


class Renewer:
    def __init__(self, state_path, settings_path):
        self.state_path = Path(state_path)
        self.settings_path = Path(settings_path)
        self.running = False
        self.result = ""
        self.last_attempt = 0.0
        try:
            saved = json.loads(self.state_path.read_text(encoding="utf-8"))
            self.last_attempt = float(saved.get("last_attempt") or 0)
            self.result = str(saved.get("result") or "")
        except (OSError, ValueError, TypeError, AttributeError):
            pass

    def enabled(self):
        try:
            return json.loads(self.settings_path.read_text(encoding="utf-8-sig")).get("RenewClaude") is True
        except (OSError, ValueError, AttributeError):
            return False

    def _save(self):
        try:
            self.state_path.parent.mkdir(parents=True, exist_ok=True)
            self.state_path.write_text(json.dumps({"last_attempt": self.last_attempt,
                                                   "result": self.result}), encoding="utf-8")
        except OSError:
            pass

    def _renew(self, cli):
        try:
            output = _run(cli, PING)
            if not token_valid() and "unknown option" in output.lower():
                output = _run(cli, PLAIN)
            if token_valid():
                self.result = "ok"  # cc_account notices the new credentials and reads at once.
            else:
                text = output.lower()
                self.result = "signed-out" if ("login" in text or "log in" in text) else "failed"
            self._save()
        finally:
            self.running = False

    def check(self, account, now=None):
        """Called once per worker tick. Returns the status note for the Claude chip."""
        now = now or time.time()
        if self.running:
            return "Renewing Claude sign-in…"
        if (account or {}).get("state") != "expired":
            return ""
        cred = _credentials()
        if cred and cred["expires"] > now:
            return ""  # Claude Code already renewed it; cc_account picks that up.
        if not cred or (cred["login_expires"] and cred["login_expires"] <= now):
            return SIGN_IN_HINT
        if not self.enabled():
            return REFRESH_HINT
        if now - self.last_attempt < MIN_GAP:
            return SIGN_IN_HINT if self.result == "signed-out" else REFRESH_HINT
        cli = find_cli()
        self.last_attempt = now  # Persist first so a crash can never cause a loop.
        if not cli:
            self.result = "no-cli"
            self._save()
            return REFRESH_HINT
        self.result = "running"
        self._save()
        self.running = True
        threading.Thread(target=self._renew, args=(cli,), daemon=True).start()
        return "Renewing Claude sign-in…"
