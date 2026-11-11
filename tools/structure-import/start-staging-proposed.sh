#!/bin/bash
# Prepared only. Switching the shared staging binary still needs owner approval.
set -euo pipefail
cd /home/mc/.openclaw/workspace/saturn-lodestones/staging
export RUST_MIN_STACK=16777216
exec /home/mc/structure-prototype/bin/pumpkin-forceload-26.60
