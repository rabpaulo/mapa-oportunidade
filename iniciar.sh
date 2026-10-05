#!/usr/bin/env bash
set -euo pipefail
TASK_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$TASK_ROOT"
if [ ! -x .venv/bin/python ]; then
  python3 -m venv .venv
fi
if ! .venv/bin/python -c 'import fastapi, uvicorn, httpx, openpyxl, requests, dotenv' 2>/dev/null; then
  .venv/bin/python -m pip install -r requirements.lock
fi
if [ ! -d app/node_modules/next ]; then
  (cd app && npm ci)
fi
if [ ! -f data/uf/CE/contatos.db ] && [ -f "$HOME/garimpo/data/uf/CE/contatos.db" ] && [ -z "${CEARA_DATA_DIR:-}" ]; then
  .venv/bin/python scripts/importar_base.py
fi
exec .venv/bin/python scripts/dev.py "$@"
