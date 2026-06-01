#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BASELINE="a0de6c309ab789a488a6bc106d281b5d0ca79020"
DETAILS=0
PATCH_GROUPS=0
DYNAMIC_PATCHES=0
STATIC_SUMMARY=0
BRIDGE_TOOLS=0
BRIDGE_SUMMARY=0
DEPENDENCY_GATES=0
SOURCE_PATHS=0
ROW_STATE_SUMMARY=0

while [[ $# -gt 0 ]]; do
	case "$1" in
		--details|-d)
			DETAILS=1
			;;
		--patch-groups)
			PATCH_GROUPS=1
			;;
		--dynamic-patches)
			DYNAMIC_PATCHES=1
			;;
		--static-summary)
			STATIC_SUMMARY=1
			;;
		--bridge-tools)
			BRIDGE_TOOLS=1
			;;
		--bridge-summary)
			BRIDGE_SUMMARY=1
			;;
		--dependency-gates)
			DEPENDENCY_GATES=1
			;;
		--source-paths)
			SOURCE_PATHS=1
			;;
		--row-state-summary)
			ROW_STATE_SUMMARY=1
			;;
		--help|-h)
			printf 'usage: %s [--details] [--patch-groups] [--dynamic-patches] [--static-summary] [--bridge-tools] [--bridge-summary] [--dependency-gates] [--source-paths] [--row-state-summary] [baseline-commit]\n' "$0"
			exit 0
			;;
		*)
			BASELINE="$1"
			;;
	esac
	shift
done

