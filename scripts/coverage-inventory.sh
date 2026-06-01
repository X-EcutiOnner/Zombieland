#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BASELINE="a0de6c309ab789a488a6bc106d281b5d0ca79020"
DETAILS=0
PATCH_GROUPS=0
DYNAMIC_PATCHES=0
STATIC_SUMMARY=0
PATCH_ROW_GAPS=0
BRIDGE_TOOLS=0
BRIDGE_SUMMARY=0
BRIDGE_PRESSURE=0
BRIDGE_NEXT=0
DEPENDENCY_GATES=0
DEPENDENCY_GATE_SUMMARY=0
ACTIONABLE_GATES=0
SOURCE_PATHS=0
ROW_STATE_SUMMARY=0
CONSISTENCY_CHECKS=0

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
		--patch-row-gaps)
			PATCH_ROW_GAPS=1
			;;
		--bridge-tools)
			BRIDGE_TOOLS=1
			;;
		--bridge-summary)
			BRIDGE_SUMMARY=1
			;;
		--bridge-pressure)
			BRIDGE_PRESSURE=1
			;;
		--bridge-next)
			BRIDGE_NEXT=1
			;;
		--dependency-gates)
			DEPENDENCY_GATES=1
			;;
		--dependency-gate-summary)
			DEPENDENCY_GATE_SUMMARY=1
			;;
		--actionable-gates)
			ACTIONABLE_GATES=1
			;;
		--source-paths)
			SOURCE_PATHS=1
			;;
		--row-state-summary)
			ROW_STATE_SUMMARY=1
			;;
		--consistency-checks)
			CONSISTENCY_CHECKS=1
			;;
		--help|-h)
			printf 'usage: %s [--details] [--patch-groups] [--dynamic-patches] [--static-summary] [--patch-row-gaps] [--bridge-tools] [--bridge-summary] [--bridge-pressure] [--bridge-next] [--dependency-gates] [--dependency-gate-summary] [--actionable-gates] [--source-paths] [--row-state-summary] [--consistency-checks] [baseline-commit]\n' "$0"
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

patch_row_gaps() {
	local patch_inventory
	patch_inventory="$(mktemp "${TMPDIR:-/tmp}/zl-patch-inventory.XXXXXX")"
	patch_groups > "$patch_inventory"
	python3 - "$patch_inventory" <<'PY'
import csv
from pathlib import Path
import signal
import sys

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

patch_inventory = Path(sys.argv[1])
coverage_path = Path("coverage/ZL_COVERAGE_INDEX.tsv")
audit_text = Path("TEST_PATCH_AUDIT.md").read_text(errors="ignore") if Path("TEST_PATCH_AUDIT.md").exists() else ""

with coverage_path.open(newline="") as handle:
	rows = list(csv.DictReader(handle, delimiter="\t"))

row_ids = {row["id"] for row in rows}
reference_columns = [
	"id",
	"source_owners",
	"bridge_contracts",
	"harmony_patches",
	"tool_or_target",
	"evidence_refs",
	"notes",
]

print("location\tclass\ttargeting\texact_patch_id\tdisposition_hint\trepresented_by\taudit_mentions")
for line in patch_inventory.read_text().splitlines():
	if not line.strip():
		continue
	location, class_name, targeting, attrs = line.split("\t", 3)
	exact_id = f"PATCH.{class_name}"
	if exact_id in row_ids:
		continue
	represented_by = []
	for row in rows:
		text = "\n".join(row.get(column, "") for column in reference_columns)
		if class_name in text:
			represented_by.append(row["id"])

	audit_mentions = audit_text.count(class_name)
	if targeting == "base":
		disposition = "base/context"
	elif represented_by:
		disposition = "represented-by-row"
	elif audit_mentions:
		disposition = "audit-only"
	else:
		disposition = "unrepresented"

	print(
		f"{location}\t{class_name}\t{targeting}\t{exact_id}\t{disposition}"
		f"\t{';'.join(sorted(set(represented_by)))}\t{audit_mentions}"
	)
PY
	rm -f "$patch_inventory"
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
	"zombieland/create_sos_space_map_fixture",
	"zombieland/sos2_ship_hologram_state",
}

