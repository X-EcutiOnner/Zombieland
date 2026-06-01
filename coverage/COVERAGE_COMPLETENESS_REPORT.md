# Zombieland 1.6 Coverage Completeness Report

## 1. Inventory method

The original research pass inspected the current GitHub repository through the repository connector and web-visible tree pages because that session could not DNS-resolve `github.com` for a local `git clone`. The inventory used the current `master` compare against baseline `a0de6c309ab789a488a6bc106d281b5d0ca79020`, GitHub file/search results, fetched current files (`Source/ZombieLand.csproj`, `About/About.xml`, `LoadFolders.xml`, `TEST_COVERAGE.md`, `TEST_PATCH_AUDIT.md`), the GitHub Defs directory listing, and the uploaded session transcript as supporting history. The current local reconciliation on 2026-05-31 then validated source and patch inventory from the checkout itself.

The ledger deliberately separates behavior ownership from evidence production. High-level feature rows own player-facing behavior. Patch rows own hook intent and target disposition. Scenario rows own end-to-end retest passes. Bridge rows own test infrastructure. Def rows own XML family resolution. Source backstop rows prevent discovered files from falling through the cracks.

Use `scripts/coverage-inventory.sh --dependency-gates` as the compact current-state queue for rows that are still `partial`, `partial_runtime`, `dependency/unavailable`, or `removed_vanilla_target`. This is deliberately a triage view, not a claim that every listed row needs immediate live testing; its `gate_kind` column separates standing governance, parent branch markers, scenario rollups, unavailable DLC/content, unavailable optional mods, and removed-target documentation.

## 2. Row counts

### By row_type

- bridge_contract: 18
- def: 22
- feature: 21
- integration: 9
- negative_space: 1
- patch: 292
- scenario: 18
- supporting_infra: 24

### By owner_cluster

- bridge_tools: 18
- buildings_items: 12
- combat: 9
- contamination: 102
- core_zombie: 49
- defs_assets: 12
- hostility: 31
- infection: 15
- negative_space: 1
- optional_integrations: 19
- pathing: 19
- quests: 14
- rendering: 24
- save_load: 13
- settings: 15
- social: 11
- startup: 17
- supporting_infra: 4
- ui: 20

## 3. Source files covered vs unassigned

Original generated source inventory size: 136 files. Local reconciliation on 2026-05-31 with `find Source -type f -name '*.cs' -not -path '*/obj/*'` found 153 non-obj source files in the current checkout.

Original backstop source rows created: 14. The broad `Constants` and `Tools` rows were split into named source sub-surface rows on 2026-05-31, so the current index has 23 `SRC.*` rows. These are not meant to inflate behavior coverage; they are guardrails for files or helper sub-surfaces that were discovered but not otherwise named by a feature, patch, integration, or bridge row.

The 2026-05-31 source-backstop cleanup resolved all 23 current `SRC.*` rows. `BrainzThought`, `ColonyEvaluation`, `ColorData`, `CountingCache`, `Debouncer`, `Dialog_ErrorMessage`, `DisposableMaterial`, `DistanceComparer`, `Gas`, `Rubble`, `VictimHead`, `ZombieRemover`, all `Constants.*` rows, and all `Tools.*` rows now point at existing feature/scenario owners or, for `SRC.Tools.HarmonyIL`, the patch-inventory owner. The remaining uncertainty is patch-row granularity, not unassigned source ownership.

The local reconciliation removed the original "possible undiscovered source" uncertainty. A follow-up exact-path reconciliation assigned every non-obj `Source/*.cs` file to `ZL_COVERAGE_INDEX.tsv` either through a feature row, bridge row, integration row, or supporting-infra/source-backstop row. The exact source-path gap count is now 0.

## 4. Harmony patch count and coverage state

