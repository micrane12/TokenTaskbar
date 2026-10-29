# TokenTaskbar

Portable Windows 11 x64 taskbar widget for Claude and Codex account usage.

## Run on another Windows PC

1. Extract the entire ZIP into a writable folder.
2. Run `TokenTaskbar.exe`. Keep `TokenTaskbar.Collector.exe` and `_internal` beside it.
3. Claude and Codex appear together. Each compact chip shows five-hour usage and reset time, followed by weekly usage and reset time. Close panel keeps the widget running; Quit widget exits it. Running the executable again opens the existing panel.

Python and Windhawk do not need to be installed. The included collector carries its Python runtime; the UI uses Windows' .NET Framework. Target: Windows 11 x64, standard horizontal primary taskbar. ARM64, other Windows versions, secondary monitors, and customized taskbars have not been verified. This unsigned development build may receive a Windows reputation prompt.

Each PC uses its **own** existing Codex / Claude Code sign-in and local usage records. This package contains no account credentials, chat histories, or usage snapshots. Monitoring another PC from this PC is not implemented. Startup registration is optional through Settings. No installer or firewall rule is required.

## Structure

- `source/TokenTaskbar.cs`: native Windows Forms app, simultaneous provider chips, usage popup, taskbar attachment and layout-gap calculation.
- `source/worker.py`: background data coordinator; sends summary JSON to the app over a private stdout pipe.
- `source/collector.py`: copied from your Token Timer project; reads local token counters.
- `source/cc_account.py` and `codex_account.py`: copied from your Chat Bridge project; read provider account percentages with caching and backoff.
- `source/build.ps1`: reproducible UI build and collector packaging.
- `source/test_*.py`: existing collector tests plus quota validation tests.

The taskbar uses the chosen minimal lanes design, with orange Claude and blue Codex bars. Both agents remain visible. Percentages sit immediately to the right of their bars, vertically centred; reset countdowns sit below. The 5h/Week labels are removed from the taskbar: the first pool is the short window and the second is weekly. The detail panel and tooltip identify each pool. Official app icons are embedded in the executable. Bars mean **quota used**. Account quotas and this-PC token totals are separate measurements. `*` marks a last-known quota. `—` means no valid reading; weekly or model-specific data never substitutes for a missing five-hour pool.

When Chat Bridge is running on this PC at its default localhost port, the collector shares its authoritative account counters instead of issuing duplicate provider requests. Bridge estimates are never used. Without Bridge, the included readers operate independently. Last-known account counters and retry deadlines are retained in `data/quota-cache.json`; stale data is labelled, and expired windows are hidden. Provider credentials and chat text are not stored there or included in the portable ZIP.

## Verified and unfinished

Verified: C# compilation; standalone collector packaging; seventeen Python tests; layout gap/overlap tests; live Codex responses and shared last-known Claude counters. Regression tests cover both Claude response formats, missing/scoped pools, inactive-but-reported pools, shared-source fallback and cache contents. Inactive pool metadata does not invalidate an otherwise usable reported counter and reset time. Claude's provider endpoint is currently rate-limited; its Bridge cache supplies the last-known readings with an explicit stale label.

The taskbar widget uses a per-pixel transparent layered child window rather than painting a rectangular background. It attaches inside the Windows.UI.Composition.DesktopWindowContentBridge content host, and maintains its child z-order without becoming globally topmost. The current build was seen on the desktop with the taskbar background visible through it. The widget scales to your size preference and taskbar height. The drawing surface uses 4× supersampling, grayscale text antialiasing and 256px source icons; transparency/antialiasing checks pass at normal and 150% scale.

The app runs independently of the taskbar child-window lifetime. Three forced replacements of its own HWND recovered in the same process and returned to the correct parent and screen position. Layout scans that fail during transitions retain the previous slot; reliable reports of insufficient space must persist before activating the tray fallback. Reparenting no longer hides the widget first. **The exact rapid Windhawk fullscreen toggling still needs a visual retest**: the recovery test simulates child-window replacement, not every Windhawk animation. Auto-hide and multi-monitor checks also remain.

The current approach embeds a separate app window into unused taskbar space. It does not reserve space or make Windows reflow its buttons. If no safe gap is found, it hides the element and leaves a tray fallback. Whether this approach is robust enough without Windhawk remains an integration question.

