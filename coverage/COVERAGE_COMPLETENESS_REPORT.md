# Zombieland 1.6 Coverage Completeness Report

## 1. Inventory method

I inspected the current GitHub repository through the repository connector and web-visible tree pages because the sandbox could not DNS-resolve `github.com` for a local `git clone`. The inventory uses the current `master` compare against baseline `a0de6c309ab789a488a6bc106d281b5d0ca79020`, GitHub file/search results, fetched current files (`Source/ZombieLand.csproj`, `About/About.xml`, `LoadFolders.xml`, `TEST_COVERAGE.md`, `TEST_PATCH_AUDIT.md`), the GitHub Defs directory listing, and the uploaded session transcript as supporting history. The compare reported `master` 188 commits ahead of the baseline. The project file shows a RimWorld 1.6 build target via `Krafs.Rimworld.Ref` 1.6.4633, output to `1.6/Assemblies`, and RimBridge annotations.

The ledger deliberately separates behavior ownership from evidence production. High-level feature rows own player-facing behavior. Patch rows own hook intent and target disposition. Scenario rows own end-to-end retest passes. Bridge rows own test infrastructure. Def rows own XML family resolution. Source backstop rows prevent discovered files from falling through the cracks.

## 2. Row counts

### By row_type

- bridge_contract: 18
- def: 22
- feature: 19
- integration: 9
- negative_space: 1
- patch: 107
- scenario: 16
- supporting_infra: 15

### By owner_cluster

- bridge_tools: 18
- contamination: 54
- core_zombie: 27
- defs_assets: 12
- hostility: 20
- negative_space: 1
- optional_integrations: 10
- pathing: 7
- quests: 5
- rendering: 9
- save_load: 5
- settings: 10
- startup: 11
- supporting_infra: 12
- ui: 6

## 3. Source files covered vs unassigned

Discovered Source inventory size: 136 files. Referenced by at least one coverage row: 136. Missing from generated rows: 0.

Backstop source rows created: 14. These are not meant to inflate behavior coverage; they are guardrails for files that were discovered but not otherwise named by a feature, patch, integration, or bridge row.

Unassigned source inventory limitation: the sandbox could not clone the repository and the GitHub Source directory page intermittently failed to render a complete tree, so the source inventory is a union of compare/search/docs rather than a local `git ls-files`. This is why `UNASSIGNED_SURFACES.tsv` includes a high-priority source-inventory verification item.

## 4. Harmony patch count and coverage state

Current docs report 256 static patch rows, 40 dynamic patch rows, and 4 class-level base rows. This generated ledger includes 107 explicit patch rows for high-risk and documented current audit targets, plus feature/scenario rows that list related patch families. It does not expand all 256 static rows one-by-one. That gap is explicit in `UNASSIGNED_SURFACES.tsv` and should be closed by running `scripts/coverage-inventory.sh --patch-groups` locally and appending missing `PATCH.*` rows.

Patch evidence-state distribution among explicit patch rows:

- contract_runtime: 99
- source_only: 8

## 5. Def-family count and coverage state

Def-family rows generated: 22. They cover the current GitHub Defs directory listing: `Defs/Zombie_BodyParts.xml; Defs/Zombie_Damages.xml; Defs/Zombie_Effecter.xml; Defs/Zombie_Faction.xml; Defs/Zombie_Hediffs.xml; Defs/Zombie_Jobs.xml; Defs/Zombie_Kinds.xml; Defs/Zombie_Letter.xml; Defs/Zombie_LifeStages.xml; Defs/Zombie_Maneuvers.xml; Defs/Zombie_MentalState.xml; Defs/Zombie_Needs.xml; Defs/Zombie_Quests.xml; Defs/Zombie_Race.xml; Defs/Zombie_Recipes.xml; Defs/Zombie_Sounds.xml; Defs/Zombie_ThingCategories.xml; Defs/Zombie_Things.xml; Defs/Zombie_ThinkTree.xml; Defs/Zombie_Thoughts.xml; Defs/Zombie_ToolCapacity.xml; Defs/Zombie_Workgivers.xml`.