Current local `scripts/coverage-inventory.sh --patch-groups` output on 2026-05-31 finds 257 static patch rows, 40 dynamic patch rows, and 4 class-level base rows. `TEST_PATCH_AUDIT.md` mentions all 301 patch classes, but `ZL_COVERAGE_INDEX.tsv` still keeps many of them under feature/scenario ownership rather than explicit `PATCH.*` rows. This should be treated as a row-granularity decision: add explicit patch rows only when target-level disposition helps planning more than the existing audit table. The latest patch-governance refresh reconciled `TEST_PATCH_AUDIT.md` to the regenerated inventory by updating current dynamic source anchors and the 257-row static slice count; the earlier cleanup added 14 explicit contamination patch rows for high-value Tools Harmony/IL consumers that already had target/semantic/runtime or dependency evidence in `TEST_PATCH_AUDIT.md`. The current row-granularity pass added 159 explicit runtime-backed rows: five for `ZombieDamageFlasher`/`ZombieLeaner`, four for `ZombieAreaManager`/`Dialog_ManageAreas`, eight for the startup/UI/tick/infestation/Pawn.Tick hooks, twelve for the avoidance/doors/workgiver pathing slice, nineteen for the zombie lifecycle/combat/incident interaction slice, nineteen for the damage/infection/hostility/fire/death slice, nineteen for the social/selection/corpse-filter slice, fourteen for the finalization/load-game/settings/menu/fog/ideo slice, fourteen for the chainsaw/equipment/projectile/job-gate/animal-response slice, one follow-up for `PawnRenderer_DrawEquipment_Patch`, four for the render/apparel/stats slice (`PawnRenderer_RenderPawnAt`, `Graphic_Multi_Init`, `Verb_TryStartCastOn`, and `StatExtension_GetStatValue`), five for the hostility targeting/cache slice (`AttackTargetFinder_GetShootingTargetScore`, `GenHostility_IsActiveThreat`, and the three `AttackTargetsCache` hooks), seven for the C1 stack/container slice (`GenRecipe` product transfer, `ThingOwner` transfer, stack absorb/split, comp split, ingestion, and carry ticks), six for the C2 build/replace slice (minify, install/reinstall blueprints, blueprint replacement, smooth/reverted walls), one for nutrient-paste feed transfer, seven for the C0/C3/C4/C5 contamination primitive slice (need gating, world reset, cell entry, fire decay, tending, rest comfort, and corpse inner-pawn synchronization), seven for the C3/C4 world-product/environment slice (leavings, mineable destroy/mining context, wild plant spawn, plant harvest, ambrosia sprout, and snow/sand clearing), two for the dangerous-area warning/message UI slice (`Messages.MessagesDoGUI` ordering and `Message.Draw` offset), two for startup lifecycle services (`TimeControlService`, `ClearMapsService`), two for direct `GenHostility.HostileTo` overloads, and one for generated-map contamination seeding (`MapGenerator_GenerateContentsIntoMap_Patch`), backed by current RimWorld 1.6 decompiler checks plus existing scenario runtime evidence. It also added eight explicit optional-integration patch rows: `Map_MapUpdate_Patch` for the SoS floating-zombie draw hook plus the seven optional dynamic patch targets for Combat Extended, RimConnect settings UI, and Save Our Ship 2. The active Core-only runtime proves absent optional gates are inert and log-clean; the 2026-06-01 active CE pass then resolved all four CE targets against current CE 16.7.2.0 and proved tanky/electrifier damage-stack behavior, while direct surgical-cut, projectile-noise, spitter after-armor, turret ammo, and log-clean CE compatibility remain pending. A follow-up negative-space slice added two explicit `obsolete/disposed` rows for disabled `Prepare() => false` patches, `Thing_MakeThing_Patch` and the old `PathFinder_FindPath_Patch`, so future passes test their active replacements rather than retesting dead hooks. The pollution dependency slice added explicit source-only rows for `JobDriver_ClearPollution_ClearPollutionAt_Patch` and `GridUtility_Unpollute_Patch`; decompiler confirms the RimWorld 1.6 targets still exist, while the local Core-only install keeps their runtime behavior dependency-gated. The map-generation contamination slice used only generic lifecycle/read tools: a built-in debug colony produced `ZL_MapGeneration_Contamination_01.rws`, reloaded cleanly, and preserved generated-map contamination at `(150,100)`. The 2026-06-01 chainsaw draw-equipment follow-up used existing `zombieland/chainsaw_slaughter_zombie` plus generic `rimworld/screenshot_cell_rect`, so it did not add another one-off bridge endpoint.