See `RELEASING.md` for the build and GitHub release process.

## Build

On a Windows x64 development PC with Python 3.11+ and PyInstaller installed, run `source/build.ps1` in PowerShell. The script uses the Windows .NET Framework C# compiler. Building needs Python; using the packaged app does not.

Run `python -m unittest discover -s source -v` from this folder. Run `TokenTaskbar.exe --test` for gap-placement, overlap, alpha transparency, antialiasing, scaled rendering and icon assertions; it writes `layout-test.txt`. With the regular widget closed, `--test-recovery` runs an approximately 19-second check that replaces only its own HWND three times, verifies same-process reattachment, writes `recovery-test.txt` and quits. `--diagnose` writes `taskbar-host.txt` showing the native taskbar ancestry and widget position. `lifecycle.log` records process/window lifecycle events to distinguish real exits from hiding. These diagnostics and personal usage cache are excluded from the ZIP. Any older settings.json is ignored; preferences are set in widget-settings.json through the Settings panel.

The reused Bridge account readers use provider usage endpoints that may change. They read existing access tokens for requests to their respective provider, but do not refresh, rotate, or export credentials. They are copied modules rather than live dependencies on `C:\Dev`.

## Widget settings

Usage and settings share one window with **Usage** and **Settings** tabs. Clicking the widget or tray icon opens the Usage tab; right-clicking either and choosing Settings opens the Settings tab. The window opens above the widget.

- **Show on taskbar** turns the taskbar widget off without quitting. Token Taskbar keeps running from its tray icon; turn the switch back on (and Save) to bring the widget back. Quit widget on the Usage tab exits the app.

- **Size** (70-125%, 5% steps), **Position** (Left/Center/Right) and **Nudge** (±400 px; arrows move 10 px, Shift+arrows 1 px) change the widget on the taskbar as you adjust them. Size is capped to taskbar height, and position uses the nearest unoccupied gap.
- A status line reports where the widget landed: placed as asked, moved to the nearest free space, or in the tray because no gap is wide enough.
- **Show provider logos** hides the Claude and Codex logos on the taskbar widget, making it about 31 px narrower per provider. The usage window keeps its logos.
- **Show Claude / Show Codex** hide one provider on the taskbar, which halves the widget's width. At least one stays on. The usage panel always shows both.
- **Start with Windows** adds or removes a Startup-folder shortcut. If the app is moved or a newer ZIP is extracted to a different folder, the next launch repoints an existing shortcut at the running executable.

Changes are previews until you click **Save**; the footer shows "Not saved yet" meanwhile. **Undo changes**, Esc or closing the window returns the taskbar to the saved settings. Switching tabs keeps unsaved changes. **Reset to defaults** previews the defaults (Start with Windows unchanged); Save keeps them. Settings are stored locally in widget-settings.json beside the executable.

## Claude session renewal

Claude's access token can expire while the PC is off even though the saved login is still usable. Click **Reconnect** in the Claude card to open Claude Code's `/usage` screen. Claude Code renews its own saved session; follow its sign-in instructions only if the saved session is no longer accepted. Use `/exit` when finished. The widget detects changed credentials on its next 10-second poll and retries without waiting out the previous authentication backoff. It still respects API rate limits.

Reconnect also finds standard native and npm installations when the Windows startup PATH is missing Claude Code. `CLAUDE_CONFIG_DIR` is respected by the standalone Claude reader. Authentication failures cached by an older Chat Bridge fall back to the standalone reader. Offline, expired-session, rejected-login and forbidden-access states are displayed separately, with the age of any retained reading.

Renewal is user-initiated, not unattended at boot: the CLI has no dedicated silent-renew command. TokenTaskbar never rotates or writes Claude Code credentials or sends a model prompt to renew them.

## Updating the application

From v0.1.7 onward, open Settings and click Check for updates. Save or undo pending settings first. A progress window downloads and verifies the latest release, updates files in place, and restarts the widget. Your saved preferences remain. Existing versions need one manual ZIP update to v0.1.7 to obtain the updater. The app folder must be writable. Failed installations retain a backup in the temporary update folder.
