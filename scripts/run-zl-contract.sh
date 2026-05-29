#!/usr/bin/env bash
set -euo pipefail

endpoint="${GABS_HTTP_ENDPOINT:-http://localhost:8097/mcp}"
game_id="${GABS_GAME_ID:-rimworld}"
save_name="ZombieTest"
timeout_seconds=90
load_save=1
tool_name=""

usage() {
	printf 'Usage: %s [--endpoint URL] [--game ID] [--save NAME|--no-load] [--timeout SECONDS] TOOL\n' "$0" >&2
}

while [[ $# -gt 0 ]]; do
	case "$1" in
		--endpoint)
			endpoint="${2:?missing endpoint}"
			shift 2
			;;
		--game)
			game_id="${2:?missing game id}"
			shift 2
			;;
		--save)
			save_name="${2:?missing save name}"
			load_save=1
			shift 2
			;;
		--no-load)
			load_save=0
			shift
			;;
		--timeout)
			timeout_seconds="${2:?missing timeout}"
			shift 2
			;;
		--help|-h)
			usage
			exit 0
			;;
		-*)
			usage
			exit 2
			;;
		*)
			tool_name="$1"
			shift
			;;
	esac
done

if [[ -z "$tool_name" ]]; then
	usage
	exit 2
fi

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
	local message="$1"
	local json="${2:-}"
	printf 'FAIL: %s\n' "$message" >&2
	if [[ -n "$json" ]]; then
		printf '%s\n' "$json" | jq '.' >&2 || printf '%s\n' "$json" >&2
	fi
	exit 1
}

resolve_tool() {
	local requested="$1"
	local response resolved
	response="$(call_gabs games_tool_names "$(jq -cn --arg gameId "$game_id" --arg query "$requested" '{gameId:$gameId, query:$query, brief:true, limit:100}')")"
	resolved="$(printf '%s\n' "$response" | jq -r --arg requested "$requested" '
		.result.structuredContent.tools // []
		| map(select(
			.name == $requested
			or .gabpName == $requested
			or .localName == $requested
			or .originalName == $requested
		))
		| if length == 1 then .[0].name else empty end
	')"
	if [[ -n "$resolved" ]]; then
		printf '%s\n' "$resolved"
		return 0
	fi

	resolved="$(printf '%s\n' "$response" | jq -r '
		.result.structuredContent.tools // []
		| if length == 1 then .[0].name else empty end
	')"
	if [[ -n "$resolved" ]]; then
		printf '%s\n' "$resolved"
		return 0
	fi

	fail_json "Could not resolve tool '$requested' uniquely." "$response"
}