The def rows distinguish cold-load/source-only resolution from runtime/save-load/visual coverage. Quest, infection, contamination jobs/needs/hediffs, race/kinds/think-tree, things/effecters/sounds, faction and letters are mapped to their owner scenario rows.

## 6. Bridge tool count and classification

Bridge rows generated: 18. They classify the current `Source/BridgeTools/ZombielandBridgeTools.*.cs` files as generic primitives, scenario builders, narrow contracts, optional integrations, or historical/pending review. The index maps each bridge file family back to the feature/scenario rows it evidences rather than letting bridge tools own behavior directly.

## 7. Highest-risk remaining gaps

1. Complete patch expansion: the explicit ledger covers key/current documented patches, but the reported 256 static rows need local inventory expansion.
2. Full source tree certainty: because local clone failed, one high-priority unassigned surface asks for local `git ls-files`/coverage-inventory reconciliation.
3. Core emergent horde scenario: narrow contracts are strong, but one dense save-load horde loop remains.
4. Special zombie visual/integration gauntlet: many narrow contracts pass, but a single mixed-map visual save-load pass remains.
5. Optional mod compatibility: CE, AlienRace, Vehicles, Dubs, SoS2, RimConnect, CameraPlus and customization systems need active-mod runtime evidence.
6. UI clean negatives and visual/audio polish: contamination and defense-room positive paths are covered, but clean negative comparisons and audio/visual confirmations remain.
7. DLC-gated surfaces: Biotech mech repair and child/killed-child thought paths remain dependency-gated.

## 8. Recommended next passes in order

1. `SOURCE_AUDIT` + `BRIDGE_TOOL_REVIEW`: run `scripts/coverage-inventory.sh --patch-groups --dynamic-patches` locally and append missing patch rows.
2. `RW16_TARGET_AUDIT` + `RW14_RW16_SEMANTIC_DIFF`: focus only on patch rows still marked `needs_decompiler_verification`, `source_only`, `partial`, `dependency/unavailable`, or `obsolete/disposed`.
3. `SCENARIO_RUNTIME` + `SAVE_LOAD`: build the dense `S-Core-Horde-Loop` save-load/performance smoke.
4. `VISUAL_UI`: run special zombie gauntlet visuals and UI clean negative comparisons.
5. `OPTIONAL_MOD_COMPAT`: one active-mod pass per named optional integration.
6. `NEGATIVE_SPACE_1_6`: re-run discovery after every RimWorld 1.6 or dependency update.

## 9. Confidence level and limitations

Confidence: partial-authoritative scaffold. It is authoritative for the rows it explicitly maps and for the current docs/transcript evidence it records, but it is not a final complete ledger because local cloning and direct full-tree enumeration were unavailable in this session and all static Harmony rows were not expanded one-by-one.

RimWorld 1.6 decompiler evidence in this report is taken from current repo docs and the transcript, not from a live decompiler tool in this session. Rows without current-doc member IDs are marked `needs_decompiler_verification`.

## 10. Consistency checks

1. Every discovered `Source/*.cs` file is referenced by at least one coverage row or represented by a source-backstop row: PASS.
2. Every known `[HarmonyPatch]` class from fetched docs/search is represented by an explicit patch row or feature-row patch listing: PARTIAL PASS; known key rows covered, all 256 static rows require local expansion.
3. Every discovered BridgeTools file is represented by a `bridge_contract` row: PASS for discovered bridge files.
4. Every major XML def file/family from the current Defs tree is represented by a `def` row: PASS.
5. No duplicate `id` values: PASS.
6. No empty required fields and no embedded tabs in fields: PASS.
7. High-level feature rows have duplicate guard text: PASS.
8. Known porting incidents are represented: PASS for `PawnCanOpen`/`FreePassage` recursion and C5 focused/combined contamination evidence; C6 quest blocker and replacement are represented from current docs.
9. Large uninspected class limitation is explicitly marked: PASS; see `UNASSIGNED_SURFACES.tsv`.

## 11. Generated files

- `ZL_COVERAGE_INDEX.tsv`
- `TEST_EVIDENCE.tsv`
- `UNASSIGNED_SURFACES.tsv`
- `COVERAGE_COMPLETENESS_REPORT.md`
- `ZL_COVERAGE_RESEARCH_FILES.zip`
