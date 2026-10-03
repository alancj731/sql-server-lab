#!/usr/bin/env bash
# Lints all Bicep templates and recompiles the per-lab template embedded in the worker.
#   ./infra/scripts/build-bicep.sh          # rebuild
#   ./infra/scripts/build-bicep.sh --check  # fail if the committed lab.json is stale (CI)
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
require az

for f in "$REPO_ROOT"/infra/bicep/platform/*.bicep "$REPO_ROOT"/infra/bicep/lab/main.bicep; do
  az bicep lint --file "$f" --diagnostics-format sarif >/dev/null
  az bicep build --file "$f" --stdout >/dev/null
  ok "${f#"$REPO_ROOT"/}"
done

target="$REPO_ROOT/backend/src/SqlServerLab.Infrastructure/Azure/lab.json"
fresh="$(mktemp)"
az bicep build --file "$REPO_ROOT/infra/bicep/lab/main.bicep" --outfile "$fresh"
# The generator metadata (Bicep version/hash) differs between machines; compare everything else.
strip() { jq 'del(.metadata._generator)' "$1"; }
if [[ "${1:-}" == "--check" ]]; then
  diff <(strip "$fresh") <(strip "$target") >/dev/null || die "lab.json is stale. Run infra/scripts/build-bicep.sh and commit it."
  ok "lab.json is current"
else
  cp "$fresh" "$target"
  ok "Updated ${target#"$REPO_ROOT"/}"
fi
rm -f "$fresh"