The exact generated-name reconciliation now has a 19-row tail. It is intentionally not a 19-row behavior backlog: `AreaManager_Patches.*`, `Dialog_ManageAreas_Patches.*`, `DamageFlasher_*`, `Effecter_Trigger_Patch`, `IncidentWorker_Patches`, the two `InfestationCellFinder` child names, `PawnDownedWiggler_WigglerTick_Patch`, the two `Pawn_GeneTracker_AddGene_*` overloads, and `Pawn_MeleeVerbs_ChooseMeleeVerb_Patch` are owned by differently named explicit `PATCH.*` rows; `Assets.LoadAssetBundle` is covered by the startup asset-bundle row; `SelectionDrawer_DrawSelectionOverlays_Patch`, `MapInterface_MapInterfaceUpdate_Patch`, `MapInterface_MapInterfaceOnGUI_AfterMainTabs_Patch`, and `EditWindow_DebugInspector_CurrentDebugString_Patch` are source-audited debug/inspector surfaces and should not get runtime rows unless those debug features become supported player-facing scenarios.

The 2026-05-31 dependency-boundary pass strengthened four C3 `source_only` rows with fresh RimWorld 1.6 decompiler evidence: `Building_SubcoreScanner.Tick`, `Building_GeneExtractor.Finish`, `JobDriver_DisassembleMech.MakeNewToils`, and `TunnelJellySpawner.Spawn`. They remain `dependency/unavailable` because the active runtime load order is Core-only (`Harmony`, `Core`, `Zombieland`, `RimBridgeServer`), so no Biotech/content product-transfer runtime claim is made.

Patch evidence-state distribution among explicit patch rows:

- contract_runtime: 273
- contract_runtime_absent: 6
- contract_runtime_inactive: 1
- save_load: 1
- source_only: 11

Patch port-delta distribution among explicit patch rows after the remaining explicit `needs_audit` decompiler pass:

- resolved: 271
- dependency/unavailable: 15
- partial_runtime: 3
- removed_vanilla_target: 1
- obsolete/disposed: 2

## 5. Def-family count and coverage state

Def-family rows generated: 22. They cover the current GitHub Defs directory listing: `Defs/Zombie_BodyParts.xml; Defs/Zombie_Damages.xml; Defs/Zombie_Effecter.xml; Defs/Zombie_Faction.xml; Defs/Zombie_Hediffs.xml; Defs/Zombie_Jobs.xml; Defs/Zombie_Kinds.xml; Defs/Zombie_Letter.xml; Defs/Zombie_LifeStages.xml; Defs/Zombie_Maneuvers.xml; Defs/Zombie_MentalState.xml; Defs/Zombie_Needs.xml; Defs/Zombie_Quests.xml; Defs/Zombie_Race.xml; Defs/Zombie_Recipes.xml; Defs/Zombie_Sounds.xml; Defs/Zombie_ThingCategories.xml; Defs/Zombie_Things.xml; Defs/Zombie_ThinkTree.xml; Defs/Zombie_Thoughts.xml; Defs/Zombie_ToolCapacity.xml; Defs/Zombie_Workgivers.xml`.

The def rows distinguish XML/asset reference resolution from runtime/save-load/visual behavior ownership. A live `zombieland/startup_support_state` def-resolution pass now resolves all generated def-family rows for the active Core-only configuration: 154 Zombieland-owned defs across 23 DefDatabase types, zero class/config/database errors, 40 initialized ThingDef graphics, two PawnKindDef body graphics, and 38 resolved SoundDefs. Quest, infection, contamination jobs/needs/hediffs, race/kinds/think-tree, things/effecters/sounds, faction and letters remain mapped to their owner scenario rows for behavior coverage.

## 6. Bridge tool count and classification

Bridge rows generated: 18. They classify the current 20 `Source/BridgeTools/ZombielandBridgeTools.*.cs` files plus `ZombieRuntimeActions` as generic primitives, scenario builders, narrow contracts, optional integrations, or retained evidence tools. A 2026-05-31 source attribute scan found 176 public Zombieland bridge tools across 19 Tool-bearing files, with shared helpers in `ZombielandBridgeTools.Common.cs` and `ZombieRuntimeActions.cs`. After the 2026-06-01 helper-retirement pass, the current source has 175 public Zombieland bridge tools.

