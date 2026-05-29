#!/usr/bin/env bash
set -euo pipefail

endpoint="${GABS_HTTP_ENDPOINT:-http://localhost:8097/mcp}"
game_id="${GABS_GAME_ID:-rimworld}"
save_name=""
load_save=0
readiness="visual"
ticks=1
timeout_seconds=20
load_timeout_seconds=120
poll_interval_ms=10
ack_attention=0

usage() {
	printf 'Usage: %s [--endpoint URL] [--game ID] [--save NAME|--no-load] [--ticks N] [--timeout SECONDS] [--load-timeout SECONDS] [--poll-ms MS] [--readiness TARGET] [--ack-attention]\n' "$0" >&2
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
			save_name=""
			shift
			;;
		--ticks)
			ticks="${2:?missing tick count}"
			shift 2
			;;
		--timeout)
			timeout_seconds="${2:?missing timeout seconds}"
			shift 2
			;;
		--load-timeout)
			load_timeout_seconds="${2:?missing load timeout seconds}"
			shift 2
			;;
		--poll-ms)
			poll_interval_ms="${2:?missing poll interval ms}"
			shift 2
			;;
		--readiness)
			readiness="${2:?missing readiness target}"
			shift 2
			;;
		--ack-attention)
			ack_attention=1
			shift
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
			usage
			exit 2
			;;
	esac
done

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

structured() {
	jq '.result.structuredContent // .result.content // .error'
}

call_game_tool() {
	local tool="$1"
	local args_json
	if [[ $# -ge 2 ]]; then
		args_json="$2"
	else
		args_json="{}"
	fi
	local seconds="${3:-20}"
	call_gabs games_call_tool "$(jq -cn \
		--arg gameId "$game_id" \
		--arg tool "$tool" \
		--argjson args "$args_json" \
		--argjson timeout "$seconds" \
		'{gameId:$gameId, tool:$tool, arguments:$args, timeout:$timeout}')"
}

resolve_tool() {
	local requested="$1"
	local fallback="$2"
	local response resolved detail
	response="$(call_gabs games_tool_names "$(jq -cn --arg gameId "$game_id" --arg query "$requested" '{gameId:$gameId, query:$query, brief:true, limit:100}')")"
	fail_if_tool_error "tool_discovery_failed" "GABS failed while discovering '$requested'." "$response"
	resolved="$(printf '%s\n' "$response" | jq -r --arg requested "$requested" '
		.result.structuredContent.tools // []
		| map(select(.name == $requested or .gabpName == $requested or .localName == $requested or .originalName == $requested))
		| if length == 1 then .[0].name else empty end
	')"
	if [[ -n "$resolved" ]]; then
		printf '%s\n' "$resolved"
		return 0
	fi

	detail="$(call_gabs games_tool_detail "$(jq -cn --arg gameId "$game_id" --arg tool "$fallback" '{gameId:$gameId, tool:$tool}')")"
	fail_if_tool_error "tool_discovery_failed" "Could not inspect fallback tool '$fallback' for '$requested'." "$detail"
	printf '%s\n' "$detail" | jq -r '.result.structuredContent.name // empty'
}

attention_response() {
	call_gabs games_get_attention "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId, timeout:10}')"
}

handle_attention() {
	local phase="$1"
	local response blocking attention_id ack
	response="$(attention_response)"
	fail_if_tool_error "attention_check_failed" "Could not inspect attention after $phase." "$response"
	blocking="$(printf '%s\n' "$response" | jq -r '.result.structuredContent.blocking // false')"
	attention_id="$(printf '%s\n' "$response" | jq -r '.result.structuredContent.attention.attentionId // empty')"
	if [[ "$blocking" != "true" && -z "$attention_id" ]]; then
		return 0
	fi
	if [[ "$ack_attention" -ne 1 || -z "$attention_id" ]]; then
		fail_json "attention_blocked" "GABS has open attention after $phase. Re-run with --ack-attention only for known fixture-level noise." "$response"
	fi
	ack="$(call_gabs games_ack_attention "$(jq -cn --arg gameId "$game_id" --arg attentionId "$attention_id" '{gameId:$gameId, attentionId:$attentionId, timeout:10}')")"
	fail_if_tool_error "attention_ack_failed" "Could not acknowledge attention '$attention_id'." "$ack"
}

status="$(call_gabs games_status "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
fail_if_tool_error "gabs_status_failed" "GABS could not read status for '$game_id'." "$status"
state="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.status // "unknown"')"
tool_count="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.toolCount // 0')"
if [[ "$state" == "running-disconnected" || ( "$state" == "running" && "$tool_count" == "0" ) ]]; then
	connect_response="$(call_gabs games_connect "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId, timeout:30, forceTakeover:true}')")"
	fail_if_tool_error "gabs_connect_failed" "GABS could not reconnect to '$game_id'." "$connect_response"
	status="$(call_gabs games_status "$(jq -cn --arg gameId "$game_id" '{gameId:$gameId}')")"
	fail_if_tool_error "gabs_status_failed" "GABS could not read status for '$game_id' after reconnect." "$status"
	state="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.status // "unknown"')"
	tool_count="$(printf '%s\n' "$status" | jq -r '.result.structuredContent.toolCount // 0')"
fi
if [[ "$state" != "running" || "$tool_count" == "0" ]]; then
	fail_json "gabs_not_connected" "GABS is not connected to mirrored RimWorld tools." "$status"
fi

step_tool="$(resolve_tool 'rimworld/step_game_ticks' 'rimworld_rimworld_step_game_ticks')"

if [[ "$load_save" -eq 1 ]]; then
	load_tool="$(resolve_tool 'rimworld/load_game_ready' 'rimworld_rimworld_load_game_ready')"
	load_response="$(call_game_tool "$load_tool" "$(jq -cn \
		--arg saveName "$save_name" \
		--arg readiness "$readiness" \
		--argjson timeoutMs "$((load_timeout_seconds * 1000))" \
		'{saveName:$saveName, timeoutMs:$timeoutMs, pollIntervalMs:50, readiness:$readiness, pauseIfNeeded:true}')" "$load_timeout_seconds")"
	fail_if_tool_error "load_failed" "Could not load save '$save_name'." "$load_response"
	handle_attention "loading '$save_name'"
else
	handle_attention "pre-step"
fi

step_response="$(call_game_tool "$step_tool" "$(jq -cn \
	--argjson ticks "$ticks" \
	--argjson timeoutMs "$((timeout_seconds * 1000))" \
	--argjson pollIntervalMs "$poll_interval_ms" \
	'{ticks:$ticks, timeoutMs:$timeoutMs, pollIntervalMs:$pollIntervalMs, pauseFirst:true, playSound:false}')" "$timeout_seconds")"
fail_if_tool_error "step_failed" "The real game tick step failed." "$step_response"
step_success="$(printf '%s\n' "$step_response" | jq -r '.result.structuredContent.success // false')"
if [[ "$step_success" != "true" ]]; then
	fail_json "step_failed" "The real game tick step returned success=false." "$step_response"
fi
handle_attention "tick stepping"

printf '%s\n' "$step_response" | structured | jq '{
	success,
	status,
	message,
	requestedTicks,
	completedTicks,
	advancedTicks,
	advancedFrames,
	startTicksGame,
	endTicksGame,
	paused: .state.paused,
	timeSpeed: .state.timeSpeed,
	operationStatus: .operation.Status,
	operationSuccess: .operation.Success
}'
