#!/usr/bin/env bash
set -euo pipefail

endpoint="${GABS_HTTP_ENDPOINT:-http://localhost:8097/mcp}"
game_id="${GABS_GAME_ID:-rimworld}"

if ! command -v jq >/dev/null 2>&1; then
	printf 'jq is required.\n' >&2
	exit 2
fi

rpc_id=1

call_gabs() {
	local name="$1"
	local args_json
	if [[ $# -ge 2 ]]; then
		args_json="$2"
	else
		args_json="{}"
	fi
	local payload
	payload="$(jq -cn \
		--argjson id "$rpc_id" \
		--arg name "$name" \
		--argjson args "$args_json" \
		'{jsonrpc:"2.0", id:$id, method:"tools/call", params:{name:$name, arguments:$args}}')"
	rpc_id=$((rpc_id + 1))
	curl -sS \
		-H 'Content-Type: application/json' \
		-H 'Accept: application/json, text/event-stream' \
		-d "$payload" \
		"$endpoint"
}

fail_json() {
	local code="$1"
	local message="$2"
	local json="${3:-}"
	printf 'FAIL[%s]: %s\n' "$code" "$message" >&2
	if [[ -n "$json" ]]; then
		printf '%s\n' "$json" | jq '.' >&2 || printf '%s\n' "$json" >&2
	fi
	exit 1
}

tool_is_error() {
	local response="$1"
	printf '%s\n' "$response" | jq -e '(.error != null) or (.result.isError // false)' >/dev/null
}

fail_if_tool_error() {
	local code="$1"
	local message="$2"
	local response="$3"
	if tool_is_error "$response"; then
		fail_json "$code" "$message" "$response"
	fi
}

call_game_tool() {
	local tool="$1"
	local args_json
	if [[ $# -ge 2 ]]; then
		args_json="$2"
	else
		args_json="{}"
	fi
	local seconds="${3:-15}"
	call_gabs games_call_tool "$(jq -cn \
		--arg gameId "$game_id" \
		--arg tool "$tool" \
		--argjson args "$args_json" \
		--argjson timeout "$seconds" \
		'{gameId:$gameId, tool:$tool, arguments:$args, timeout:$timeout}')"
}

status="$(call_gabs games_status "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
fail_if_tool_error "gabs_status_failed" "GABS could not read status for '$game_id'." "$status"

state="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.status // "unknown"')"
tool_count="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.toolCount // 0')"
if [[ "$state" != "running" || "$tool_count" == "0" ]]; then
	fail_json "gabs_not_connected" "GABS is not connected to mirrored RimWorld tools." "$status"
fi

attention="$(call_gabs games_get_attention "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId, timeout:10}')")"
fail_if_tool_error "attention_check_failed" "Could not inspect GABS attention." "$attention"
blocking="$(printf '%s\n' "$attention" | jq -r '.result.structuredContent.blocking // false')"
if [[ "$blocking" == "true" ]]; then
	fail_json "attention_blocked" "GABS has blocking attention open." "$attention"
fi

bridge="$(call_game_tool 'rimbridge/get_bridge_status' '{}' 15)"
fail_if_tool_error "bridge_status_failed" "RimBridge status tool failed." "$bridge"

zl_status="$(call_game_tool 'zombieland/get_status' '{"_rimBridgeTimeoutMs":10000}' 15)"
fail_if_tool_error "zombieland_status_failed" "Zombieland status tool failed." "$zl_status"
zl_success="$(printf '%s\n' "$zl_status" | jq -r '.result.structuredContent.success // false')"
if [[ "$zl_success" != "true" ]]; then
	fail_json "zombieland_status_failed" "Zombieland status returned success=false." "$zl_status"
fi

jq -n \
	--arg gameId "$game_id" \
	--argjson gabs "$(printf '%s\n' "$status" | jq '.result.structuredContent')" \
	--argjson attention "$(printf '%s\n' "$attention" | jq '.result.structuredContent')" \
	--argjson bridgeState "$(printf '%s\n' "$bridge" | jq '.result.structuredContent.state')" \
	--argjson zombieland "$(printf '%s\n' "$zl_status" | jq '.result.structuredContent | {success, hasCurrentMap, spawnedZombieCount, ordinaryZombies, blobs, spitters, timeSpeed}')" \
	'{success:true, gameId:$gameId, gabs:$gabs, attention:$attention, bridgeState:$bridgeState, zombieland:$zombieland}'