The 2026-06-01 bridge-governance pass added `scripts/coverage-inventory.sh --bridge-tools`, which turns the previous ad hoc Tool-attribute scan into a reusable TSV inventory. Current output confirms 175 Tool attributes across 19 Tool-bearing files: 24 `generic-primitive`, 8 `scenario-fixture`, 140 `narrow-contract`, 0 `evidence-helper`, and 3 `optional-integration` tools. `zombieland/complete_frame_by_id` is classified as a generic contamination primitive because it completes arbitrary current-map frames by id/ThingID/label with a generated worker and returns structured frame/worker handoff state. The former `zombieland/setup_thumper_visual_wave_observation` endpoint was retired because thumper screenshot/setup observation evidence is already preserved by defense-room fixtures plus generic screenshot/audio primitives. This does not make the umbrella bridge row complete; it gives future sessions a cheap gate before adding another bespoke contract.

All 17 individual `BRIDGE.*` family rows are now classified. `BRIDGE.Common` is the resolved generic primitive row and includes `zombieland/wait_for_semantic_change`, which should absorb future bounded tick-until-condition needs before adding bespoke tick-loop contracts. The umbrella `J.BRIDGE.TOOLS` row remains `partial` by design because consolidation is a standing maintenance rule, not a one-time completion claim.

## 7. Highest-risk remaining gaps

1. Patch row granularity: `TEST_PATCH_AUDIT.md` mentions all 301 patch classes, but not every class is represented as its own `PATCH.*` row in `ZL_COVERAGE_INDEX.tsv`; the highest-value hooks are now explicit rows, and the remaining 19 exact generated-name misses are classified as aliases, base/context, or debug/source-only surfaces rather than missing behavior.
2. Explicit patch tail: no explicit `PATCH.*` rows remain in `needs_audit`; remaining non-runtime patch rows are dependency-gated, obsolete/disposed, inactive optional-mod rows, or documented partial branches.
3. Settings UI end-to-end polish: the baseline is covered. Default restart persistence, in-game save-load, menu entry, modal backing state, save-then-uninstall entry, mouse-driven new-colony wizard entry, focused mouse-driven edits including apparel modal Select All/Deselect All plus the main Threat scale slider, source/decompiler-backed text/numeric field disposition, and two downstream visible UI-edited checks have evidence: `Show zombie statistics` driving the live alert surface and `Zombies health bar` driving the hovered zombie overlay. A 2026-06-01 source/decompiler reread confirmed remaining visible controls share the same radio/checkbox/slider/numeric/button helper paths. Future settings work should be named downstream UI-edit/effect pairs where the player-facing risk justifies another live pass.
4. Special zombie broader integration polish: controlled action/death behavior and the visual save-load baseline are covered; remaining work is only broader mixed-map combat or optional/DLC variants when they become named rows.
5. Optional mod compatibility: Dubs and Camera+ are covered for the current local installs. CE is now installed from upstream GitHub, built locally, enabled before Zombieland, and partially runtime-covered: all four CE patch targets resolve and tanky/electrifier damage-stack smokes pass under CE. CE remains high priority because direct surgical-cut ArmorReroute, projectile noise, spitter after-armor, turret ammo, and a log-clean active pass are not complete. AlienRace, Vehicles, SoS2, RimConnect and customization systems still need active-mod runtime evidence, but a 2026-06-01 local app/workshop About.xml scan found those dependencies unavailable on this machine. Dubs now has installed-source/decompiler classification plus a resolved active startup/map/zombie smoke with clean logs; direct Dubs profiler UI activation is tracked only as a low-priority external UI item if it becomes a named player-facing requirement. Camera+ now has installed-source/decompiler classification plus active marker/delegate runtime evidence with a furthest-zoom screenshot and clean logs.
6. Visual polish: contamination clean negatives, defense-room visual/audio, and special visual save-load are covered; remaining UI/visual work should be changed art/effect variants, debug overlays, or optional-mod/DLC variants only.
7. DLC-gated surfaces: pollution clearing, Biotech mech repair/GeneAssembler, and child/killed-child thought paths remain dependency-gated; these are now recorded as `dependency/unavailable` instead of generic blockers. A 2026-05-31 local inspection confirmed the official RimWorld `Data` folder contains only `Core` and `rimworld/list_mods includeInactive=true` exposes no official DLC packages, so these rows require a different install/configuration for runtime evidence. The explicit adult death-thought patch row is `partial_runtime` because the child exception is not active in the current Core-only surface.
8. Core branch tails: mixed horde save-load, dense 80-zombie normal-speed smoke, focused corpse/downed-pawn eating, direct tanky wall destruction, and the direct zombie-hearer clamor branch are covered. No known C baseline branch tail remains in the core active-mod configuration; future work should be named co-location, endurance, optional-mod, DLC, or regression variants.

