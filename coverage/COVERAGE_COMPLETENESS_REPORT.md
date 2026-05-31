# Zombieland 1.6 Coverage Completeness Report

## 1. Inventory method

The original research pass inspected the current GitHub repository through the repository connector and web-visible tree pages because that session could not DNS-resolve `github.com` for a local `git clone`. The inventory used the current `master` compare against baseline `a0de6c309ab789a488a6bc106d281b5d0ca79020`, GitHub file/search results, fetched current files (`Source/ZombieLand.csproj`, `About/About.xml`, `LoadFolders.xml`, `TEST_COVERAGE.md`, `TEST_PATCH_AUDIT.md`), the GitHub Defs directory listing, and the uploaded session transcript as supporting history. The current local reconciliation on 2026-05-31 then validated source and patch inventory from the checkout itself.

The ledger deliberately separates behavior ownership from evidence production. High-level feature rows own player-facing behavior. Patch rows own hook intent and target disposition. Scenario rows own end-to-end retest passes. Bridge rows own test infrastructure. Def rows own XML family resolution. Source backstop rows prevent discovered files from falling through the cracks.

## 2. Row counts

### By row_type

- bridge_contract: 18
- def: 22
- feature: 21
- integration: 9
- negative_space: 1
- patch: 107
- scenario: 18
- supporting_infra: 15

### By owner_cluster

- bridge_tools: 18
- contamination: 55
- core_zombie: 27
- defs_assets: 12
- hostility: 20
- negative_space: 1
- optional_integrations: 10
- pathing: 7
- quests: 6
- rendering: 13
- save_load: 8
- settings: 10
- startup: 11
- supporting_infra: 6
- ui: 7

## 3. Source files covered vs unassigned

Original generated source inventory size: 136 files. Local reconciliation on 2026-05-31 with `find Source -type f -name '*.cs' -not -path '*/obj/*'` found 153 non-obj source files in the current checkout.

Backstop source rows created: 14. These are not meant to inflate behavior coverage; they are guardrails for files that were discovered but not otherwise named by a feature, patch, integration, or bridge row.

The 2026-05-31 source-backstop cleanup resolved the helper ownership for `BrainzThought`, `ColonyEvaluation`, `ColorData`, `CountingCache`, `Debouncer`, `DisposableMaterial`, `DistanceComparer`, `Gas`, `Rubble`, `VictimHead`, and `ZombieRemover`. `Constants`, `Dialog_ErrorMessage`, and `Tools` remain as deliberate source-backstop audit rows until they are split or assigned without hiding broad helper behavior inside one row.

The local reconciliation removed the original "possible undiscovered source" uncertainty. A follow-up exact-path reconciliation assigned every non-obj `Source/*.cs` file to `ZL_COVERAGE_INDEX.tsv` either through a feature row, bridge row, integration row, or supporting-infra/source-backstop row. The exact source-path gap count is now 0.

## 4. Harmony patch count and coverage state

Current local `scripts/coverage-inventory.sh --patch-groups` output on 2026-05-31 finds 257 static patch rows, 40 dynamic patch rows, and 4 class-level base rows. `TEST_PATCH_AUDIT.md` mentions all 301 patch classes, but `ZL_COVERAGE_INDEX.tsv` still keeps many of them under feature/scenario ownership rather than explicit `PATCH.*` rows. This should be treated as a row-granularity decision: add explicit patch rows only when target-level disposition helps planning more than the existing audit table.

Patch evidence-state distribution among explicit patch rows:

- contract_runtime: 99
- contract_runtime_inactive: 1
- source_only: 7

## 5. Def-family count and coverage state

Def-family rows generated: 22. They cover the current GitHub Defs directory listing: `Defs/Zombie_BodyParts.xml; Defs/Zombie_Damages.xml; Defs/Zombie_Effecter.xml; Defs/Zombie_Faction.xml; Defs/Zombie_Hediffs.xml; Defs/Zombie_Jobs.xml; Defs/Zombie_Kinds.xml; Defs/Zombie_Letter.xml; Defs/Zombie_LifeStages.xml; Defs/Zombie_Maneuvers.xml; Defs/Zombie_MentalState.xml; Defs/Zombie_Needs.xml; Defs/Zombie_Quests.xml; Defs/Zombie_Race.xml; Defs/Zombie_Recipes.xml; Defs/Zombie_Sounds.xml; Defs/Zombie_ThingCategories.xml; Defs/Zombie_Things.xml; Defs/Zombie_ThinkTree.xml; Defs/Zombie_Thoughts.xml; Defs/Zombie_ToolCapacity.xml; Defs/Zombie_Workgivers.xml`.

The def rows distinguish XML/asset reference resolution from runtime/save-load/visual behavior ownership. A live `zombieland/startup_support_state` def-resolution pass now resolves all generated def-family rows for the active Core-only configuration: 154 Zombieland-owned defs across 23 DefDatabase types, zero class/config/database errors, 40 initialized ThingDef graphics, two PawnKindDef body graphics, and 38 resolved SoundDefs. Quest, infection, contamination jobs/needs/hediffs, race/kinds/think-tree, things/effecters/sounds, faction and letters remain mapped to their owner scenario rows for behavior coverage.

