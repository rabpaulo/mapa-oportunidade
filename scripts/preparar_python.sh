#!/usr/bin/env bash
set -euo pipefail
TASK_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$TASK_ROOT"
test -x .venv/bin/python || python3 -m venv .venv
exec .venv/bin/python -m pip install -r requirements.lock
