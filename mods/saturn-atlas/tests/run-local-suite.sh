#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -d /home/mc/.local/share/saturn-tools ]; then
 export PATH=/home/mc/.local/share/saturn-tools/bin:/home/mc/.cargo/bin:$PATH
 export JAVA_HOME=/home/mc/.local/share/saturn-tools/jdk25
fi
cargo test --manifest-path core/Cargo.toml
cargo run --manifest-path core/Cargo.toml --example protocol_fixture -- tests/fixtures
client/gradlew -p client test build --no-daemon
cargo build --release --manifest-path server/Cargo.toml --target wasm32-wasip2 --target-dir target
python3 - <<'PY'
import pathlib,re,xml.etree.ElementTree as E
files=list(pathlib.Path('client/build/test-results/test').glob('TEST-*.xml'))
assert len(files)==5, 'Missing test reports'
rows=[E.parse(p).getroot().attrib for p in files]
assert sum(int(r['tests']) for r in rows)>=26
assert all(int(r['failures'])==0 and int(r['errors'])==0 for r in rows)
print('CLIENT SUITE:',sum(int(r['tests']) for r in rows),'passed; 0 failed')
wasm=pathlib.Path('target/wasm32-wasip2/release/saturn_atlas.wasm').read_bytes()
assert wasm[:8]==bytes.fromhex('0061736d0d000100'), 'Expected a component, not a core module'
versions=set(re.findall(rb'pumpkin:plugin/[a-z0-9-]+@([0-9.]+)',wasm))
assert versions=={b'0.1.0'}, f'Unexpected Pumpkin ABI markers: {versions}'
assert b'load-chunk' in wasm, 'Preload guest artifact missing load-chunk import marker'
print('COMPONENT ABI markers: 0.1.0 with load-chunk; runtime linking still required')

PY
