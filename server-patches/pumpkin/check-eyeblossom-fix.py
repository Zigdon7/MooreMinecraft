#!/usr/bin/env python3
"""Fail closed if a Pumpkin update drops the reviewed Saturn eyeblossom mitigation.

Source preflight only: this does NOT prove a binary was built from these sources.
An upstream replacement fix requires review, runtime evidence and updating this pin.
"""
import hashlib
from pathlib import Path
import sys

REVIEWED_SHA256 = "2dc0f6d34b6360e3d65944dfe85dac791cd2f92cbe139554b6675a11971732e2"
RELATIVE_SOURCE = "crates/pumpkin-core/src/block/blocks/plant/eyeblossom.rs"

if len(sys.argv) != 2:
    sys.exit("Usage: check-eyeblossom-fix.py /path/to/Pumpkin-source")
source = Path(sys.argv[1]) / RELATIVE_SOURCE
if not source.is_file():
    sys.exit("BLOCKED: eyeblossom source missing/moved; review the candidate before update.")
actual = hashlib.sha256(source.read_bytes()).hexdigest()
if actual != REVIEWED_SHA256:
    sys.exit("BLOCKED: candidate does not match reviewed eyeblossom mitigation. "
             "Apply eyeblossom-disconnect.patch, or review/test an upstream replacement "
             "and update the source pin. Do not install a stock binary over this fix.")
print("PASS: reviewed eyeblossom mitigation present in source. "
      "Still verify build provenance, artifact hash, and in-game behavior before promotion.")
