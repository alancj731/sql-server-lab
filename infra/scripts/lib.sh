#!/usr/bin/env bash
# Shared helpers for deploy.sh, destroy.sh, and smoke-lab.sh. Sourced, not executed.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATE_DIR="$REPO_ROOT/.deploy"

if [[ -t 1 ]]; then
  C_BOLD=$'\e[1m'; C_GREEN=$'\e[32m'; C_YELLOW=$'\e[33m'; C_RED=$'\e[31m'; C_RESET=$'\e[0m'
else
  C_BOLD=''; C_GREEN=''; C_YELLOW=''; C_RED=''; C_RESET=''
fi

phase() { printf '\n%s==> %s%s\n' "$C_BOLD" "$*" "$C_RESET"; }
info() { printf '    %s\n' "$*"; }
ok() { printf '    %s✔%s %s\n' "$C_GREEN" "$C_RESET" "$*"; }
warn() { printf '    %s!%s %s\n' "$C_YELLOW" "$C_RESET" "$*" >&2; }
die() { printf '%s✘ %s%s\n' "$C_RED" "$*" "$C_RESET" >&2; exit 1; }

require() {
  for cmd in "$@"; do
    command -v "$cmd" >/dev/null 2>&1 || die "Required command '$cmd' not found."
  done
}

confirm() {
  local prompt="$1"
  [[ "${ASSUME_YES:-false}" == "true" ]] && return 0
  read -r -p "    $prompt [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]]
}

# Poll a command until it prints the expected value. Usage: wait_for <timeout-seconds> <expected> <command...>
wait_for() {
  local timeout="$1" expected="$2"
  shift 2
  local deadline=$((SECONDS + timeout)) value=''
  while ((SECONDS < deadline)); do
    value="$("$@" 2>/dev/null || true)"
    [[ "$value" == "$expected" ]] && return 0
    sleep 10
  done
  return 1
}

state_file() { printf '%s/%s.json' "$STATE_DIR" "$1"; }

# Read a key from the saved deployment state for an environment.
state_get() {
  local file
  file="$(state_file "$1")"
  [[ -f "$file" ]] || die "No deployment state at $file. Run deploy.sh first."
  jq -er --arg k "$2" '.[$k] // empty' "$file"
}

state_merge() {
  local file
  file="$(state_file "$1")"
  mkdir -p "$STATE_DIR"
  [[ -f "$file" ]] || echo '{}' >"$file"
  local tmp
  tmp="$(mktemp)"
  jq -s '.[0] * .[1]' "$file" - >"$tmp" && mv "$tmp" "$file"
}
