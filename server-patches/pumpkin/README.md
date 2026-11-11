# Pumpkin force-load ticket backport

- Base tag: `0.2.2+26.3-26.60`
- Base commit: `eda69440e6369c5f8a05c3f69fc809c40dab93a6`
- Source: [Pumpkin-MC/Pumpkin PR4373](https://github.com/Pumpkin-MC/Pumpkin/pull/4373)
- Upstream patch commit: `f1f0efe5285c2fa7fdffbf813df71ca673213062`
- Original author: wsuites. Original patch metadata and co-author attribution retained.
- Local tested backport commit: `95acaca` on `saturn-forceload-testing` in the
  operator's separate Pumpkin checkout (not a commit in this monorepo).

`4373-forceload-tickets.patch` fixes missing loading tickets in `/forceload add`,
`remove` and `remove all`. It does **not** implement registration persistence across
restarts. See [runtime QA](../../tools/structure-import/QA.md) for results and gaps.

This is an attributed upstream patch, not a new independent implementation or a
claim of upstream acceptance. It is GPL-3.0 code; [LICENSE](LICENSE) applies here,
not the monorepo's default MIT license. No Pumpkin binary is redistributed.

## Reproduce the tested build

In a separate, clean checkout pinned to the base commit, first run `git apply
--check` against this patch, then `git apply`. Do not apply it twice. A machine
with a normal C compiler/archiver can use those instead of the operator's Zig
wrappers. For the current container, from the monorepo root:

```bash
repo="$PWD"
export CC="$repo/tools/structure-import/cc-zig"
export AR="$repo/tools/structure-import/ar-zig"
export PATH="/home/mc/.local/share/saturn-tools/bin:/home/mc/.cargo/bin:$PATH"
export CARGO_INCREMENTAL=0
cargo build --locked --manifest-path /home/mc/pumpkin-chunkloader/Cargo.toml \
  -p pumpkin -j 1 --config 'profile.dev.package.pumpkin-data.codegen-units=16'
```

The wrappers reference the already-installed user-owned Zig toolchain; no system
packages were installed. For this unoptimized debug build, launch only an isolated
test config with `RUST_MIN_STACK=16777216`. Never use this build for performance
claims or replace a live binary without the operator's required approval/backup.

Tested binary SHA256:
`a606011e3e4318f405684c50710c0a1ecb9eb7776b01345dbff9aa752e981947`.
