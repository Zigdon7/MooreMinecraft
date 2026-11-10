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
import pathlib,xml.etree.ElementTree as E
files=list(pathlib.Path('client/build/test-results/test').glob('TEST-*.xml'))
assert len(files)==5, 'Missing test reports'
rows=[E.parse(p).getroot().attrib for p in files]
assert sum(int(r['tests']) for r in rows)>=26
assert all(int(r['failures'])==0 and int(r['errors'])==0 for r in rows)
print('CLIENT SUITE:',sum(int(r['tests']) for r in rows),'passed; 0 failed')
PY
