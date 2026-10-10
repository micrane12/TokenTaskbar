"""Token Timer LAN test collector. Reads local usage; never exports chat content.

Uses only Python's standard library. No provider credentials are read.
"""
import argparse
import html
import hmac
import ipaddress
import json
import os
import re
import secrets
import socket
import sys
import threading
import time
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def timestamp(value):
    try:
        if isinstance(value, (int, float)):
            return float(value) / 1000 if value > 100_000_000_000 else float(value)
        return datetime.fromisoformat(str(value).replace("Z", "+00:00")).timestamp()
    except (ValueError, TypeError, OverflowError):
        return 0


def count(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return 0
    return max(0, int(value))


def tokens(usage, provider):
    if provider == "claude":
        return sum(count(usage.get(key)) for key in (
            "input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens"))
    # Codex input already includes cached input; reasoning is part of output.
    if "total_tokens" in usage:
        return count(usage["total_tokens"])
    return count(usage.get("input_tokens")) + count(usage.get("output_tokens"))


def window(raw, observed):
    if not isinstance(raw, dict):
        return None
    used = raw.get("used_percent")
    reset = timestamp(raw.get("resets_at"))
    if isinstance(used, bool) or not isinstance(used, (int, float)) or not 0 <= used <= 100 or reset <= 0:
        return None
    return {"used_percent": used, "resets_at": int(reset * 1000), "observed_at": int(observed * 1000)}


class Collector:
    def __init__(self, home=None, codex_home=None):
        self.home = Path(home) if home else Path.home()
        self.codex_home = Path(codex_home) if codex_home else Path(os.environ.get("CODEX_HOME", self.home / ".codex"))
        self.files = {}
        self.records = {}
        self.explicit_sessions = set()
        self.limit_readings = []
        self.found = set()
        self.lock = threading.Lock()
        self.last_scan = 0

    def ingest(self, entry, state, provider, now):
        at = timestamp(entry.get("timestamp"))
        if not at or at > now + 300:
            return
        if provider == "claude":
            if entry.get("type") != "assistant":
                return
            message = entry.get("message", {})
            usage = message.get("usage")
            if not isinstance(usage, dict):
                return
            self.found.add(provider)
            identity = message.get("id") or entry.get("uuid")
            if not identity:
                return
            key = (provider, str(identity))
            # Streaming entries can repeat a response with updated counters.
            value = tokens(usage, provider)
            previous = self.records.get(key)
            if previous is None or value >= previous[2]:
                self.records[key] = (provider, at, value, "")
            return

        payload = entry.get("payload", {})
        kind = entry.get("type")
        if kind == "session_meta":
            state["session"] = str(payload.get("id") or state["session"])
            return
        if kind == "token_usage_record" and isinstance(payload.get("usage"), dict):
            session = str(payload.get("session_id") or state["session"])
            self.explicit_sessions.add(session)
            self.found.add(provider)
            identity = payload.get("response_id") or f"{session}:{at}"
            self.records[(provider, str(identity))] = (provider, at, tokens(payload["usage"], provider), session)
            return
        if kind != "event_msg" or payload.get("type") != "token_count":
            return
        self.found.add(provider)
        limits = payload.get("rate_limits")
        if isinstance(limits, dict) and limits.get("limit_id") in (None, "codex"):
            reading = {"observed": at}
            for slot in ("primary", "secondary"):
                raw = limits.get(slot)
                if isinstance(raw, dict):
                    minutes = raw.get("window_minutes")
                    key = "five_hour" if minutes == 300 else "weekly" if minutes == 10080 else None
                    if key and (value := window(raw, at)):
                        reading[key] = value
            if len(reading) > 1:
                self.limit_readings.append(reading)
        info = payload.get("info")
        if not isinstance(info, dict) or not isinstance(info.get("total_token_usage"), dict):
            return
        total = tokens(info["total_token_usage"], provider)
        previous = state.get("total", 0)
        if total > previous:
            last = info.get("last_token_usage")
            delta = tokens(last, provider) if isinstance(last, dict) else total - previous
            session = state["session"]
            self.records[(provider, f"fallback:{session}:{total}")] = (provider, at, delta, session)
        state["total"] = total

    def scan(self, now):
        cutoff = now - 7 * 86400
        roots = [("codex", self.codex_home / "sessions"),
                 ("claude", self.home / ".claude" / "projects"),
                 ("claude", self.home / ".config" / "claude" / "projects")]
        for provider, root in roots:
            if not root.is_dir():
                continue
            for path in root.rglob("*.jsonl"):
                try:
                    stat = path.stat()
                    if stat.st_mtime < cutoff:
                        continue
                    state = self.files.setdefault(path, {"offset": 0, "session": path.stem, "total": 0, "signature": None})
                    signature = (stat.st_ino, stat.st_size, stat.st_mtime_ns)
                    if state["signature"] == signature:
                        continue
                    if stat.st_size < state["offset"] or (state["signature"] and stat.st_ino != state["signature"][0]):
                        state.update(offset=0, total=0)
                    with path.open("rb") as stream:
                        stream.seek(state["offset"])
                        while True:
                            start = stream.tell()
                            line = stream.readline()
                            if not line:
                                break
                            if not line.endswith(b"\n"):
                                stream.seek(start)
                                break  # The writer has not finished this line yet.
                            state["offset"] = stream.tell()
                            if len(line) > 4 * 1024 * 1024:
                                continue
                            # Skip prompt/tool lines before decoding JSON. Data is
                            # discarded immediately after extracting counters.
                            head = line[:800]
                            if provider == "codex":
                                match = re.search(rb'"type"\s*:\s*"([^"]+)"', head)
                                if not match or match[1] not in (b"session_meta", b"token_usage_record", b"event_msg"):
                                    continue
                                if match[1] == b"event_msg" and not re.search(rb'"type"\s*:\s*"token_count"', head):
                                    continue
                            elif not re.search(rb'"type"\s*:\s*"assistant"', line):
                                continue
                            try:
                                self.ingest(json.loads(line), state, provider, now)
                            except (ValueError, TypeError, AttributeError, OverflowError):
                                continue
                    state["signature"] = signature
                except (OSError, PermissionError):
                    continue
        self.records = {key: value for key, value in self.records.items() if value[1] >= cutoff}
        self.limit_readings = sorted((r for r in self.limit_readings if r["observed"] >= cutoff),
                                     key=lambda r: r["observed"])[-32:]
        self.last_scan = now

    def snapshot(self, now=None):
        now = now or time.time()
        with self.lock:
            if now - self.last_scan >= 2:
                self.scan(now)
            providers = []
            for provider, name in (("codex", "Codex"), ("claude", "Claude")):
                records = [r for key, r in self.records.items() if r[0] == provider and
                           not (key[1].startswith("fallback:") and r[3] in self.explicit_sessions)]
                reading = self.limit_readings[-1] if provider == "codex" and self.limit_readings else {}
                providers.append({"id": provider, "name": name, "found": provider in self.found,
                                  "tokens_5h": sum(r[2] for r in records if now - 5 * 3600 <= r[1] <= now),
                                  "tokens_week": sum(r[2] for r in records if now - 7 * 86400 <= r[1] <= now),
                                  "five_hour": reading.get("five_hour"), "weekly": reading.get("weekly")})
            return {"schema": 1, "generated_at": int(now * 1000), "providers": providers}


def private_address():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        try:
            probe.connect(("192.0.2.1", 9))  # Routing lookup only; no payload is sent.
            address = probe.getsockname()[0]
            ip = ipaddress.ip_address(address)
            if not ip.is_loopback and any(ip in ipaddress.ip_network(net) for net in
                                         ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")):
                return address
        except OSError:
            pass
    raise RuntimeError("Connect this computer to private Wi-Fi, or supply --host with its private IPv4 address.")


def make_server(host, port, secret, collector):
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            supplied = self.headers.get("Authorization", "")
            if not hmac.compare_digest(supplied, "Bearer " + secret):
                self.send_error(401)
                return
            if self.path != "/snapshot":
                self.send_error(404)
                return
            body = json.dumps(collector.snapshot(), separators=(",", ":")).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *_):
            pass  # Never log the pairing token, request headers, or usage.

    return ThreadingHTTPServer((host, port), Handler)


def main():
    parser = argparse.ArgumentParser(description="Token Timer private-network desktop collector")
    parser.add_argument("--host", help="Private IPv4 interface to bind")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--summary", action="store_true", help="Print counters only, without starting a server")
    args = parser.parse_args()
    collector = Collector()
    if args.summary:
        print(json.dumps(collector.snapshot(), indent=2))
        return
    host = args.host or private_address()
    ip = ipaddress.ip_address(host)
    if not any(ip in ipaddress.ip_network(net) for net in ("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16")):
        parser.error("Use a private IPv4 address; this test collector is for trusted LANs only.")
    if not 1024 <= args.port <= 65535:
        parser.error("Port must be between 1024 and 65535")
    secret = secrets.token_urlsafe(32)
    snapshot = collector.snapshot()
    found = ", ".join(p["name"] for p in snapshot["providers"] if p["found"]) or "No supported records yet"
    server = make_server(host, args.port, secret, collector)
    print("\nTOKEN TIMER - PHONE TEST\n", flush=True)
    print("Detected: " + found, flush=True)
    print("Paste this whole link into Token Timer on your phone:\n", flush=True)
    print(f"http://{host}:{args.port}/snapshot#{secret}\n", flush=True)
    pairing_page = None
    try:
        import qrcode
        from qrcode.image.svg import SvgPathImage
        link = f"http://{host}:{args.port}/snapshot#{secret}"
        qr = qrcode.make(link, image_factory=SvgPathImage, border=4)
        folder = Path(sys.executable).parent if getattr(sys, "frozen", False) else Path(__file__).parent
        pairing_page = folder / "TokenTimer-pairing.html"
        pairing_page.write_text(
            '<!doctype html><meta charset="utf-8"><title>Token Timer pairing</title>'
            '<style>body{font:18px system-ui;background:#091015;color:#edf1f5;max-width:620px;margin:50px auto;padding:24px}'
            'svg{width:320px;height:320px;background:white;border-radius:16px}code{word-break:break-all;color:#85aeff}'
            '</style><h1>Connect your phone.</h1><p>Open Token Timer and tap <b>Scan pairing QR</b>.</p>'
            + qr.to_string().decode() + '<p>Or copy this pairing link:</p><code>' + html.escape(link)
            + '</code><p>Keep the collector running and both devices on the same trusted private Wi-Fi.</p>'
            '<p>This is an unencrypted local-network test connection. This code expires when the collector stops.</p>',
            encoding="utf-8")
        print("For QR pairing, open this file on your computer:", flush=True)
        print(str(pairing_page) + "\n", flush=True)
    except (ImportError, OSError):
        print("QR page unavailable; use the pairing link above.\n", flush=True)
    print("Keep this window open and use the same trusted private Wi-Fi on your phone.", flush=True)
    print("This test link uses unencrypted LAN transport. Do not expose the port to the internet.", flush=True)
    print("If Windows asks, allow access on Private networks only. No provider keys are read.", flush=True)
    print("Restarting changes the pairing code. Press Ctrl+C to stop.\n", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        if pairing_page:
            try:
                pairing_page.unlink(missing_ok=True)
            except OSError:
                pass


if __name__ == "__main__":
    main()
