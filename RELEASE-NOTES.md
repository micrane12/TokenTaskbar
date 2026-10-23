# TokenTaskbar preview

Windows 11 x64 taskbar usage display for Claude and Codex, with a usage panel, reset countdowns and local reset times, size and position settings, a startup toggle, Renew login and Refresh controls.

Download the Windows ZIP, extract all files into a writable folder, and run TokenTaskbar.exe. No Python or Windhawk installation is required. Existing Claude Code and Codex CLI sign-ins are required for account usage. Use the tray icon to open the panel.

This is an unsigned preview. Taskbar customization, auto-hide and secondary monitors have not been fully verified. It uses free taskbar space and falls back to the tray when space is unavailable. Each PC reads its own accounts and local token records; remote PC monitoring is not included.

The archive contains no credentials, usage cache or personal settings. SHA-256 checksums are provided alongside the ZIP.

## v0.1.1

Visible widget icons, text and bars now open Settings directly; transparent empty space remains click-through. Settings includes Update (opens the latest release) and Uninstall (removes startup registration and stops the portable widget while preserving files). Full installation-file removal and automatic updating are not yet implemented.

## v0.1.2

Automatically shrink to the largest available taskbar slot when the preferred size cannot fit. Windows DPI and taskbar height are respected, the preferred size stays saved, and Settings reports the fitted size. The minimum is 70%; tray fallback remains when space is insufficient.

## v0.1.3

Displays the installed version in the window title and both panel tabs. Includes larger, smoother countdown/reset text, enough space for 100% and stale markers, usage colors changing to yellow at 60% and red at 80%, unified Usage/Settings tabs, and taskbar visibility/provider logo controls.

## v0.1.4

Percentage text is white below 60%, orange from 60%, and red from 80%. Percentage labels fit their content (including 100% and stale markers), with a small gap beside the progress bar. The bar and time row resize together.

## v0.1.7

Settings now has Check for updates. It downloads the latest published ZIP and SHA-256 checksum, verifies the archive and application version before stopping the widget, backs up app files, replaces only application components in the same folder, and restarts. Saved settings, usage cache and startup registration are preserved. It attempts rollback on replacement failure. Download or validation failures leave the running app untouched.

## v0.1.8

Fixes Start with Windows silently failing after a manual update into a new folder (for example, extracting v0.1.7 after deleting the v0.1.3 folder). On launch, an existing Startup shortcut that points to another location is repointed at the running TokenTaskbar.exe. Run v0.1.8 once from its folder and a broken shortcut repairs itself.
