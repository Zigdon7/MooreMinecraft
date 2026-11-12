# Main/staging check — 2026-11-12
Pulled main 0dc93be. Only Mod Manager source changed since staging deployment 34320b0.
Atlas WASM on disk matches the built artifact; no server/plugin/client-JAR update or restart required.
Staging TCP 25566 reachable; production untouched.

Published manager-preview-v0.1.6 from exact Windows CI artifact in run 43223337606.
14 updater tests passed locally and in CI; native default/dark suites passed, STA assertion included.
Inspected native dark checked-state screenshot. Actual folder picker/Narrator unverified.
Release includes checksums, native UI evidence and EXE. Updated staging components manager pointer.
Pack manifest and Atlas component pointers remain unchanged.