tool_pattern = re.compile(r'\[Tool\("([^"]+)"(?:,\s*Description\s*=\s*"([^"]*)")?')

semantic_family_overrides = {
	"zombieland/fogged_door_spawns_room_zombies": "Fog",
	"zombieland/fog_blocker_removal_spawns_room_zombies": "Fog",
	"zombieland/fog_blocker_replacement_does_not_spawn_room_zombies": "Fog",
	"zombieland/detonate_suicide_bomber": "SpecialZombies",
	"zombieland/suicide_bomber_countdown_contract": "SpecialZombies",
	"zombieland/kill_toxic_splasher": "SpecialZombies",
	"zombieland/move_dark_slimer": "SpecialZombies",
	"zombieland/heal_wounded_zombie": "SpecialZombies",
	"zombieland/heal_wounded_zombie_tick": "SpecialZombies",
	"zombieland/emp_electrifier": "SpecialZombies",
	"zombieland/electrify_powered_building": "SpecialZombies",
	"zombieland/active_electrifier_attack_verb_contract": "SpecialZombies",
	"zombieland/active_electrifier_bullet_absorption_contract": "SpecialZombies",
	"zombieland/active_electrifier_melee_shock_contract": "SpecialZombies",
	"zombieland/albino_melee_bite_hidden_contract": "SpecialZombies",
	"zombieland/hostility_to_zombies_contract": "Hostility",
	"zombieland/zombie_active_threat_count_contract": "Hostility",
	"zombieland/zombie_target_cache_excludes_specials": "Hostility",
	"zombieland/zombie_faction_world_contract": "Incidents",
	"zombieland/infected_incident_hooks_contract": "Incidents",
	"zombieland/zombie_faction_pawn_generation_contract": "Incidents",
	"zombieland/zombie_ticking_budget_contract": "Core",
	"zombieland/zombie_ticking_feedback_contract": "Core",
}

for path in sorted(Path("Source/BridgeTools").glob("ZombielandBridgeTools*.cs")):
	lines = path.read_text(errors="ignore").splitlines()
	family = path.stem.replace("ZombielandBridgeTools.", "").replace("ZombielandBridgeTools", "Common")
	for line_number, line in enumerate(lines, start=1):
		match = tool_pattern.search(line)
		if not match:
			continue
		name, description = match.groups()
		semantic_family = semantic_family_overrides.get(name, family)
		if name in generic_names:
			kind = "generic-primitive"
		elif name in scenario_fixture_names:
			kind = "scenario-fixture"
		elif semantic_family == "RimConnect":
			kind = "optional-integration"
		elif name.endswith("_contract") or "contract" in name or "verify" in (description or "").lower():
			kind = "narrow-contract"
		else:
			kind = "evidence-helper"
		description = (description or "").replace("\t", " ").strip()
		print(f"{path}:{line_number}\t{semantic_family}\t{name}\t{kind}\t{description}")
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

bridge_pressure() {
	local bridge_inventory
	bridge_inventory="$(mktemp "${TMPDIR:-/tmp}/zl-bridge-inventory.XXXXXX")"
	bridge_tools > "$bridge_inventory"
	python3 - "$bridge_inventory" <<'PY'
import collections
from pathlib import Path
import signal
import sys

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

rows = []
for line in Path(sys.argv[1]).read_text().splitlines():
	if not line.strip():
		continue
	parts = line.split("\t")
	if len(parts) != 5:
		continue
	location, family, tool_name, kind, description = parts
	rows.append((family, kind, tool_name, location))

print("family\ttotal\tgeneric_primitives\tscenario_fixtures\tnarrow_contracts\toptional_integrations\tpressure\trecommendation")
family_rows = []
for family in sorted({family for family, kind, tool_name, location in rows}):
	subset = [(kind, tool_name, location) for fam, kind, tool_name, location in rows if fam == family]
	counts = collections.Counter(kind for kind, tool_name, location in subset)
	narrow = counts["narrow-contract"]
	scenario = counts["scenario-fixture"]
	generic = counts["generic-primitive"]
	optional = counts["optional-integration"]
	total = len(subset)
	if narrow == 0:
		pressure = "none"
		recommendation = "no narrow-contract consolidation pressure"
	elif scenario == 0 and narrow >= 8:
		pressure = "high"
		recommendation = "consider a named scenario fixture or generic primitive before adding more contracts"
	elif scenario > 0 and narrow >= 8:
		pressure = "medium"
		recommendation = "prefer extending existing scenario fixture or generic primitive before adding contracts"
	elif narrow >= 4:
		pressure = "medium"
		recommendation = "review whether next evidence belongs in a scenario fixture"
	else:
		pressure = "low"
		recommendation = "retain unless a future scenario preserves the evidence"
	family_rows.append((family, total, generic, scenario, narrow, optional, pressure, recommendation))

pressure_order = {"high": 0, "medium": 1, "low": 2, "none": 3}
for family, total, generic, scenario, narrow, optional, pressure, recommendation in sorted(
	family_rows,
	key=lambda row: (pressure_order[row[6]], -row[4], row[0]),
):
	print(f"{family}\t{total}\t{generic}\t{scenario}\t{narrow}\t{optional}\t{pressure}\t{recommendation}")
PY
	rm -f "$bridge_inventory"
}

