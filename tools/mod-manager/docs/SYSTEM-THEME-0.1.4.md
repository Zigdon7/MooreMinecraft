# Saturn Mod Manager 0.1.4 — Windows system theme preview

Download **Saturn-Mod-Manager.exe**, close your previous manager, then run this copy.
Windows x64, self-contained, unsigned. This replaces only the manager application;
pack manifests still install JARs only. The mod channel and Minecraft servers are unchanged.

- Follows the Windows light/dark app preference via .NET 10 SystemColorMode.System.
- Native themed buttons replace the fixed blue Install fill.
- System card, input, text and underlined link colors; refresh on system color changes.
- High-contrast handling is delegated to WinForms/Windows, but interactive high-contrast
  and live-theme-switch acceptance remain unverified.

Source: d92ccfe8cfc554dcca2226598c3c369ffd746cb6
CI: https://github.com/Zigdon7/MooreMinecraft/actions/runs/42971643122

Native Windows evidence covers 14 UI/keyboard/automation cases under each of the
runner default and dark app preferences (28 cases), plus 13 updater transaction tests.
Offline UI fixtures are not real installation or Narrator evidence. The existing
Atlas regression job is unchanged. SHA256SUMS.txt pins the exact published EXE.