Current `scripts/coverage-inventory.sh --dependency-gates` distribution after the 2026-06-01 CE pass: 33 rows total, split into 3 `standing-governance`, 2 `parent-branch-marker`, 2 `scenario-rollup`, 12 `dlc-content-unavailable`, 13 `external-mod-unavailable`, and 1 `removed-target-doc`. CE rows are no longer absent-runtime gates; the remaining CE work is a partial active compatibility track that still appears in the external-mod queue until the named behavior/log-clean scenarios close. No row is currently classified as an untyped actionable gate in the active local setup.

## 8. Recommended next passes in order

1. `SOURCE_AUDIT`: use the local `coverage-inventory.sh --patch-groups --dynamic-patches` outputs to add explicit patch rows only where they improve target-level planning; do not add rows merely to eliminate exact-name alias misses.
2. `RW16_TARGET_AUDIT` + `RW14_RW16_SEMANTIC_DIFF`: focus only on patch rows still marked `needs_decompiler_verification`, `source_only`, `partial`, `dependency/unavailable`, or `obsolete/disposed`.
   Start with `scripts/coverage-inventory.sh --dependency-gates` and inspect `gate_kind` so dependency skips, parent branch markers, standing governance, scenario rollups, and intentional negative-space rows are separated from actionable source/decompiler work.
3. `VISUAL_UI`: do not rerun baseline visuals; run only changed art/effect variants, explicitly supported debug overlays, or optional-mod/DLC visual variants.
4. `SCENARIO_RUNTIME` + `SAVE_LOAD`: extend core horde only for named branch tails, not as another baseline dense-horde rerun.
5. `OPTIONAL_MOD_COMPAT`: one active-mod pass per named optional integration.
6. `BRIDGE_TOOL_REVIEW`: run `scripts/coverage-inventory.sh --bridge-tools` before adding new bridge contracts; keep the 17 classified bridge families stable; add future reusable setup/read/wait tools to `BRIDGE.Common` or a scenario fixture first, and retire one-off contracts only when their evidence is preserved elsewhere.
7. `NEGATIVE_SPACE_1_6`: re-run discovery after every RimWorld 1.6 or dependency update.

## 9. Confidence level and limitations

Confidence: partial-authoritative scaffold. It is authoritative for the rows it explicitly maps and for the current docs, local reconciliation, live bridge evidence, and decompiler evidence it records, but it is not a final complete ledger because explicit patch-row expansion is intentionally limited and all static Harmony rows are not represented one-by-one in `ZL_COVERAGE_INDEX.tsv`.

RimWorld 1.6 decompiler evidence in this report is a mix of current repo docs, transcript history, and current-session live decompiler checks. Rows without current-doc member IDs are still marked through their owning row notes or left for later `RW16_TARGET_AUDIT`; def-family resolution no longer depends on decompiler evidence because it is proven through live DefDatabase/asset initialization.

## 10. Consistency checks

1. Every discovered `Source/*.cs` file is referenced by at least one coverage row or represented by a source-backstop row: PASS.
2. Every known `[HarmonyPatch]` class from current local inventory is represented by an explicit patch row or feature-row patch listing: PARTIAL PASS; `TEST_PATCH_AUDIT.md` mentions all 301 patch classes, while this coverage index deliberately expands only the rows where row-level target disposition helps planning.
3. Every discovered BridgeTools file is represented by a `bridge_contract` row: PASS for discovered bridge files; all individual `BRIDGE.*` family rows are classified, while the umbrella bridge-governance row remains partial for future consolidation.
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
