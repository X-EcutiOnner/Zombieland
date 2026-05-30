#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BASELINE="a0de6c309ab789a488a6bc106d281b5d0ca79020"
DETAILS=0
PATCH_GROUPS=0
DYNAMIC_PATCHES=0
STATIC_SUMMARY=0

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
		--help|-h)
			printf 'usage: %s [--details] [--patch-groups] [--dynamic-patches] [--static-summary] [baseline-commit]\n' "$0"
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

section "Likely Uncovered Keywords"
git log --format='%s' "$BASELINE"..HEAD > /tmp/zl-covered-subjects.$$
trap 'rm -f /tmp/zl-covered-subjects.$$' EXIT
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
	if rg -qi --fixed-strings "$keyword" /tmp/zl-covered-subjects.$$; then
		printf 'covered-subject: %s\n' "$keyword"
	else
		printf 'missing-subject: %s\n' "$keyword"
	fi
done
