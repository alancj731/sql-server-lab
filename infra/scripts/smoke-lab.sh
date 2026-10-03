#!/usr/bin/env bash
# Real-Azure smoke test: creates one lab through the deployed API, waits for Ready, deallocates, starts, deletes,
# and verifies nothing is left behind. Takes ~20-30 minutes and costs a few cents.
#
#   ./infra/scripts/smoke-lab.sh --env dev
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ENV_NAME="dev"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) ENV_NAME="$2"; shift 2 ;;
    *) die "Unknown option: $1" ;;
  esac
done
require az jq curl

BASE="https://$(state_get "$ENV_NAME" apiFqdn)/api/v1"
API_APP_ID="$(state_get "$ENV_NAME" apiAppId)"
LOCATION="$(state_get "$ENV_NAME" location)"
NAME="smoke-$(date -u +%m%d%H%M)"

token() { az account get-access-token --resource "api://$API_APP_ID" --query accessToken -o tsv; }
call() {
  local method="$1" path="$2" body="${3:-}"
  local args=(-sS -X "$method" -H "Authorization: Bearer $(token)" -H "Content-Type: application/json" -w '\n%{http_code}')
  [[ -n "$body" ]] && args+=(-d "$body")
  local response code
  response="$(curl "${args[@]}" "$BASE$path")"
  code="${response##*$'\n'}"
  RESPONSE="${response%$'\n'*}"
  [[ "$code" =~ ^2 ]] || die "$method $path → HTTP $code: $RESPONSE"
}

wait_state() {
  local want="$1" timeout="$2"
  local deadline=$((SECONDS + timeout)) state='' last=''
  while ((SECONDS < deadline)); do
    call GET "/labs/$LAB_ID"
    state="$(jq -r .state <<<"$RESPONSE")"
    local progress
    progress="$(jq -r 'if .currentJob then "\(.currentJob.type) \(.currentJob.progress)% \(.currentJob.currentStep // "")" else "" end' <<<"$RESPONSE")"
    [[ "$state $progress" != "$last" ]] && info "$(date +%H:%M:%S) $state $progress" && last="$state $progress"
    [[ "$state" == "$want" ]] && return 0
    [[ "$state" == "Failed" ]] && die "Lab failed: $(jq -r .stateReason <<<"$RESPONSE")"
    sleep 15
  done
  die "Timed out waiting for $want (last state $state)."
}

phase "Create lab $NAME in $LOCATION"
call POST /labs "$(jq -n --arg n "$NAME" --arg r "$LOCATION" '{name:$n, region:$r, ttlHours:2}')"
LAB_ID="$(jq -r .lab.id <<<"$RESPONSE")"
RG="$(jq -r .lab.resourceGroupName <<<"$RESPONSE")"
ok "Lab $LAB_ID ($RG)"
wait_state Ready 2400
ok "Ready — SQL Server answered the health check"

[[ "$(az network public-ip list -g "$RG" --query 'length(@)' -o tsv)" == "0" ]] || die "A public IP exists in $RG"
ok "No public IP in the lab resource group"

phase "Deallocate"
call POST "/labs/$LAB_ID/deallocate"
wait_state Stopped 1200
ok "Stopped"

phase "Start"
call POST "/labs/$LAB_ID/start"
wait_state Ready 1800
ok "Ready again"

phase "Delete"
call DELETE "/labs/$LAB_ID" "$(jq -n --arg n "$NAME" '{confirmName:$n}')"
wait_state Deleted 1800
[[ "$(az group exists -n "$RG")" == "false" ]] || die "$RG still exists"
[[ "$(az resource list --tag "labId=$LAB_ID" --query 'length(@)' -o tsv)" == "0" ]] || die "Resources tagged labId=$LAB_ID remain"
ok "Resource group and all tagged resources removed"

phase "Smoke test passed"