bridge_next() {
	local bridge_inventory
	bridge_inventory="$(mktemp "${TMPDIR:-/tmp}/zl-bridge-inventory.XXXXXX")"
	bridge_tools > "$bridge_inventory"
	python3 - "$bridge_inventory" <<'PY'
import collections
import csv
from pathlib import Path
import signal
import sys

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

family_owner_rows = {
	"Chainsaw": ["J.BUILDINGS.ITEMS"],
	"Contamination": ["G.CONTAMINATION.C4_ENVIRONMENT"],
	"Core": ["C.CORE.ZOMBIE_LOOP"],
	"CorpsesAndAvoidance": ["D.PATHING.DOOR_AVOIDANCE", "I.INFECTION.MEDICAL"],
	"DefenseRoom": ["J.BUILDINGS.ITEMS", "D.PATHING.DOOR_AVOIDANCE"],
	"Eating": ["C.CORE.ZOMBIE_LOOP", "I.INFECTION.MEDICAL"],
	"Environment": ["C.CORE.ZOMBIE_LOOP", "G.CONTAMINATION.C4_ENVIRONMENT"],
	"Fog": ["H.QUESTS.INCIDENTS"],
	"Hostility": ["E.HOSTILITY.TARGETING"],
	"Incidents": ["H.QUESTS.INCIDENTS"],
	"Infection": ["I.INFECTION.MEDICAL"],
	"ProjectilesAndArea": ["E.HOSTILITY.TARGETING", "F.RENDERING.VISUALS"],
	"RimConnect": ["INT.RimConnect"],
	"SemanticWait": ["J.BRIDGE.TOOLS"],
	"Settings": ["B.SETTINGS.WORLD_DEFAULTS"],
	"SoS2": ["BRIDGE.SoS2", "K.OPTIONAL.INTEGRATIONS"],
	"Social": ["K.SOCIAL.SELECTION"],
	"SpecialGauntlet": ["SCENARIO.S.SPECIAL.GAUNTLET", "C.SPECIAL.ZOMBIES"],
	"SpecialZombies": ["C.SPECIAL.ZOMBIES", "SCENARIO.S.SPECIAL.GAUNTLET"],
	"UninstallHygiene": ["M.UNINSTALL.HYGIENE"],
	"WallPush": ["J.BUILDINGS.ITEMS", "D.PATHING.DOOR_AVOIDANCE"],
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
	unresolved_state = state in {"partial", "partial_runtime", "dependency/unavailable", "removed_vanilla_target"}

	if row_id in standing_governance:
		return "standing-governance"
	if state == "removed_vanilla_target":
		return "removed-target-doc"
	if row_type == "scenario" and state in {"partial", "partial_runtime", "dependency/unavailable"}:
		return "scenario-rollup"
	if owner == "optional_integrations" and state in {"partial", "partial_runtime", "dependency/unavailable"}:
		return "external-mod-unavailable"
	if state == "partial_runtime" and (
		"branch" in question
		or "delegated" in question
		or "geneassembler" in question
	):
		return "parent-branch-marker"
	if unresolved_state and (
		"biotech" in question
		or "odyssey" in question
		or "official dlc" in question
		or "child" in question
		or "killed-child" in question
		or "pollution-capable" in question
		or "wastepack" in question
		or "clearpollution" in question
		or "temporary terrain" in question
		or "tunnel jelly" in question
		or "sandgrid" in question
	):
		return "dlc-content-unavailable"
	if unresolved_state:
		return "actionable-gate"
	return "resolved"

def backlog_kind(owner_rows):
	kinds = {gate_kind(row) for row in owner_rows}
	states = {row["port_delta_state"] for row in owner_rows}
	if "actionable-gate" in kinds:
		return "local-action"
	if kinds & {"dlc-content-unavailable", "external-mod-unavailable"}:
		return "dependency-only"
	if kinds and kinds <= {"standing-governance", "scenario-rollup", "parent-branch-marker", "removed-target-doc"}:
		return "governance-only"
	if states and states <= {"resolved", "resolved_runtime", "obsolete/disposed"}:
		return "resolved-additive"
	if not owner_rows:
		return "unmapped"
	return "review"

def recommendation(backlog, narrow, scenario):
	if backlog == "local-action":
		return "do source/decompiler pass, then runtime only if needed"
	if backlog == "dependency-only":
		return "do not retest locally until dependency/setup exists"
	if backlog == "governance-only":
		return "keep as standing audit; no gameplay retest implied"
	if backlog == "resolved-additive":
		if narrow >= 8 and scenario == 0:
			return "behavior covered; add scenario fixture only for a named new variant"
		return "behavior covered; future work must be a named regression or variant"
	if backlog == "unmapped":
		return "map this bridge family to coverage rows before adding tests"
	return "inspect owner rows before adding tests"

tool_rows = []
for line in Path(sys.argv[1]).read_text().splitlines():
	if not line.strip():
		continue
	parts = line.split("\t")
	if len(parts) != 5:
		continue
	location, family, tool_name, kind, description = parts
	tool_rows.append((family, kind, tool_name, location))

coverage_rows = {}
with Path("coverage/ZL_COVERAGE_INDEX.tsv").open(newline="") as handle:
	for row in csv.DictReader(handle, delimiter="\t"):
		coverage_rows[row["id"]] = row

print("family\ttotal\tnarrow_contracts\tscenario_fixtures\towner_rows\towner_states\tbacklog_kind\tnext_step")
output = []
for family in sorted({family for family, kind, tool_name, location in tool_rows}):
	subset = [(kind, tool_name, location) for fam, kind, tool_name, location in tool_rows if fam == family]
	counts = collections.Counter(kind for kind, tool_name, location in subset)
	owners = [coverage_rows[row_id] for row_id in family_owner_rows.get(family, []) if row_id in coverage_rows]
	backlog = backlog_kind(owners)
	owner_ids = ";".join(row["id"] for row in owners) or "unmapped"
	owner_states = ";".join(f"{row['id']}={row['port_delta_state']}" for row in owners) or "unmapped"
	output.append((
		backlog,
		-counts["narrow-contract"],
		family,
		len(subset),
		counts["narrow-contract"],
		counts["scenario-fixture"],
		owner_ids,
		owner_states,
		recommendation(backlog, counts["narrow-contract"], counts["scenario-fixture"]),
	))

backlog_order = {
	"local-action": 0,
	"unmapped": 1,
	"review": 2,
	"dependency-only": 3,
	"governance-only": 4,
	"resolved-additive": 5,
}
for backlog, negative_narrow, family, total, narrow, scenario, owner_ids, owner_states, next_step in sorted(
	output,
	key=lambda row: (backlog_order.get(row[0], 9), row[1], row[2]),
):
	print(f"{family}\t{total}\t{narrow}\t{scenario}\t{owner_ids}\t{owner_states}\t{backlog}\t{next_step}")
PY
	rm -f "$bridge_inventory"
}

dependency_gates() {
	local mode="${1:-all}"
	python3 - "$mode" <<'PY'
import csv
import sys
from pathlib import Path

mode = sys.argv[1]

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
		or "odyssey" in question
		or "official dlc" in question
		or "child" in question
		or "killed-child" in question
		or "pollution-capable" in question
		or "wastepack" in question
		or "clearpollution" in question
		or "temporary terrain" in question
		or "tunnel jelly" in question
		or "sandgrid" in question
		):
		return "dlc-content-unavailable"
	return "actionable-gate"

path = Path("coverage/ZL_COVERAGE_INDEX.tsv")
rows = []
with path.open(newline="") as handle:
	for row in csv.DictReader(handle, delimiter="\t"):
		if row["port_delta_state"] not in states:
			continue
		kind = gate_kind(row)
		rows.append((kind, row))

if mode == "summary":
	counts = {}
	for kind, row in rows:
		counts[kind] = counts.get(kind, 0) + 1
	for kind in sorted(counts):
		print(f"{kind}\t{counts[kind]}")
elif mode == "actionable":
	for kind, row in rows:
		if kind != "actionable-gate":
			continue
		print("\t".join([
			row["id"],
			row["row_type"],
			row["owner_cluster"],
			row["evidence_state"],
			row["port_delta_state"],
			kind,
			row["primary_scenario"],
			row["open_questions"].replace("\t", " ").strip(),
		]))
else:
	for kind, row in rows:
		print("\t".join([
			row["id"],
			row["row_type"],
			row["owner_cluster"],
			row["evidence_state"],
			row["port_delta_state"],
			kind,
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

coverage_report_patch_counts() {
	python3 - <<'PY'
import collections
import csv
import re
import signal
import sys
from pathlib import Path

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

coverage_path = Path("coverage/ZL_COVERAGE_INDEX.tsv")
report_path = Path("coverage/COVERAGE_COMPLETENESS_REPORT.md")

with coverage_path.open(newline="") as handle:
	rows = [row for row in csv.DictReader(handle, delimiter="\t") if row["row_type"] == "patch"]

expected = {
	"evidence": collections.Counter(row["evidence_state"] for row in rows),
	"port": collections.Counter(row["port_delta_state"] for row in rows),
}

report = report_path.read_text(errors="ignore")

def extract_distribution(start_marker):
	start = report.find(start_marker)
	if start < 0:
		return None
	rest = report[start:].splitlines()[1:]
	result = {}
	seen_distribution = False
	for line in rest:
		if not line.strip():
			if seen_distribution:
				break
			continue
		if not line.lstrip().startswith("- "):
			if seen_distribution:
				break
			continue
		seen_distribution = True
		match = re.match(r"- ([^:]+): ([0-9]+)$", line.strip())
		if match:
			result[match.group(1)] = int(match.group(2))
		else:
			break
	return result

actual = {
	"evidence": extract_distribution("Patch evidence-state distribution among explicit patch rows:"),
	"port": extract_distribution("Patch port-delta distribution among explicit patch rows"),
}

failures = []
for section in ("evidence", "port"):
	if actual[section] is None:
		failures.append(f"missing {section} distribution section")
		continue
	expected_dict = dict(sorted(expected[section].items()))
	actual_dict = dict(sorted(actual[section].items()))
	if actual_dict != expected_dict:
		failures.append(f"{section} expected {expected_dict} but report has {actual_dict}")

if failures:
	print("; ".join(failures))
	sys.exit(1)

print("patch evidence and port distributions match row-state summary")
PY
}

coverage_report_patch_gap_counts() {
	local expected
	expected="$(patch_row_gaps | awk -F '\t' '
		NR > 1 {
			total++
			count[$5]++
		}
		END {
			printf "%d\t%d\t%d\t%d\t%d",
				total + 0,
				count["represented-by-row"] + 0,
				count["audit-only"] + 0,
				count["base/context"] + 0,
				count["unrepresented"] + 0
		}
	')"
	python3 - "$expected" <<'PY'
import re
import signal
import sys
from pathlib import Path

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

expected = tuple(int(value) for value in sys.argv[1].split("\t"))
report = Path("coverage/COVERAGE_COMPLETENESS_REPORT.md").read_text(errors="ignore")
match = re.search(
	r"reports\s+([0-9]+)\s+exact-name misses:\s+"
	r"([0-9]+)\s+represented\b.*?"
	r"([0-9]+)\s+audit-only\b.*?"
	r"([0-9]+)\s+base/context\b.*?"
	r"([0-9]+)\s+unrepresented",
	report,
	re.S,
)

if not match:
	print("missing exact-name patch-row gap distribution in report")
	sys.exit(1)

actual = tuple(int(value) for value in match.groups())
if actual != expected:
	print(
		"expected total/represented/audit/base/unrepresented "
		f"{expected} but report has {actual}"
	)
	sys.exit(1)

print("exact-name patch-row gap distribution matches generated output")
PY
}

coverage_report_dependency_gate_counts() {
	local expected
	expected="$(dependency_gates summary | awk -F '\t' '
		{
			count[$1] = $2
		}
		END {
			printf "%d\t%d\t%d\t%d\t%d",
				count["dlc-content-unavailable"] + 0,
				count["parent-branch-marker"] + 0,
				count["scenario-rollup"] + 0,
				count["standing-governance"] + 0,
				count["removed-target-doc"] + 0
		}
	')"
	python3 - "$expected" <<'PY'
import re
import signal
import sys
from pathlib import Path

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

expected = tuple(int(value) for value in sys.argv[1].split("\t"))
report = Path("coverage/COVERAGE_COMPLETENESS_REPORT.md").read_text(errors="ignore")
match = re.search(
	r"dependency-gate-summary`\s+distribution\b.*?is:\s+"
	r"([0-9]+)\s+DLC/content gates,\s+"
	r"([0-9]+)\s+parent branch markers,\s+"
	r"([0-9]+)\s+scenario rollup,\s+"
	r"([0-9]+)\s+standing-governance rows,\s+and\s+"
	r"([0-9]+)\s+removed-target documentation row",
	report,
	re.S,
)

if not match:
	print("missing dependency-gate summary distribution in report")
	sys.exit(1)

actual = tuple(int(value) for value in match.groups())
if actual != expected:
	print(
		"expected dlc/parent/scenario/governance/removed "
		f"{expected} but report has {actual}"
	)
	sys.exit(1)

print("dependency-gate distribution matches generated output")
PY
}

coverage_report_bridge_counts() {
	python3 - <<'PY'
import collections
import csv
import re
import signal
import subprocess
import sys
from pathlib import Path

signal.signal(signal.SIGPIPE, signal.SIG_DFL)

with Path("coverage/ZL_COVERAGE_INDEX.tsv").open(newline="") as handle:
	coverage_rows = list(csv.DictReader(handle, delimiter="\t"))

bridge_rows = [row for row in coverage_rows if row["row_type"] == "bridge_contract"]
individual_bridge_rows = [
	row for row in bridge_rows
	if row["id"].startswith("BRIDGE.") and row["id"] != "J.BRIDGE.TOOLS"
]

bridge_tools_output = subprocess.check_output(
	["bash", "scripts/coverage-inventory.sh", "--bridge-tools"],
	text=True,
)
tool_rows = list(csv.DictReader(bridge_tools_output.splitlines(), delimiter="\t"))
tool_files = {
	row["location"].split(":", 1)[0]
	for row in tool_rows
}
kind_counts = collections.Counter(row["kind_hint"] for row in tool_rows)
expected_kind_counts = {
	"generic-primitive": kind_counts.get("generic-primitive", 0),
	"scenario-fixture": kind_counts.get("scenario-fixture", 0),
	"narrow-contract": kind_counts.get("narrow-contract", 0),
	"evidence-helper": kind_counts.get("evidence-helper", 0),
	"optional-integration": kind_counts.get("optional-integration", 0),
}

report = Path("coverage/COVERAGE_COMPLETENESS_REPORT.md").read_text(errors="ignore")

rows_match = re.search(
	r"Bridge rows generated:\s+([0-9]+)\.\s+.*?current\s+([0-9]+)\s+"
	r"`Source/BridgeTools/ZombielandBridgeTools\.\*\.cs` files.*?"
	r"source has\s+([0-9]+)\s+public Zombieland bridge tools across\s+"
	r"([0-9]+)\s+Tool-bearing files",
	report,
	re.S,
)
if not rows_match:
	print("missing bridge row/tool headline counts in report")
	sys.exit(1)

actual_headline = tuple(int(value) for value in rows_match.groups())
expected_headline = (
	len(bridge_rows),
	len(tool_files),
	len(tool_rows),
	len(tool_files),
)

kind_match = re.search(
	r"Current output confirms\s+([0-9]+)\s+Tool attributes across\s+"
	r"([0-9]+)\s+Tool-bearing files:\s+"
	r"([0-9]+)\s+`generic-primitive`,\s+"
	r"([0-9]+)\s+`scenario-fixture`,\s+"
	r"([0-9]+)\s+`narrow-contract`,\s+"
	r"([0-9]+)\s+`evidence-helper`,\s+and\s+"
	r"([0-9]+)\s+`optional-integration` tools",
	report,
	re.S,
)
if not kind_match:
	print("missing bridge kind distribution in report")
	sys.exit(1)

actual_kind = tuple(int(value) for value in kind_match.groups())
expected_kind = (
	len(tool_rows),
	len(tool_files),
	expected_kind_counts["generic-primitive"],
	expected_kind_counts["scenario-fixture"],
	expected_kind_counts["narrow-contract"],
	expected_kind_counts["evidence-helper"],
	expected_kind_counts["optional-integration"],
)

individual_match = re.search(r"All\s+([0-9]+)\s+individual `BRIDGE\.\*` family rows", report)
if not individual_match:
	print("missing individual BRIDGE row count in report")
	sys.exit(1)

actual_individual = int(individual_match.group(1))
expected_individual = len(individual_bridge_rows)

failures = []
if actual_headline != expected_headline:
	failures.append(f"headline expected {expected_headline} but report has {actual_headline}")
if actual_kind != expected_kind:
	failures.append(f"kind distribution expected {expected_kind} but report has {actual_kind}")
if actual_individual != expected_individual:
	failures.append(f"individual rows expected {expected_individual} but report has {actual_individual}")

if failures:
	print("; ".join(failures))
	sys.exit(1)

print("bridge row/tool/kind distributions match generated output")
PY
}

consistency_checks() {
	local failures=0

	printf 'check\tstatus\tdetails\n'

	if awk -F '\t' 'NR == 1 { expected = NF; next } NF != expected { bad = 1 } END { exit bad }' coverage/ZL_COVERAGE_INDEX.tsv; then
		printf 'coverage_tsv_shape\tPASS\tall rows have header column count\n'
	else
		printf 'coverage_tsv_shape\tFAIL\tone or more rows have a different column count\n'
		failures=$((failures + 1))
	fi

	local duplicate_ids
	duplicate_ids="$(awk -F '\t' 'NR > 1 { count[$1]++ } END { for (id in count) if (count[id] > 1) print id ":" count[id] }' coverage/ZL_COVERAGE_INDEX.tsv | sort)"
	if [[ -z "$duplicate_ids" ]]; then
		printf 'coverage_duplicate_ids\tPASS\tno duplicate ids\n'
	else
		printf 'coverage_duplicate_ids\tFAIL\t%s\n' "$duplicate_ids"
		failures=$((failures + 1))
	fi

	local unassigned_count
	unassigned_count="$(source_paths | awk -F '\t' 'NR > 1 && $2 != "covered" { count++ } END { print count + 0 }')"
	if [[ "$unassigned_count" == "0" ]]; then
		printf 'source_paths_covered\tPASS\t0 unassigned source paths\n'
	else
		printf 'source_paths_covered\tFAIL\t%s unassigned source paths\n' "$unassigned_count"
		failures=$((failures + 1))
	fi

	local actionable_count
	actionable_count="$(dependency_gates actionable | awk 'END { print NR + 0 }')"
	if [[ "$actionable_count" == "0" ]]; then
		printf 'actionable_gates\tPASS\t0 actionable gates\n'
	else
		printf 'actionable_gates\tFAIL\t%s actionable gates\n' "$actionable_count"
		failures=$((failures + 1))
	fi

		local patch_summary
		patch_summary="$(patch_groups | awk -F '\t' 'BEGIN { static = 0; dynamic = 0; base = 0 } $3 == "static" { static++ } $3 == "dynamic" { dynamic++ } $3 == "base" { base++ } END { printf "static=%d dynamic=%d base=%d total=%d", static, dynamic, base, static + dynamic + base }')"
		printf 'patch_inventory\tPASS\t%s\n' "$patch_summary"

		local unrepresented_patch_gaps
		unrepresented_patch_gaps="$(patch_row_gaps | awk -F '\t' 'NR > 1 && $5 == "unrepresented" { count++ } END { print count + 0 }')"
		if [[ "$unrepresented_patch_gaps" == "0" ]]; then
			printf 'patch_row_gaps\tPASS\t0 unrepresented exact-name gaps\n'
		else
			printf 'patch_row_gaps\tFAIL\t%s unrepresented exact-name gaps\n' "$unrepresented_patch_gaps"
			failures=$((failures + 1))
		fi

	local bridge_summary_line
	bridge_summary_line="$(bridge_summary | awk -F '\t' '$1 == "candidate_retire" { print "candidate_retire=" $4 " " $5 }')"
	printf 'bridge_retirement_candidates\tPASS\t%s\n' "$bridge_summary_line"

	local bridge_next_local_action
	bridge_next_local_action="$(bridge_next | awk -F '\t' 'NR > 1 && $7 == "local-action" { count++ } END { print count + 0 }')"
	if [[ "$bridge_next_local_action" == "0" ]]; then
		printf 'bridge_next_local_action\tPASS\t0 bridge families with local-action backlog\n'
	else
		printf 'bridge_next_local_action\tFAIL\t%s bridge families with local-action backlog\n' "$bridge_next_local_action"
		failures=$((failures + 1))
	fi

	local report_patch_counts
	if report_patch_counts="$(coverage_report_patch_counts)"; then
		printf 'coverage_report_patch_counts\tPASS\t%s\n' "$report_patch_counts"
	else
		printf 'coverage_report_patch_counts\tFAIL\t%s\n' "$report_patch_counts"
		failures=$((failures + 1))
	fi

	local report_patch_gap_counts
	if report_patch_gap_counts="$(coverage_report_patch_gap_counts)"; then
		printf 'coverage_report_patch_gap_counts\tPASS\t%s\n' "$report_patch_gap_counts"
	else
		printf 'coverage_report_patch_gap_counts\tFAIL\t%s\n' "$report_patch_gap_counts"
		failures=$((failures + 1))
	fi

	local report_dependency_gate_counts
	if report_dependency_gate_counts="$(coverage_report_dependency_gate_counts)"; then
		printf 'coverage_report_dependency_gate_counts\tPASS\t%s\n' "$report_dependency_gate_counts"
	else
		printf 'coverage_report_dependency_gate_counts\tFAIL\t%s\n' "$report_dependency_gate_counts"
		failures=$((failures + 1))
	fi

	local report_bridge_counts
	if report_bridge_counts="$(coverage_report_bridge_counts)"; then
		printf 'coverage_report_bridge_counts\tPASS\t%s\n' "$report_bridge_counts"
	else
		printf 'coverage_report_bridge_counts\tFAIL\t%s\n' "$report_bridge_counts"
		failures=$((failures + 1))
	fi

	return "$failures"
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

if [[ "$PATCH_ROW_GAPS" == "1" ]]; then
	patch_row_gaps
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

if [[ "$BRIDGE_PRESSURE" == "1" ]]; then
	bridge_pressure
	exit 0
fi

if [[ "$BRIDGE_NEXT" == "1" ]]; then
	bridge_next
	exit 0
fi

if [[ "$DEPENDENCY_GATE_SUMMARY" == "1" ]]; then
	printf 'gate_kind\tcount\n'
	dependency_gates summary
	exit 0
fi

if [[ "$ACTIONABLE_GATES" == "1" ]]; then
	printf 'id\trow_type\towner_cluster\tevidence_state\tport_delta_state\tgate_kind\tprimary_scenario\topen_questions\n'
	dependency_gates actionable
	exit 0
fi

if [[ "$DEPENDENCY_GATES" == "1" ]]; then
	printf 'id\trow_type\towner_cluster\tevidence_state\tport_delta_state\tgate_kind\tprimary_scenario\topen_questions\n'
	dependency_gates all
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

if [[ "$CONSISTENCY_CHECKS" == "1" ]]; then
	consistency_checks
	exit $?
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