patch_groups() {
	find Source -type f -name '*.cs' -not -path '*/obj/*' -print0 \
		| sort -z \
		| xargs -0 awk '
			function trim(value) {
				gsub(/^[ \t]+|[ \t]+$/, "", value)
				return value
			}
			function uncomment(value, before, after) {
				while (value != "") {
					if (in_block_comment) {
						if (match(value, /\*\//)) {
							value = substr(value, RSTART + RLENGTH)
							in_block_comment = 0
							continue
						}
						return ""
					}
					if (match(value, /\/\*/)) {
						before = substr(value, 1, RSTART - 1)
						after = substr(value, RSTART + RLENGTH)
						value = before
						in_block_comment = 1
						if (after != "" && after ~ /\*\//) {
							sub(/^[^\*]*\*\//, "", after)
							in_block_comment = 0
							value = value after
							continue
						}
					}
					sub(/[ \t]*\/\/.*/, "", value)
					return value
				}
				return value
			}
			function flush() {
				if (class_name == "")
					return
				specific_target = current_attrs ~ /(nameof\(|MethodType|\"[A-Za-z0-9_]+\"|,[ \t]*nameof|,[ \t]*\"|new\[\])/
				targeting = dynamic ? "dynamic" : (specific_target ? "static" : "base")
				printf "%s:%d\t%s\t%s\t%s\n", file, class_line, class_name, targeting, current_attrs
				class_name = ""
				class_line = 0
				current_attrs = ""
				class_base_attrs = ""
				dynamic = 0
				active = 0
				brace_depth = 0
			}
			FNR == 1 {
				flush()
				attrs = ""
				file = FILENAME
				in_block_comment = 0
			}
			{
				source = uncomment($0)
			}
			source ~ /\[HarmonyPatch/ {
				if (active && brace_depth <= 0)
					flush()
				line = trim(source)
				attrs = attrs == "" ? line : attrs " | " line
			}
			attrs != "" && source ~ /^[ \t]*(public |internal |private |protected |static |sealed |abstract |partial |file )*class[ \t]+[A-Za-z0-9_]+/ {
				flush()
				class_line = FNR
				class_name = source
				sub(/^[ \t]*(public |internal |private |protected |static |sealed |abstract |partial |file )*class[ \t]+/, "", class_name)
				sub(/[ \t:{(<].*$/, "", class_name)
				current_attrs = attrs
				class_base_attrs = attrs
				attrs = ""
				active = 1
				brace_depth = 0
				next
			}
			attrs != "" && active && source ~ /^[ \t]*(public |internal |private |protected |static |virtual |override |sealed |async )*[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]*\(/ {
				method_name = source
				sub(/^[ \t]*(public |internal |private |protected |static |virtual |override |sealed |async )*[A-Za-z_][A-Za-z0-9_<>,\[\]\.?]*[ \t]+/, "", method_name)
				sub(/[ \t]*\(.*$/, "", method_name)
				method_attrs = class_base_attrs == "" ? attrs : class_base_attrs " | " attrs
				printf "%s:%d\t%s.%s\tstatic\t%s\n", file, FNR, class_name, method_name, method_attrs
				attrs = ""
			}
			active && source ~ /TargetMethods?\(/ {
				dynamic = 1
			}
			active {
				open_count = gsub(/{/, "{", source)
				close_count = gsub(/}/, "}", source)
				brace_depth += open_count - close_count
				if (brace_depth <= 0 && FNR > class_line) {
					flush()
					attrs = ""
				}
			}
			END {
				flush()
			}
		'
}

bridge_tools() {
	python3 - <<'PY'
from pathlib import Path
import re
import signal

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

generic_names = {
	"zombieland/get_status",
	"zombieland/list_zombies",
	"zombieland/sound_events_state",
	"zombieland/spawn_zombie",
	"zombieland/spawn_zombie_group",
	"zombieland/pheromone_state",
	"zombieland/place_wall_line",
	"zombieland/place_thing",
	"zombieland/start_map_fire",
	"zombieland/spawn_pawn_fixture",
	"zombieland/read_cell_things",
	"zombieland/spawn_colonist",
	"zombieland/spawn_spitter_visual_fixture",
	"zombieland/spawn_reference_lineup",
	"zombieland/read_contamination_state",
	"zombieland/read_contamination_effect_state",
	"zombieland/write_contamination_state",
	"zombieland/complete_frame_by_id",
	"zombieland/remove_all_zombies",
	"zombieland/get_pawn_infection",
	"zombieland/apply_zombie_bite",
	"zombieland/remove_pawn_infections",
	"zombieland/convert_pawn_to_zombie",
	"zombieland/wait_for_semantic_change",
}

scenario_fixture_names = {
	"zombieland/defense_room_state",
	"zombieland/incident_threat_state",
	"zombieland/infection_medical_state",
	"zombieland/infection_workflow_state",
	"zombieland/area_workflow_state",
	"zombieland/settings_state",
	"zombieland/special_gauntlet_state",
	"zombieland/uninstall_hygiene_state",
}

tool_pattern = re.compile(r'\[Tool\("([^"]+)"(?:,\s*Description\s*=\s*"([^"]*)")?')
for path in sorted(Path("Source/BridgeTools").glob("ZombielandBridgeTools*.cs")):
	lines = path.read_text(errors="ignore").splitlines()
	family = path.stem.replace("ZombielandBridgeTools.", "").replace("ZombielandBridgeTools", "Common")
	for line_number, line in enumerate(lines, start=1):
		match = tool_pattern.search(line)
		if not match:
			continue
		name, description = match.groups()
		if name in generic_names:
			kind = "generic-primitive"
		elif name in scenario_fixture_names:
			kind = "scenario-fixture"
		elif family == "RimConnect":
			kind = "optional-integration"
		elif name.endswith("_contract") or "contract" in name or "verify" in (description or "").lower():
			kind = "narrow-contract"
		else:
			kind = "evidence-helper"
		description = (description or "").replace("\t", " ").strip()
		print(f"{path}:{line_number}\t{family}\t{name}\t{kind}\t{description}")
PY
}

bridge_summary() {
	local bridge_inventory
	bridge_inventory="$(mktemp "${TMPDIR:-/tmp}/zl-bridge-inventory.XXXXXX")"
	bridge_tools > "$bridge_inventory"
	python3 - "$bridge_inventory" <<'PY'
import collections
from pathlib import Path
import signal
import sys

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

path = Path(sys.argv[1])
rows = []
for line in path.read_text().splitlines():
	line = line.rstrip("\n")
	if not line:
		continue
	parts = line.split("\t")
	if len(parts) != 5:
		continue
	location, family, tool_name, kind, description = parts
	rows.append({
		"location": location,
		"family": family,
		"tool_name": tool_name,
		"kind": kind,
		"description": description,
	})

kind_counts = collections.Counter(row["kind"] for row in rows)
family_counts = collections.Counter(row["family"] for row in rows)
family_kind_counts = collections.Counter((row["family"], row["kind"]) for row in rows)

print("section\tfamily\tkind_hint\tcount\tnotes")
for kind, count in sorted(kind_counts.items()):
	print(f"kind\tnot_applicable\t{kind}\t{count}\tall families")
for family, count in sorted(family_counts.items()):
	kinds = ", ".join(
		f"{kind}={family_kind_counts[(family, kind)]}"
		for kind in sorted(kind_counts)
		if family_kind_counts[(family, kind)]
	)
	print(f"family\t{family}\tall\t{count}\t{kinds}")

evidence_helpers = [row for row in rows if row["kind"] == "evidence-helper"]
if evidence_helpers:
	for row in evidence_helpers:
		print(
			"candidate_retire"
			f"\t{row['family']}"
			f"\t{row['kind']}"
			"\t1"
			f"\t{row['tool_name']} at {row['location']}"
		)
else:
	print("candidate_retire\tnot_applicable\tevidence-helper\t0\tno retained evidence-helper tools classified")

for row in sorted(
	(row for row in rows if row["kind"] == "narrow-contract"),
	key=lambda value: (value["family"], value["tool_name"]),
):
	if row["tool_name"].startswith("zombieland/setup_") or row["tool_name"].endswith("_observation"):
		print(
			"candidate_review"
			f"\t{row['family']}"
			f"\t{row['kind']}"
			"\t1"
			f"\t{row['tool_name']} at {row['location']}"
		)
PY
	rm -f "$bridge_inventory"
}

dependency_gates() {
	python3 - <<'PY'
import csv
from pathlib import Path

states = {
	"partial",
	"partial_runtime",
	"dependency/unavailable",
	"removed_vanilla_target",
}

standing_governance = {
	"A.HARMONY.INVENTORY",
	"J.BRIDGE.TOOLS",
	"NEG.1_6.DISCOVERY",
}

def gate_kind(row):
	row_id = row["id"]
	state = row["port_delta_state"]
	owner = row["owner_cluster"]
	row_type = row["row_type"]
	question = row["open_questions"].lower()

	if row_id in standing_governance:
		return "standing-governance"
	if state == "removed_vanilla_target":
		return "removed-target-doc"
	if row_type == "scenario":
		return "scenario-rollup"
	if owner == "optional_integrations":
		return "external-mod-unavailable"
	if state == "partial_runtime" and (
		"branch" in question
		or "delegated" in question
		or "geneassembler" in question
	):
		return "parent-branch-marker"
	if (
		"biotech" in question
		or "official dlc" in question
		or "child" in question
		or "killed-child" in question
		or "pollution-capable" in question
		or "wastepack" in question
		or "clearpollution" in question
		or "temporary terrain" in question
		or "tunnel jelly" in question
		):
		return "dlc-content-unavailable"
	return "actionable-gate"

path = Path("coverage/ZL_COVERAGE_INDEX.tsv")
with path.open(newline="") as handle:
	for row in csv.DictReader(handle, delimiter="\t"):
		if row["port_delta_state"] not in states:
			continue
		print("\t".join([
			row["id"],
			row["row_type"],
			row["owner_cluster"],
			row["evidence_state"],
			row["port_delta_state"],
			gate_kind(row),
			row["primary_scenario"],
			row["open_questions"].replace("\t", " ").strip(),
		]))
PY
}

source_paths() {
	python3 - <<'PY'
from pathlib import Path
import csv
import re
import signal

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

source_paths = sorted(
	str(path)
	for path in Path("Source").rglob("*.cs")
	if "/obj/" not in str(path)
)

owners = {path: [] for path in source_paths}
reference_columns = [
	"source_owners",
	"bridge_contracts",
	"harmony_patches",
	"notes",
]

with Path("coverage/ZL_COVERAGE_INDEX.tsv").open(newline="") as handle:
	for row in csv.DictReader(handle, delimiter="\t"):
		text = "\n".join(row.get(column, "") for column in reference_columns)
		for match in re.finditer(r"Source/[A-Za-z0-9_./+-]+\.cs", text):
			path = match.group(0)
			if path in owners:
				owners[path].append(row["id"])

print("source_path\tstatus\towner_ids")
for path in source_paths:
	ids = sorted(set(owners[path]))
	status = "covered" if ids else "unassigned"
	print(f"{path}\t{status}\t{';'.join(ids)}")
PY
}

row_state_summary() {
	python3 - <<'PY'
import collections
import csv
from pathlib import Path
import signal

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

with Path("coverage/ZL_COVERAGE_INDEX.tsv").open(newline="") as handle:
	rows = list(csv.DictReader(handle, delimiter="\t"))
print("section\trow_type\tstate_kind\tstate\tcount")

for row_type, count in sorted(collections.Counter(row["row_type"] for row in rows).items()):
	print(f"row_type\t{row_type}\ttotal\tall\t{count}")

for row_type in sorted({row["row_type"] for row in rows}):
	subset = [row for row in rows if row["row_type"] == row_type]
	for state, count in sorted(collections.Counter(row["evidence_state"] for row in subset).items()):
		print(f"evidence_state\t{row_type}\tevidence_state\t{state}\t{count}")
	for state, count in sorted(collections.Counter(row["port_delta_state"] for row in subset).items()):
		print(f"port_delta_state\t{row_type}\tport_delta_state\t{state}\t{count}")
PY
}

if [[ "$PATCH_GROUPS" == "1" ]]; then
	printf 'location\tclass\ttargeting\tpatch_attributes\n'
	patch_groups
	exit 0
fi

if [[ "$DYNAMIC_PATCHES" == "1" ]]; then
	printf 'location\tclass\tlookup_kind\towner\n'
	patch_groups \
		| awk -F '\t' '$3 == "dynamic" { print $1 "\t" $2 }' \
		| while IFS=$'\t' read -r location class_name; do
			file="${location%%:*}"
			start="${location##*:}"
			snippet="$(sed -n "${start},$((start + 45))p" "$file")"
			if printf '%s\n' "$snippet" | rg -q 'AccessTools\.Method\("[^"]+:'; then
				lookup_kind="external-string"
			elif printf '%s\n' "$snippet" | rg -q 'AccessTools\.TypeByName'; then
				lookup_kind="external-typename"
			elif printf '%s\n' "$snippet" | rg -q 'AccessTools\.(FirstInner|FirstMethod)|InnerMethodsStartingWith'; then
				lookup_kind="closure-search"
			elif printf '%s\n' "$snippet" | rg -q 'TargetMethods\('; then
				lookup_kind="multi-target"
			elif printf '%s\n' "$snippet" | rg -q 'AccessTools\.Method\(typeof|AccessTools\.Constructor\(typeof|SymbolExtensions.GetMethodInfo'; then
				lookup_kind="typed-reflection"
			else
				lookup_kind="dynamic-other"
			fi
			case "$file" in
				*Patches.cs) owner="core" ;;
				*Contamination*) owner="contamination" ;;
				*CETools.cs|*SoSTools.cs|*RimConnectSupport.cs) owner="optional-integration" ;;
				*) owner="specialized" ;;
			esac
			printf '%s\t%s\t%s\t%s\n' "$location" "$class_name" "$lookup_kind" "$owner"
		done
	exit 0
fi

if [[ "$STATIC_SUMMARY" == "1" ]]; then
	printf 'group\tstatic_rows\tscenario_hint\tfiles\n'
	patch_groups \
		| awk -F '\t' '
			$3 == "static" {
				file = $1
				sub(/:.*/, "", file)
				line = $1
				sub(/^.*:/, "", line)
				line += 0

				if (file == "Source/Patches.cs") {
					if (line < 600) {
						group = "patches-startup-ui-tick"
						scenario = "S-Source-Patch-Audit,S-Settings-Persistence,S-Core-Horde-Loop"
					} else if (line < 1300) {
						group = "patches-chainsaw-equipment-jobs"
						scenario = "S-Defense-Room"
					} else if (line < 2100) {
						group = "patches-avoidance-doors-workgivers"
						scenario = "S-Settings-Persistence,S-Core-Horde-Loop,S-Defense-Room"
					} else if (line < 2800) {
						group = "patches-zombie-lifecycle-combat"
						scenario = "S-Core-Horde-Loop,S-Special-Gauntlet"
					} else if (line < 3700) {
						group = "patches-render-specials-apparel"
						scenario = "S-Special-Gauntlet,S-Settings-Persistence"
					} else if (line < 4700) {
						group = "patches-damage-infection-hostility"
						scenario = "S-Core-Horde-Loop,S-Special-Gauntlet"
					} else {
						group = "patches-records-clamor-misc"
						scenario = "S-Core-Horde-Loop,S-Incident-Threat"
					}
				} else if (file ~ /^Source\/ContaminationPatches/) {
					group = "contamination-static"
					scenario = "S-Contamination-Persistence"
				} else if (file == "Source/Patches_Hostility.cs") {
					group = "hostility-static"
					scenario = "S-Special-Gauntlet,S-Defense-Room"
				} else if (file == "Source/ZombieAreaManager.cs") {
					group = "area-manager-static"
					scenario = "S-Settings-Persistence,S-Defense-Room"
				} else if (file == "Source/ZombieDamageFlasher.cs") {
					group = "damage-flasher-static"
					scenario = "S-Special-Gauntlet"
				} else if (file == "Source/ZombieLeaner.cs") {
					group = "zombie-leaner-static"
					scenario = "S-Core-Horde-Loop,S-Special-Gauntlet"
				} else if (file == "Source/Service.cs") {
					group = "service-static"
					scenario = "S-Source-Patch-Audit"
				} else if (file == "Source/Assets.cs") {
					group = "assets-static"
					scenario = "S-Source-Patch-Audit"
				} else {
					group = file
					scenario = "S-Source-Patch-Audit"
				}

				count[group]++
				scenarios[group] = scenario
				if (files[group] !~ file) {
					files[group] = files[group] == "" ? file : files[group] "," file
				}
			}
			END {
				for (group in count)
					printf "%s\t%d\t%s\t%s\n", group, count[group], scenarios[group], files[group]
			}
		' \
		| sort
	exit 0
fi

if [[ "$BRIDGE_TOOLS" == "1" ]]; then
	printf 'location\tfamily\ttool_name\tkind_hint\tdescription\n'
	bridge_tools
	exit 0
fi

if [[ "$BRIDGE_SUMMARY" == "1" ]]; then
	bridge_summary
	exit 0
fi

if [[ "$DEPENDENCY_GATES" == "1" ]]; then
	printf 'id\trow_type\towner_cluster\tevidence_state\tport_delta_state\tgate_kind\tprimary_scenario\topen_questions\n'
	dependency_gates
	exit 0
fi

if [[ "$SOURCE_PATHS" == "1" ]]; then
	source_paths
	exit 0
fi

if [[ "$ROW_STATE_SUMMARY" == "1" ]]; then
	row_state_summary
	exit 0
fi

section() {
	printf '\n== %s ==\n' "$1"
}

section "Scope"
printf 'baseline: %s\n' "$BASELINE"
printf 'source files: '
find Source -type f -name '*.cs' -not -path '*/obj/*' | wc -l | tr -d ' '
printf '\n'
printf 'commits since baseline: '
git rev-list --count "$BASELINE"..HEAD

section "Scenario Definitions"
if [[ -f TEST_SCENARIOS.md ]]; then
	rg -n '^## S-' TEST_SCENARIOS.md | sed 's/^TEST_SCENARIOS.md://'
else
	printf 'missing TEST_SCENARIOS.md\n'
fi

section "Coverage Clusters"
if [[ -f TEST_COVERAGE.md ]]; then
	rg -n '^### [A-Z]\.' TEST_COVERAGE.md | sed 's/^TEST_COVERAGE.md://'
else
	printf 'missing TEST_COVERAGE.md\n'
fi

section "Harmony Patch Files"
rg -n '\[HarmonyPatch' Source -g '!obj/**' \
	| awk -F: '{ count[$1]++ } END { for (file in count) printf "%4d %s\n", count[file], file }' \
	| sort -nr

section "Bridge Tool Files"
rg -n 'public static object ' Source/BridgeTools -g '*.cs' \
	| awk -F: '{ count[$1]++ } END { for (file in count) printf "%4d %s\n", count[file], file }' \
	| sort -nr

section "Gameplay Class Summary"
rg -n '^[[:space:]]*(public|internal|static|sealed|abstract|partial|class).*class [A-Za-z0-9_]+[[:space:]]*:[[:space:]]*(JobDriver|ThinkNode_JobGiver|WorkGiver|ThingComp|MapComponent|WorldComponent|ModSettings|Window|Page|Need|Hediff|Recipe|PlaceWorker|Alert|Thought|Command|Thing|Pawn|Building|QuestNode|QuestPart)' Source -g '!obj/**' \
	| sed -E 's#^.*:[[:space:]]*(public |internal |static |sealed |abstract |partial )*class [^:{]+.*:[[:space:]]*([^,{]+).*#\2#' \
	| sort \
	| uniq -c \
	| sort -nr

section "Serialization Tick UI Hook Summary"
rg -n 'ExposeData\(|WorldComponentTick|MapComponentTick|QuestPartTick|DoWindowContents|DoSettingsWindowContents|GetGizmos|CompGetGizmosExtra|DebugAction' Source -g '!obj/**' -g '!Source/BridgeTools/**' \
	| sed -E 's#^([^:]+):.*#\1#' \
	| sort \
	| uniq -c \
	| sort -nr

if [[ "$DETAILS" == "1" ]]; then
	section "Bridge Tool Names"
	rg -n 'public static object ' Source/BridgeTools -g '*.cs' \
		| sed -E 's#^([^:]+):([0-9]+):[[:space:]]*public static object ([A-Za-z0-9_]+).*#\1:\2 \3#' \
		| sort

	section "Gameplay Classes"
	rg -n '^[[:space:]]*(public|internal|static|sealed|abstract|partial|class).*class [A-Za-z0-9_]+[[:space:]]*:[[:space:]]*(JobDriver|ThinkNode_JobGiver|WorkGiver|ThingComp|MapComponent|WorldComponent|ModSettings|Window|Page|Need|Hediff|Recipe|PlaceWorker|Alert|Thought|Command|Thing|Pawn|Building|QuestNode|QuestPart)' Source -g '!obj/**' \
		| sed -E 's#^([^:]+):([0-9]+):[[:space:]]*(public |internal |static |sealed |abstract |partial )*class ([^:{]+).*:[[:space:]]*([^,{]+).*#\1:\2 \4 -> \5#' \
		| sort

	section "Serialization Tick UI Hooks"
	rg -n 'ExposeData\(|WorldComponentTick|MapComponentTick|QuestPartTick|DoWindowContents|DoSettingsWindowContents|GetGizmos|CompGetGizmosExtra|DebugAction' Source -g '!obj/**' -g '!Source/BridgeTools/**' \
		| sed -E 's#^([^:]+):([0-9]+):[[:space:]]*#\1:\2 #' \
		| sort

	section "Evidence Commits"
	git log --reverse --format='%h %ad %s' --date=short "$BASELINE"..HEAD \
		| rg ' (Cover|Add .*bridge smoke|Recheck|Record|Restore|Fix|Preserve|Verify|Optimize|Trim|Harden) '
else
	section "Details"
	printf 'run with --details for bridge tool names, gameplay class lines, hook lines, and evidence commits\n'
fi

section "Coverage Keyword Signals"
coverage_text="/tmp/zl-coverage-text.$$"
commit_text="/tmp/zl-covered-subjects.$$"
trap 'rm -f "$coverage_text" "$commit_text"' EXIT
{
	for path in TEST_COVERAGE.md TEST_SCENARIOS.md TEST_PATCH_AUDIT.md coverage/ZL_COVERAGE_INDEX.tsv coverage/ZL_UI_SURFACE_INDEX.tsv coverage/UNASSIGNED_SURFACES.tsv coverage/COVERAGE_COMPLETENESS_REPORT.md
	do
		[[ -f "$path" ]] && printf '\n== %s ==\n' "$path" && cat "$path"
	done
} > "$coverage_text"
git log --format='%s' "$BASELINE"..HEAD > "$commit_text"
keywords=(
	settings
	keyframe
	defaults
	uninstall
	apparel
	biome
	advanced
	modal
	alert
	quest
	decontamination
	forecast
	tooltip
	audio
	sound
	CameraPlus
	Dubs
	"Save Our Ship"
	Vehicle
	"Combat Extended"
	"save load"
	save-load
	persistence
	serializer
	dense
	performance
)
for keyword in "${keywords[@]}"
do
	if rg -qi --fixed-strings "$keyword" "$coverage_text"; then
		if rg -qi --fixed-strings "$keyword" "$commit_text"; then
			printf 'documented-and-commit-subject: %s\n' "$keyword"
		else
			printf 'documented-only: %s\n' "$keyword"
		fi
	else
		printf 'not-in-coverage-text: %s\n' "$keyword"
	fi
done
