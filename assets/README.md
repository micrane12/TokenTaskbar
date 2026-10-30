# Provider icons

`TokenTaskbar.ico` and `TokenTaskbar.png` are the app's own orange/blue usage-bar icon, matching the original tray design. The ICO contains 16, 20, 24, 32, 48, 64, 128 and 256-pixel sizes. Regenerate both on Windows with `source/build-icon.ps1`. The ICO is embedded as the executable's Windows shell icon and as the panel/tray icon, and is referenced by the Start menu shortcut.

These PNGs are the unmodified app icons from the installed official Windows app packages:

- `claude.png`: Claude 2.19675.1.0, Square44x44Logo.targetsize-256_altform-unplated.png.
- `codex.png`: OpenAI.Codex 26.930.7945.0, Square44x44Logo.targetsize-256_altform-unplated.png.

They identify the corresponding usage lanes. Both are embedded in the compiled widget, so another PC does not need those desktop apps installed to display the icons. Brand ownership remains with the respective providers.