call_game_tool() {
	local tool="$1"
	local args_json
	if [[ $# -ge 2 ]]; then
		args_json="$2"
	else
		args_json="{}"
	fi
	local seconds="${3:-$timeout_seconds}"
	call_gabs games_call_tool "$(jq -cn \
		--arg gameId "$game_id" \
		--arg tool "$tool" \
		--argjson args "$args_json" \
		--argjson timeout "$seconds" \
		'{gameId:$gameId, tool:$tool, arguments:$args, timeout:$timeout}')"
}

ack_attention_if_present() {
	local response="$1"
	local attention_id
	attention_id="$(printf '%s\n' "$response" | jq -r '.result.structuredContent.attention.attentionId // empty')"
	if [[ -z "$attention_id" ]]; then
		return 1
	fi

	call_gabs games_ack_attention "$(jq -cn --arg gameId "$game_id" --arg attentionId "$attention_id" '{gameId:$gameId, attentionId:$attentionId}')" >/dev/null
	return 0
}

load_game_ready() {
	call_game_tool "$load_ready_tool" "$(jq -cn --arg saveName "$save_name" '{saveName:$saveName, readiness:"visual", timeoutMs:120000, pauseIfNeeded:true}')" 130
}

rimworld_processes="$(ps ax -o pid=,command= | grep "[R]imWorld by Ludeon Studios" || true)"
rimworld_count="$(printf '%s\n' "$rimworld_processes" | sed '/^[[:space:]]*$/d' | wc -l | tr -d ' ')"
if [[ "$rimworld_count" -gt 1 ]]; then
	printf 'FAIL: multiple RimWorld processes are running; refusing ambiguous bridge run.\n' >&2
	printf '%s\n' "$rimworld_processes" >&2
	exit 1
fi

status="$(call_gabs games_status "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
status_name="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.status // "unknown"')"
if [[ "$status_name" == "stopped" ]]; then
	start="$(call_gabs games_start "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
	if [[ "$(printf '%s\n' "$start" | jq -r '.result.isError // false')" == "true" ]]; then
		fail_json "GABS failed to start '$game_id'." "$start"
	fi
fi

status="$(call_gabs games_status "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
tool_count="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.toolCount // 0')"
if [[ "$tool_count" == "0" ]]; then
	connect="$(call_gabs games_connect "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
	if [[ "$(printf '%s\n' "$connect" | jq -r '.result.isError // false')" == "true" ]]; then
		fail_json "GABS could not connect to '$game_id' GABP tools." "$connect"
	fi
fi

bridge_status_tool="$(resolve_tool 'rimbridge/get_bridge_status')"
list_logs_tool="$(resolve_tool 'rimbridge/list_logs')"
list_events_tool="$(resolve_tool 'rimbridge/list_operation_events')"
load_ready_tool="$(resolve_tool 'rimworld/load_game_ready')"
contract_tool="$(resolve_tool "$tool_name")"

if [[ "$load_save" -eq 1 ]]; then
	load_response=""
	for attempt in 1 2 3 4; do
		load_response="$(load_game_ready)"
		load_success="$(printf '%s\n' "$load_response" | jq -r '.result.structuredContent.success // false')"
		if [[ "$load_success" == "true" ]]; then
			break
		fi
		ack_attention_if_present "$load_response" || true
	done
	load_success="$(printf '%s\n' "$load_response" | jq -r '.result.structuredContent.success // false')"
	if [[ "$load_success" != "true" ]]; then
		fail_json "Failed to load save '$save_name' to visual readiness." "$load_response"
	fi
fi

before_status="$(call_game_tool "$bridge_status_tool" '{}' 15)"
ready="$(printf '%s\n' "$before_status" | jq -r '.result.structuredContent.state.automationReady // false')"
if [[ "$ready" != "true" && "$load_save" -eq 1 ]]; then
	load_response="$(load_game_ready)"
	ack_attention_if_present "$load_response" || true
	before_status="$(call_game_tool "$bridge_status_tool" '{}' 15)"
	ready="$(printf '%s\n' "$before_status" | jq -r '.result.structuredContent.state.automationReady // false')"
fi
if [[ "$ready" != "true" ]]; then
	fail_json "RimBridge is connected but the game is not automation-ready." "$before_status"
fi

event_cursor="$(printf '%s\n' "$before_status" | jq -r '.result.structuredContent.latestOperationEventSequence // 0')"
log_cursor="$(printf '%s\n' "$before_status" | jq -r '.result.structuredContent.latestLogSequence // 0')"

printf 'RUN %s as %s on %s/%s\n' "$tool_name" "$contract_tool" "$game_id" "$save_name"
contract_response="$(call_game_tool "$contract_tool" '{}' "$timeout_seconds")"
printf '%s\n' "$contract_response" | jq '.result.structuredContent // .result.content // .error'

success="$(printf '%s\n' "$contract_response" | jq -r '.result.structuredContent.success // (if (.result.isError // false) then false else empty end)')"

after_status="$(call_game_tool "$bridge_status_tool" '{}' 15)"
events_response="$(call_game_tool "$list_events_tool" "$(jq -cn --argjson after "$event_cursor" '{afterSequence:$after, limit:20, includeDiagnostics:true}')" 15)"
logs_response="$(call_game_tool "$list_logs_tool" "$(jq -cn --argjson after "$log_cursor" '{afterSequence:$after, minimumLevel:"Warning", limit:20}')" 15)"

printf '\nBRIDGE STATE\n'
printf '%s\n' "$after_status" | jq '.result.structuredContent.state'
printf '\nOPERATION EVENTS\n'
printf '%s\n' "$events_response" | jq '.result.structuredContent.events // []'
printf '\nWARNINGS AND ERRORS\n'
printf '%s\n' "$logs_response" | jq '.result.structuredContent.logs // []'

if [[ "$success" != "true" ]]; then
	exit 1
fi