## 6. Bridge tool count and classification

Bridge rows generated: 18. They classify the current 20 `Source/BridgeTools/ZombielandBridgeTools.*.cs` files as generic primitives, scenario builders, narrow contracts, optional integrations, or historical/pending review. The index maps each bridge file family back to the feature/scenario rows it evidences rather than letting bridge tools own behavior directly. `ZombielandBridgeTools.SemanticWait.cs` is assigned to `BRIDGE.Common` as a generic bounded tick-until-condition primitive.

## 7. Highest-risk remaining gaps

1. Complete patch expansion: the explicit ledger covers key/current documented patches, and local inventory now confirms 257 static/40 dynamic/4 base rows; decide deliberately which remaining feature-owned patches deserve explicit `PATCH.*` rows.
2. Patch row granularity: `TEST_PATCH_AUDIT.md` mentions all 301 patch classes, but not every class is represented as its own `PATCH.*` row in `ZL_COVERAGE_INDEX.tsv`; add explicit rows only where they improve planning.
3. Special zombie broader integration polish: controlled action/death behavior and the visual save-load baseline are covered; remaining work is only broader mixed-map combat or optional/DLC variants when they become named rows.
4. Optional mod compatibility: CE, AlienRace, Vehicles, Dubs, SoS2, RimConnect, CameraPlus and customization systems need active-mod runtime evidence.
5. Visual polish: contamination clean negatives, defense-room visual/audio, and special visual save-load are covered; remaining UI/visual work should be changed art/effect variants, debug overlays, or optional-mod/DLC variants only.
6. DLC-gated surfaces: Biotech mech repair and child/killed-child thought paths remain dependency-gated.
7. Core branch tails: mixed horde save-load and dense 80-zombie normal-speed smoke are covered; remaining C-row work is narrow branch co-location only, such as direct wall-smash/destruction or eating aftermath if it becomes useful evidence.

## 8. Recommended next passes in order

1. `SOURCE_AUDIT` + `BRIDGE_TOOL_REVIEW`: use the local `coverage-inventory.sh --patch-groups --dynamic-patches` outputs to add explicit patch rows only where they improve target-level planning.
2. `RW16_TARGET_AUDIT` + `RW14_RW16_SEMANTIC_DIFF`: focus only on patch rows still marked `needs_decompiler_verification`, `source_only`, `partial`, `dependency/unavailable`, or `obsolete/disposed`.
3. `VISUAL_UI`: do not rerun baseline visuals; run only changed art/effect variants, explicitly supported debug overlays, or optional-mod/DLC visual variants.
4. `SCENARIO_RUNTIME` + `SAVE_LOAD`: extend core horde only for named branch tails, not as another baseline dense-horde rerun.
5. `OPTIONAL_MOD_COMPAT`: one active-mod pass per named optional integration.
6. `NEGATIVE_SPACE_1_6`: re-run discovery after every RimWorld 1.6 or dependency update.

## 9. Confidence level and limitations

Confidence: partial-authoritative scaffold. It is authoritative for the rows it explicitly maps and for the current docs, local reconciliation, live bridge evidence, and decompiler evidence it records, but it is not a final complete ledger because explicit patch-row expansion is intentionally limited and all static Harmony rows are not represented one-by-one in `ZL_COVERAGE_INDEX.tsv`.

RimWorld 1.6 decompiler evidence in this report is a mix of current repo docs, transcript history, and current-session live decompiler checks. Rows without current-doc member IDs are still marked through their owning row notes or left for later `RW16_TARGET_AUDIT`; def-family resolution no longer depends on decompiler evidence because it is proven through live DefDatabase/asset initialization.

## 10. Consistency checks

1. Every discovered `Source/*.cs` file is referenced by at least one coverage row or represented by a source-backstop row: PASS.
2. Every known `[HarmonyPatch]` class from current local inventory is represented by an explicit patch row or feature-row patch listing: PARTIAL PASS; `TEST_PATCH_AUDIT.md` mentions all 301 patch classes, while this coverage index deliberately expands only the rows where row-level target disposition helps planning.
3. Every discovered BridgeTools file is represented by a `bridge_contract` row: PASS for discovered bridge files.
4. Every major XML def file/family from the current Defs tree is represented by a `def` row: PASS.
5. No duplicate `id` values: PASS.
6. No empty required fields and no embedded tabs in fields: PASS.
7. High-level feature rows have duplicate guard text: PASS.
8. Known porting incidents are represented: PASS for `PawnCanOpen`/`FreePassage` recursion and C5 focused/combined contamination evidence; C6 quest blocker and replacement are represented from current docs.
9. Explicit patch-row granularity is deliberately limited rather than treated as missing source coverage: PASS; see `UNASSIGNED_SURFACES.tsv`.

## 11. Generated files

- `ZL_COVERAGE_INDEX.tsv`
- `TEST_EVIDENCE.tsv`
- `UNASSIGNED_SURFACES.tsv`
- `COVERAGE_COMPLETENESS_REPORT.md`
- `ZL_COVERAGE_RESEARCH_FILES.zip`
