# Zombieland 1.6 Scenario Definitions

These are scenario-level tests derived from `TEST_COVERAGE.md`. They intentionally sit above the existing focused bridge contracts. Their job is to prove that already-covered primitives work together as player-facing RimWorld behavior.

Do not add a custom bridge endpoint for each scenario. Prefer generic operations:

- load a named save
- spawn or find pawns/things/terrain by def name
- set settings and colonist toggles
- issue jobs or debug actions
- step real game ticks
- read map/pawn/thing/log/UI state
- save, reload, and re-read state
- capture visual evidence only for visual/UI/effect assertions

Only add Zombieland-specific bridge code when a required assertion cannot be expressed through generic RimWorld/RimBridge operations or a stable shared helper.

## Scenario Template

Each scenario should record:

| Field | Requirement |
| --- | --- |
| ID | Stable scenario id, for example `S-Settings-Persistence`. |
| Goal | One player-facing behavior cluster. |
| Prior evidence | Commit hashes for narrow behaviors this scenario should not duplicate. |
| Source checks | Zombieland files plus RimWorld 1.6 decompiler targets that define the expected behavior. |
| Fixture | Save name and whether immutable, generated, or disposable. |
| Setup | Generic bridge/debug actions that create the state. |
| Runtime | Tick/frame/load/save/actions to execute. |
| Assertions | Compact machine-readable assertions where possible. |
| Artifacts | Log summary, save name, screenshot/cell-rect, timing sample, or source audit note. |
| Completion | Exact condition for marking this scenario covered. |

## S-Source-Patch-Audit

Goal: create and maintain the source-derived map from Zombieland code surfaces to required scenario coverage, with every Harmony patch group checked against RimWorld 1.6 before runtime scenarios rely on it.

Prior evidence:
- `90f62d4 Establish RimWorld 1.6 port baseline`
- Many later commits prove individual patch groups by behavior, but there is no whole-mod patch inventory.

Source checks:
- Run `scripts/coverage-inventory.sh` for the summary, `scripts/coverage-inventory.sh --patch-groups` for the TSV patch-group inventory, `scripts/coverage-inventory.sh --dynamic-patches` for dynamic target lookup classes, and `scripts/coverage-inventory.sh --static-summary` for scenario-owned static patch slices.
- Zombieland: every file listed under `Harmony Patch Files`, every file listed under `Gameplay Classes`, and every hook listed under `Serialization Tick UI Hooks`.
- RimWorld 1.6 decompiler: resolve target members for all `[HarmonyPatch]` groups, especially dynamic `TargetMethod()`/`TargetMethods()` patches and transpilers in `Source/Patches.cs`, `Source/Patches_Hostility.cs`, `Source/ContaminationPatches*.cs`, `Source/CETools.cs`, `Source/SoSTools.cs`, `Source/RimConnectSupport.cs`, `Source/ZombieAreaManager.cs`, `Source/ZombieDamageFlasher.cs`, `Source/ZombieLeaner.cs`, `Source/Assets.cs`, and `Source/Service.cs`.
- RimWorld 1.4 decompiler: use only when the intended legacy behavior is unclear from current code or commit evidence.

Fixture:
- No map save required for the source audit itself.
- The output should be a compact generated inventory artifact or a checked matrix update, not a chronological log.

Setup:
- Generate the current source/commit inventory.
- Group patch targets by player-facing subsystem rather than by file size.
- For each group, attach existing commit evidence if the long history already proves the behavior.

Runtime:
- Use decompiler lookups for target existence and signature shape.
- Do not run live RimWorld unless a target is ambiguous or a source check reveals a likely semantic drift.

Assertions:
- Every Harmony patch group has one of: covered by existing commit evidence, assigned to a named scenario in this file, intentionally optional integration, or marked as a current gap.
- `static` rows have a RimWorld 1.6 target member signature check; `dynamic` rows have their target lookup expression checked; `base` rows are only accepted when paired with concrete method-level patch rows.
- Dynamic patch target lookup has an explicit RimWorld 1.6 member id, a semantic-runtime patch-application check for compiler-generated closure searches, or an unavailable-mod classification.
- Obsolete RimWorld 1.4-only targets are not automatically failures, but they need a written disposition: covered by a new RimWorld 1.6 patch, covered by vanilla 1.6 behavior, assigned to a visual/runtime scenario, or queued for code removal.
- No large patch file is treated as covered merely because another patch in the same file passed.

Artifacts:
- Compact inventory output from `scripts/coverage-inventory.sh`.
- Dynamic target classification output from `scripts/coverage-inventory.sh --dynamic-patches`.
- Static target slice output from `scripts/coverage-inventory.sh --static-summary`.
- Target-level dispositions in `TEST_PATCH_AUDIT.md` when source/decompiler evidence is too detailed for the coverage matrix.
- Updated `TEST_COVERAGE.md` entries only when the audit changes cluster status.
- Optional separate generated artifact if the target-member table becomes too large for the hand-maintained matrix.

Completion:
- Covered when all patch groups and gameplay extension classes have explicit coverage disposition and the audit can be regenerated without hand-scanning large source files.

## S-Settings-Persistence

Goal: prove that default settings, in-game settings, time keyframes, per-colonist toggles, and modal settings surfaces still work and persist in RimWorld 1.6.

Prior evidence:
- `13f3b89 Restore Zombieland menu texture`
- `dc443f1 Fix map-target debug actions`
- Behavior contracts that toggle settings internally are useful reference, but they do not cover UI or persistence.

Source checks:
- Zombieland: `Source/Main.cs`, `Source/ZombieSettings.cs`, `Source/SettingsDialog.cs`, `Source/Dialog_Settings.cs`, `Source/Dialog_AdvancedSettings.cs`, `Source/Dialog_ApparelBlacklist.cs`, `Source/Dialog_BiomeList.cs`, `Source/Dialog_ThumperSettings.cs`, `Source/ColonistSettings.cs`.
- RimWorld 1.6 decompiler: `RimWorld.Planet.WorldComponent.ExposeData()` exists (`88df42e0e943404ba42043aebd37585c:06015389:M`) and still routes through `BackCompatibility.PostExposeData(this)`; `Verse.ModSettings.ExposeData()` exists (`88df42e0e943404ba42043aebd37585c:0600252B:M`); `RimWorld.Dialog_ModSettings(Mod mod)` exists (`88df42e0e943404ba42043aebd37585c:06011E7F:M`); `Dialog_ModSettings.PreClose()` exists (`88df42e0e943404ba42043aebd37585c:06011E80:M`) and still calls `mod.WriteSettings()`.
- Confirm that `Dialog_Settings` still extends a valid `Page` flow and that its save/close behavior still writes world settings through the same component path.

Fixture:
- `EMPTY.rws` as immutable input.
- Generated save: `ZL_Settings_Persistence_00.rws`.

Setup:
- Load `EMPTY`.
- Ensure at least two colonists exist or spawn deterministic colonists.
- Open mod settings from main menu or use a bridge settings setter plus one UI confirmation path.
- Set representative defaults before world load: spawn mode, attack mode, smash mode, infection chance, eating toggles, avoidance, contamination base factor, visual toggles.

Runtime:
- Start/load map, open in-game Zombieland settings, change the same representative settings.
- Add two settings keyframes with different values and step to lower, middle, and upper tick windows.
- Toggle one colonist on/off for auto-avoid, auto-double-tap, and auto-extract.
- Open and close apparel blacklist, biome blacklist, advanced constants, and thumper settings.
- Save, reload, and re-read settings and colonist config.

Assertions:
- Defaults persist after `WriteSettings` and restart.
- `ZombieSettings.Values` and `ValuesOverTime` persist through save-load.
- `CalculateInterpolation` returns lower, interpolated, and upper values at expected ticks.
- Per-colonist toggles persist and are respected by workgiver or avoidance checks.
- Modal edits update the correct backing settings or constants.
- Log summary contains no settings serialization, missing translation, or UI exceptions.

Artifacts:
- Save files `ZL_Settings_Persistence_00.rws` and `ZL_Settings_Persistence_01_modals.rws`.
- Deduped log summary.
- One settings-window screenshot only if UI layout or labels look suspect.

Completion:
- Covered when all assertions pass through a save-load cycle and the source checks are recorded with the RimWorld 1.6 target member ids.

Current runtime evidence:
- Added 2026-05-30 with `zombieland/settings_state`, a reusable settings tool with `read`, `prepare`, and `verify` modes. From `EMPTY`, `actionMode:prepare openSettingsDialog:true` created three `ZombieSettings.ValuesOverTime` keyframes at 0, 2, and 5 days, set representative fields across spawn mode, attack mode, smash mode, eating, infection, avoidance, visuals, max zombie count, special threat, apparel blacklist, biome blacklist, and contamination base factor, spawned `ZL_Settings_Colonist`, and set its persisted `ColonistConfig` to `autoAvoidZombies=false`, `autoDoubleTap=false`, `autoExtractZombieSerum=true`. The tool also opened the real `Dialog_ModSettings` path and returned `settingsCategory="Zombieland settings"`.
- The fixture was saved as `ZL_Settings_Persistence_00.rws`, reloaded, then `zombieland/settings_state actionMode:verify openSettingsDialog:true` returned `success=true`: the `ZombieSettings` world component existed, all three keyframes persisted with ticks `0`, `120000`, and `300000`, deterministic interpolation samples matched the source `CalculateInterpolation` contract at day 0, day 1, day 2, day 3, and day 6, and the spawned colonist's three toggles persisted through `ColonistSettings`. The live `Values` after reload had advanced slightly from day 0 because save/load elapsed real game ticks, so the verifier intentionally asserts fixed interpolation samples rather than treating current tick-dependent values as constants. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Modal coverage was added through the same tool with `actionMode:modal` from `ZL_Settings_Persistence_00.rws`. The live run opened and validated real `Dialog_ApparelBlacklist`, `Dialog_BiomeList`, `Dialog_AdvancedSettings`, `Dialog_ThumperSettings`, and the parent `Dialog_ModSettings` path. It proved the apparel dialog sanitizes invalid apparel and preserves a real candidate (`Apparel_PlateArmor`), the biome dialog retains a real biome candidate (`AridShrubland`) and exposes the current biome list, the advanced dialog `PreClose()` writes a changed `Constants.DEBUG_MAX_ZOMBIE_COUNT` to `ZombielandAdvancedSettings.json` and the verifier restores the previous value afterward, and the thumper settings dialog can open against a real spawned `ZombieThumper` with live intensity/interval backing fields. The modal-backed keyframe changes were saved as `ZL_Settings_Persistence_01_modals.rws`, reloaded, and `actionMode:verify` stayed green with day-0 interpolation showing `blacklistedApparel=["Apparel_PlateArmor"]` and `biomesWithoutZombies=["AridShrubland"]`. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Downstream behavior coverage was added with `actionMode:behavior` from `ZL_Settings_Persistence_01_modals.rws`. The verifier reused the persisted `ZL_Settings_Colonist` config (`autoAvoidZombies=false`, `autoDoubleTap=false`, `autoExtractZombieSerum=true`) and proved the corresponding setting gates on real game objects: non-forced and forced serum extraction both accepted a spawned `ZombieCorpse` and produced `ExtractZombieSerum`; non-forced double-tap rejected an infected human corpse while forced double-tap accepted it and produced `DoubleTap`; with `betterZombieAvoidance=true`, active avoid danger (`avoidCost=972`, `shouldAvoid=true`), and a non-forced `Goto` job, the auto-avoid patch did not convert the pawn to `Flee` while the persisted colonist toggle was false. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Per-colonist gizmo coverage was added with `actionMode:gizmos` from `ZL_Settings_Persistence_01_modals.rws`. The verifier used the saved settings map as the fixture anchor, spawned and destroyed a temporary capable colonist, seeded vanilla prioritized work, and enumerated the real `PriorityWork.GetGizmos()` output. It proved the vanilla clear-priority command still appears (`vanillaPriorityCommandCount=1`), the three Zombieland commands are present and enabled when their global gates and pawn capabilities allow them, the disabled-state descriptions begin as "Ignore zombies", "Only extract zombie serum when ordered", and "Only double tap when ordered", and invoking the three `Command_Action` delegates flips `ColonistConfig` from all false to all true with the enabled descriptions updating to automatic avoid/extract/double-tap text. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- This is partial `S-Settings-Persistence` coverage. It proves in-game world settings, keyframe interpolation, colonist-toggle serialization, real mod settings dialog construction, modal construction/backing-state behavior for apparel/biome/advanced/thumper settings, save-load for the modal-backed keyframe values, downstream setting-gate behavior for serum extraction, double-tap, and auto-avoid false, and direct `PriorityWork.GetGizmos()` command presence/actions for the three per-colonist toggles. It does not yet prove default-settings restart persistence, actual mouse-driven edits through every modal row, save-then-uninstall, or every UI-edited toggle from a visible mouse path.

## S-Core-Horde-Loop

Goal: prove the main zombie loop over real time: spawn, wander, sense, track, rage, attack/eat/smash, react to fire, die, leave corpse/filth, and survive save-load.

Prior evidence:
- `3981f82 Fix zombie movement with async path requests`
- `f43506a Add path avoidance recalculation smoke`
- `36915af Restore avoid-grid path costs`
- `2bce190 Add zombie door close smoke`
- `635be71 Restore zombie blood filth contract`
- `8d4e084 Cover zombie fire attachment state`
- `7ccd5e4 Verify zombie fire rain vulnerability`
- `1233bb1 Extend burn-longer fire hooks`
- `c6f2b87 Optimize zombie tick sampling`
- `4e90c65 Trim zombie ticking hot path`

Source checks:
- Zombieland: `Source/Zombie.cs`, `Source/ZombieGenerator.cs`, `Source/ZombieStateHandler.cs`, `Source/JobDriver_Stumble.cs`, `Source/JobGiver.cs`, `Source/PheromoneGrid.cs`, `Source/TickManager.cs`, `Source/ZombieWanderer.cs`, `Source/ZombieCorpse.cs`.
- RimWorld 1.6 decompiler: `Pawn_PathFollower`, `JobDriver`, `Thing.TakeDamage`, `Fire`, `Building_Door`, and any Harmony targets touched by the horde path.

Fixture:
- Generated save: `ZL_Core_HordeLoop_base.rws`.
- Map layout: one room, one corridor, one door, one weak wall segment, one corpse, one downed pawn, one drafted colonist, one fire source.

Setup:
- Set zombie count limits low enough for deterministic assertions.
- Spawn mixed normal zombies at known distances.
- Create smell/hearing stimuli: moving pawn, weapon fire or equivalent sound event, corpse/downed pawn.

Runtime:
- Step real game ticks in phases: idle, stimulus, tracking, door/wall contact, eating, fire, death/corpse.
- Save during an active path request or active job if feasible, reload, continue stepping.

Assertions:
- Zombies do not drop destination because an async path request has no current path yet.
- Pheromone or tracking state changes after stimulus and fades later.
- Door, wall, corpse/downed-pawn, and fire behavior match settings.
- Corpse/desiccation/blood filth behavior appears according to settings.
- Reloaded zombies keep valid draw/jobs/tick registration.
- Log summary contains no pathing, render-tree, job-driver, or stale-map exceptions.

Artifacts:
- Save file before and after active loop.
- Compact bridge state dump.
- One screenshot/cell-rect if visual clustering or corpse state is ambiguous.
- Optional performance sample for tick time, but the dense perf scenario remains separate.

Current runtime evidence:
- Added 2026-05-30 with `zombieland/ambient_temperature_contract` from `EMPTY.rws`. The live run spawned a human, human corpse, normal zombie, zombie corpse, spitter, and blob on an outdoor map with local cell temperature `4.5` degrees and outdoor temperature about `4.67`. Ordinary human and human corpse `AmbientTemperature` matched the cell temperature (`4.5`), while `Zombie`, `ZombieCorpse`, `ZombieSpitter`, and `ZombieBlob` all returned the forced normal `21` degrees from `Thing_AmbientTemperature_Patch`. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Added 2026-05-30 with `zombieland/zombie_damage_log_association_suppression` from `EMPTY.rws`. The live run applied damage to an ordinary human, normal zombie, spitter, and blob. The human control produced one result hediff and one new combat-log text association, while all three Zombieland pawn classes produced result hediffs with no combat-log text association. The first attempt exposed a fixture cleanup bug that left damaged spawned pawns behind; the bridge tool now destroys those pawns before returning, and the rerun completed with no warning-or-higher log entries.

Completion:
- Covered when the loop completes before and after reload without manual repair and without adding a scenario-specific endpoint.

## S-Infection-Medical

Goal: prove zombie bites, infection states, medical recipes, surgery targets, corpse conversion timers, and infection alerts behave as a coherent RimWorld medical workflow.

Prior evidence:
- `623822f`, `fd1c12f`, `d637d99`, `2fd1969`, `2cf6be8`, `89a3c45`, `9c46bc0`
- `bac414e`, `7127f37`, `0235865`, `b7d560e`

Source checks:
- Zombieland: `Source/Hediff_Injury_ZombieBite.cs`, `Source/HediffComp_Zombie_Infecter.cs`, `Source/HediffComp_Zombie_TendDuration.cs`, `Source/Recipe_CureZombieInfection.cs`, `Source/JobDriver_DoubleTap.cs`, `Source/JobDriver_ExtractZombieSerum.cs`, `Source/WorkGivers.cs`, `Source/ZombieCorpse.cs`, `Source/Alerts.cs`.
- RimWorld 1.6 decompiler: `Verse.HediffUtility.CanHealNaturally(Hediff_Injury)` (`40ca93f8c2cf4459bd8a72c25d1bfb81:06002CCE:M`), `RimWorld.Recipe_RemoveBodyPart.GetPartsToApplyOn(Pawn, RecipeDef)` (`40ca93f8c2cf4459bd8a72c25d1bfb81:0600C371:M`), and `Verse.Hediff.ShouldRemove` (`40ca93f8c2cf4459bd8a72c25d1bfb81:1700074D:P`).

Fixture:
- Immutable input: `EMPTY.rws`.
- Future generated save: `ZL_Infection_Medical_00.rws` for a player workflow that persists bite timers through save-load.

Setup:
- Spawn one doctor, one bitten patient, and one animal or non-human negative case.
- Apply deterministic bite stages: hidden, harmless, infectable, infecting, and death-conversion-ready.
- Stage serum, extract, infected corpses, and one removable bitten body part.

Runtime:
- Run compact medical patch contracts for natural healing, bite persistence, and remove-body-part targets.
- Execute cure, double-tap, serum extraction, and corpse conversion countdown in one map session.
- Save during the countdown, reload, and continue until either conversion or prevention is proven.
- Read infection alerts before and after cure/conversion.

Assertions:
- Hidden, infectable, and infecting zombie bites do not heal naturally and do not disappear when severity reaches zero.
- Harmless bites, ordinary cuts, and non-human/non-infectable cases keep vanilla healing/removal behavior.
- `RemoveBodyPart` offers every distinct bitten part exactly once and does not add null or duplicate surgery targets.
- Cure/double-tap/extract/corpse conversion behaviors match the focused contracts in the same loaded workflow.
- Infection alerts point to the expected pawns and clear after cure, harmless treatment, or conversion.
- Log summary contains no health-card, recipe, hediff, corpse-rot, or conversion exceptions.

Current runtime evidence:
- Added 2026-05-30 with `zombieland/infection_medical_state` from `EMPTY.rws`. The live run spawned a temporary colonist and grizzly bear, applied hidden, harmless, infectable, and infecting `ZombieBite` hediffs plus a normal `Cut`, and verified natural healing behavior: hidden/infectable/infecting bites returned `CanHealNaturally=false`, harmless bites and ordinary cuts returned true, and an animal `ZombieBite` with infection state `None` also returned true through vanilla behavior. With severity set to zero, hidden/infectable/infecting bites returned `ShouldRemove=false`, while the harmless bite and ordinary cut returned true. `Recipe_RemoveBodyPart.GetPartsToApplyOn` returned all four distinct bitten parts once each, with no missing or duplicate bitten parts. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Added 2026-05-30 with `zombieland/zombie_corpse_alert_forbid_contract` from `EMPTY.rws`. The live run proved a human corpse still returns true from the unburied-colonist alert helper and becomes forbidden when outside the home area. Normal and former zombie corpses both returned false for the alert helper and stayed unforbidden after the same outside-home forbid call. The same run verified serum extraction remains available for both zombie corpse variants and double-tap remains unavailable for both. `rimbridge/list_logs minimumLevel=warning` returned no entries.

Completion:
- Partially covered by focused contracts and the 2026-05-30 medical/corpse patch runs. Remaining workflow proof: save-load during corpse conversion countdown, infection alert list behavior, and cure/double-tap/extract/medical UI in one player-facing map session.

## S-Special-Gauntlet

Goal: prove all special zombie types work together on one controlled map and that their visuals/effects remain correct across damage, action, death, cleanup, and save-load.

Prior evidence:
- `2325c82`, `860832a`, `81e6c2b`, `d6d2870`, `3daf2d5`, `b0d97f5`, `5ba1b80`, `9638321`
- `04c6412`, `58350bc`, `c181649`, `aeeac24`
- `b95123c`, `4d723cb`, `8c75244`, `1c0e9a4`, `1ac0117`, `1bedcf1`
- `dd90b2f`, `12cb6a2`, `f3a44b2`, `62343fa`, `68ebd8c`
- `cd0e93b`, `0146ec7`, `5a22ef0`

Source checks:
- Zombieland: `Source/Zombie.cs`, `Source/ZombieSpitter.cs`, `Source/ZombieBall.cs`, `Source/ZombieBlob.cs`, `Source/ZombieBlobRenderer.cs`, `Source/JobDriver_Spitter.cs`, `Source/JobDriver_Blob.cs`, `Source/JobDriver_Sabotage.cs`, `Source/TarSmoke.cs`, `Source/TarSlime.cs`, `Source/BombVest.cs`, `Source/OverlayDrawer.cs`.
- RimWorld 1.6 decompiler: projectile launch/impact path, pawn draw/render-node path, melee verb path, explosion/damage application path.

Fixture:
- Generated save: `ZL_Special_Gauntlet_base.rws`.
- Layout: separate lanes for suicide bomber, toxic splasher, tanky, miner, electrifier, albino, dark slimer, healer, spitter, blob.

Setup:
- Spawn one special per lane plus one colonist target, one powered building, one wall/mineable, one door, one wounded zombie, and one ranged attacker.

Runtime:
- Step each lane enough for the special action to trigger.
- Damage or kill each special and observe death effects.
- Save after active effects exist, reload, step again.

Assertions:
- Each special reaches the intended action without blocking the others.
- Death/impact effects create expected things/hediffs/flecks/gas/damage.
- Tar smoke and sticky goo affect targeting/move costs.
- Electrifier behaves differently against ranged and melee attacks.
- Tanky armor/smash and miner mining still occur in mixed context.
- Albino scream/sabotage does not create unwanted bite/infection.
- Visuals are nonblank and recognizable for active specials and effects after reload.

Artifacts:
- Save before action, save after action, save after reload.
- One wide screenshot or cell-rect montage.
- Deduped log summary.

Current runtime evidence:
- Added 2026-05-30 with `zombieland/zombie_skin_color_contract` from `EMPTY.rws`. The live run set an ordinary human `Pawn_StoryTracker.SkinColorBase` to `{r:0.25,g:0.2,b:0.15,a:1}` and verified that the human kept that vanilla story color while a naturally generated `Zombie` returned `Color.white` through `Pawn_StoryTracker_SkinColorBase_Patch`. Spawned `ZombieSpitter` and `ZombieBlob` instances did not naturally have story trackers in this fixture, so the probe attached temporary `Pawn_StoryTracker` instances only to exercise the patch type guard; both then returned `Color.white` and reported `storyInjectedForProbe=true`. `rimbridge/list_logs minimumLevel=warning` returned no entries. This is a narrow property/patch-boundary proof, not visual rendering coverage.
- Added 2026-05-30 with `zombieland/zombie_gene_rejection_contract` from `EMPTY.rws`. The active fixture is Core-only (`biotechActive=false`), so the contract used temporary in-memory `GeneDef`/`Gene` instances to exercise the installed `Pawn_GeneTracker.AddGene` overloads without requiring Biotech defs. The human control had a natural gene tracker, accepted both the public `AddGene(GeneDef, bool)` and private `AddGene(Gene, bool)` paths, and gene count increased `2 -> 3 -> 4`. The normal `Zombie` had a natural gene tracker and rejected both overloads with null results and count `0 -> 0 -> 0`. Spawned `ZombieSpitter` and `ZombieBlob` had no natural gene tracker in this fixture, so the probe attached temporary `Pawn_GeneTracker` instances; both rejected both overloads with null results and count `0 -> 0 -> 0`. `rimbridge/list_logs minimumLevel=warning` returned no entries. This covers the patch boundary, not a full Biotech DLC gameplay workflow.

Completion:
- Covered when all special lanes pass in one map session and at least the visual lanes are checked after reload.

## S-Contamination-Persistence

Goal: prove contamination is durable and player-visible across cells, pawns, things, jobs, effects, replacement chains, and save-load.

Prior evidence:
- `11a0c90`, `2bee432`, `22b45b3`, `fa02d3f`, `7e27948`
- `2c02244`, `324cfb2`, `ae37dda`, `b394e63`, `23b889f`, `68fb03e`
- `cf0479e`, `662d902`, `065b1a5`, `5f73f13`, `fff7028`, `f73fe81`
- `92b46aa`, `40c3613`, `33b4ad2`, `a125cdb`, `f8f709d`, `a908279`, `5f9f91e`
- `45520ed`, `5b550e4`, `beeb01c`, `51b739b`

Source checks:
- Zombieland: `Source/ContaminationManager.cs`, `Source/ContaminationSerializer.cs`, `Source/ContaminationEffect.cs`, `Source/ContaminationNeed.cs`, `Source/Hediff_Contamination.cs`, `Source/MentalState_Contamination.cs`, `Source/JobDriver_Contamination*.cs`, `Source/ContaminationPatches*.cs`, `Source/QuestDecontaminateColonists.cs`.
- RimWorld 1.6 decompiler: `Scribe`, `ThingOwner.TryTransferToContainer`, `Thing.TryAbsorbStack`, construction frame/building replacement path, plant work toils, mineable/deep drill yield path, pollution/snow clear toils.

Fixture:
- Immutable input: `EMPTY.rws`.
- Generated saves: `ZL_Contamination_Persistence_00_setup.rws`, `ZL_Contamination_Persistence_10_handoffs.rws`, `ZL_Contamination_Persistence_20_player.rws`, and `ZL_Contamination_Persistence_30_quest.rws`.

Setup:
- Create contaminated pawns, cells, terrain, stackable items, corpse, plant, mineable, frame, building, and active contamination effect.
- Create one pawn in each contamination mental/job state or stage those jobs sequentially.

Runtime:
- Save immediately after setup, reload, verify state.
- Execute replacement chains: stack absorb/split, blueprint/frame/building, minify/install, plant harvest/sow, mine/drill, snow/pollution clear, corpse handoff.
- Trigger exposure and treatment progression.
- Start decontamination quest if available and save while active.

Runtime batch plan:

| Batch | Save target | Existing bridge contracts to treat as history/narrow evidence | Generic runtime actions to sequence | Extra assertions still needed |
| --- | --- | --- | --- | --- |
| `C0-state-serializer` | `ZL_Contamination_Persistence_00_setup.rws` | `zombieland/contamination_core_contract`, `zombieland/contamination_effect_manager_contract`, `zombieland/read_contamination_state` | Load `EMPTY`, create fixed cells/things/pawns, set contamination values, save, reload, and read the same cells/things/pawns through generic state queries. | Covered 2026-05-29: core/effect contracts passed before and after reload; exact persisted stack contamination read back as `0.6`. |
| `C1-stack-container` | `ZL_Contamination_Persistence_10_handoffs.rws` | `zombieland/contamination_stack_absorb_contract`, `zombieland/contamination_carry_tracker_contract`, `zombieland/contamination_ingestion_contract`, `zombieland/contamination_thing_owner_transfer_contract`, `zombieland/complete_frame_by_id`, `zombieland/write_contamination_state`, `zombieland/read_contamination_state` | Split and merge stacks, move items through a pawn/container/frame, ingest contaminated food, save, reload, and re-read contamination. | Covered 2026-05-29: `TryAbsorbStack`, `SplitOff`, carry ticks, ingestion, contaminated stack save-load, `ThingOwner.TryTransferToContainer`, and reloaded frame completion fallback passed. |
| `C2-build-replace` | `ZL_Contamination_Persistence_15_build_replace.rws` | `zombieland/contamination_building_install_contract`, `zombieland/contamination_frame_construction_contract`, `zombieland/contamination_terrain_construction_contract`, `zombieland/contamination_smooth_wall_contract`, `zombieland/contamination_smooth_wall_reversion_contract`, `zombieland/read_contamination_state` | Build from contaminated resources, complete frame, minify, install, reinstall, smooth wall, destroy/revert wall, save, reload. | Covered 2026-05-29: building, terrain/foundation, blueprint, minified, installed, smoothed, and reverted wall chains passed and persisted after reload. |
| `C3-world-products` | `ZL_Contamination_Persistence_20_world_products.rws` | `zombieland/contamination_filth_leavings_contract`, `zombieland/contamination_mineable_yield_contract`, `zombieland/contamination_deep_drill_contract`, `zombieland/contamination_plant_stump_contract`, `zombieland/contamination_plant_harvest_contract`, `zombieland/contamination_plant_sow_contract`, `zombieland/contamination_wild_plant_spawn_contract`, `zombieland/contamination_ambrosia_sprout_contract`, `zombieland/contamination_roof_collapse_contract`, `zombieland/read_contamination_state` | Generate products through real jobs/incidents where cheap, otherwise call the same vanilla method path, save, reload, and inspect spawned products. | Covered 2026-05-29 with caveat: stable products persisted with exact contamination; compressed slag chunks are runtime-proven but reload under regenerated ids/positions, so persisted leavings coverage uses the stable component leaving. |
| `C4-cleaning-environment` | `ZL_Contamination_Persistence_25_cleaning_environment.rws` | `zombieland/contamination_cell_fire_contract`, `zombieland/contamination_clear_snow_contract`, `zombieland/contamination_clear_pollution_contract`, `zombieland/read_contamination_state` | Contaminate cells, enter cells, burn cells/things, clear snow/sand/pollution with real jobs, save, reload. | Covered 2026-05-29 for active core config: cell entry, fire reduction, and snow clearing persisted; pollution skipped because active config lacks `Wastepack`/`ClearPollution`. |
| `C5-player-effects` | `ZL_Contamination_Persistence_30_player_effects.rws` | `zombieland/contamination_rest_comfort_contract`, `zombieland/contamination_melee_equalize_contract`, `zombieland/contamination_tending_contract`, `zombieland/contamination_hallucination_contract`, `zombieland/contamination_mimic_contract`, `zombieland/contamination_sleepwalk_contract`, `zombieland/contamination_breakdown_contract`, `zombieland/contamination_hoard_pather_failure_contract`, `zombieland/contamination_hoard_driver_flow_contract`, `zombieland/read_contamination_effect_state` | Run exposure, rest, melee, tending, mental effects, and hoarding in one colony map, save during active jobs, reload, and continue ticks. | Covered 2026-05-29 for active player effects: focused rest/melee/tending passed, and the combined save-load workflow kept hallucination, mimic, sleepwalk, breakdown, and pather-failure hoard active together after reload and 270 post-reload ticks with no warning-or-higher logs. |
| `C6-ui-quest` | `ZL_Contamination_Persistence_30_quest.rws` | `zombieland/contamination_ui_quest_contract`, `zombieland/contamination_accepted_quest_contract` | Use generic UI/state reads for inspect pane, thing icons, mouseover, overlay toggle, quest list, decontamination quest start/save/reload, accepted pickup loading, send signal, return success, and legacy outcome branches. | Covered 2026-05-30 for contamination visibility, decontamination quest generation through the Core-safe 1.6 pickup transporter node, challenge-rating suppression, quest-tab row integration, save/reload persistence, accepted send/return success, pickup destruction failure, `SentUnsatisfied` failure, and sent-pawn death legacy success. |

GABS/RimBridge execution shape:

- Start from `games_status`; if stopped, use `games_start` and then `rimworld/load_game_ready` for `EMPTY`.
- Discover live names with `games_tool_names` and inspect only `rimworld/load_game_ready`, `rimworld/save_game`, `rimworld/list_saves`, the required generic state/read tools, and the existing contamination contracts that are being used as narrow checks.
- Prefer one batch runner sequence per row above. Do not add a new Zombieland bridge endpoint unless a row cannot be asserted through generic spawn/set/job/tick/read/save calls plus the already-existing narrow contracts.
- After each batch, save the named target, run the log summarizer, and store only compact state differences in the artifact note.

Current runtime evidence:

- `C0-state-serializer` is covered by live evidence from 2026-05-29. GABS started RimWorld, loaded `EMPTY`, ran `zombieland/contamination_core_contract` and `zombieland/contamination_effect_manager_contract` successfully, saved `ZL_Contamination_Persistence_00_setup.rws`, reloaded that save, and ran both contracts successfully again.
- Generic state reads after reload confirmed first-run state persisted at the expected cells: `Thing_Human6898` at `(100,100)`, `Thing_Human6904` at `(101,100)`, and `ComponentIndustrial x6` at `(103,100)`.
- The reusable `zombieland/read_contamination_state` tool read exact post-reload contamination values: saved component `Thing_ComponentIndustrial6902` at `(103,100)` had contamination `0.6`, matching the pre-save contract value; saved pawns `Thing_Human6898` and `Thing_Human6904` had stored contamination `0`, matching their cleared contract end state.
- Generic selection semantics confirmed `Thing_Human6904` still had `ContaminationJobForceRest`, `ContaminationStateForceRest`, and inspect text `Mental state: Resting` after reload. `rimbridge/list_logs` returned no warning-or-higher entries for the C0 run.
- `C1-stack-container` is covered by live evidence from 2026-05-29. `zombieland/contamination_stack_absorb_contract` passed with weighted merge `0.2 x6 + 0.8 x4 => 0.44 x10`; `zombieland/contamination_carry_tracker_contract` passed with pawn `0.060200002` and carried item `0.6398`; `zombieland/contamination_ingestion_contract` passed with eater `0.09600001` and remaining meal stack `0.403999984`.
- The `ZL_Contamination_Persistence_10_handoffs.rws` save was created after the C1 contracts plus a generic spawned `Steel x13` stack written to contamination `0.33`. After reload, `zombieland/read_contamination_state` confirmed `Thing_Human6904` at `(100,100)` stored `0.09600001`, `Thing_MealSurvivalPack6907` at `(103,100)` stored `0.403999984`, and `Thing_Steel6909` at `(106,100)` stored `0.33`. `rimbridge/list_logs` returned no warning-or-higher entries.
- A fresh C1 transfer run loaded `EMPTY`, ran `zombieland/contamination_thing_owner_transfer_contract` with `cleanup:false`, and saved `ZL_Contamination_Persistence_10_handoffs.rws`. The exact `ThingOwner.TryTransferToContainer(Thing, ThingOwner, Int32, out Thing, Boolean)` path passed for partial `4` and full remaining `6` transfers between frame resource containers: source count reached `0`, target count reached `10`, target frame contamination was `0.75`, and target frame including held wood was `1.5` before save.
- After reload, `zombieland/read_contamination_state` confirmed the serializer edge: target frame `Thing_Frame_Wall6899` at `(103,100)` kept direct contamination `0.75`, while its held `WoodLog x10` reloaded with contamination `0`. This drove the source fallback in `Frame_CompleteConstruction_Patch.ClearAndDestroyContents`, which uses frame contamination when held resource contamination is absent after reload.
- `zombieland/complete_frame_by_id` then completed reloaded frame `Thing_Frame_Wall6899` through real `Frame.CompleteConstruction`. It selected contamination source `0.75`, built `Thing_Wall6905`, and matched configured construction transfer values: final wall contamination `0.326435983` versus expected `0.326436`, worker contamination `0.00356400013` versus expected `0.00356400013`. `rimbridge/list_logs` returned no warning-or-higher entries.
- `C2-build-replace` is covered by live evidence from 2026-05-29. GABS loaded `EMPTY`, then ran the construction/replacement contracts with `cleanup:false` where fixture persistence was needed. `zombieland/contamination_frame_construction_contract` built `Thing_Wall6903` from contaminated wood with final contamination `0.348198384` versus expected `0.3481984`.
- `zombieland/contamination_building_install_contract` proved minify/install/reinstall and blueprint replacement: source stool contamination `0.6` moved to minified form, install blueprint, and final `Thing_Stool6907`; reinstall source contamination `0.45` moved to reinstall blueprint and final `Thing_Stool6908`.
- `zombieland/contamination_terrain_construction_contract` proved floor and bridge/foundation construction after fixture correction from RimWorld 1.6 decompiled `Frame.CompleteConstruction`: `WoodPlankFloor` cell `(104,100)` reached ground contamination `0.62`; `Bridge` over `WaterShallow` cell `(108,100)` reached ground contamination `0.74` with `foundationAfter`/`topAfter` `Bridge`.
- `zombieland/contamination_smooth_wall_contract` with `cleanup:false` proved natural-to-smoothed granite contamination `0.58` and nested reversion contamination `0.63`; after save/reload, `zombieland/read_contamination_state` confirmed `Thing_SmoothedGranite6924` at `(100,100)` contamination `0.58`, `Thing_SmoothedGranite6925` at `(100,101)` contamination `0.18`, reverted `Thing_Granite6932` at `(101,101)` contamination `0.63`, `Thing_Wall6903` contamination `0.348198384`, `Thing_Stool6907` contamination `0.6`, `Thing_Stool6908` contamination `0.45`, floor cell `0.62`, and bridge cell `0.74`. `rimbridge/list_logs` returned no warning-or-higher entries.
- `C3-world-products` is covered by live evidence from 2026-05-29. Existing contracts were extended with `cleanup:false` so generated products can be saved and read after reload. All real-path contracts passed before save from `EMPTY`: ambrosia incident spawned 13 ambrosia at contamination `0.68`; wild plant spawned grass at `0.201600015`; roof collapse spawned collapsed rocks at `0.67`; harvest produced raw rice at `0.256`; sow produced a rice plant at `0.207860008` and worker equalized to `0.68214`; filth/leavings produced blood at `0.02884` plus slag/component leavings at `0.160000011`; mineable yield produced steel at `0.65`; deep drill produced `Steel x45` at `0.420000017`; stump creation produced a chopped stump at `0.55`.
- The C3 save `ZL_Contamination_Persistence_20_world_products.rws` was created and reloaded after tightening fixture selectors and fixing compressed-map contamination edges. Generic readback succeeded with no missing stable targets: ship chunk `Thing_ShipChunk6902` at `0.48`, component leaving `Thing_ComponentIndustrial6908` at `0.160000011`, mineable steel `Thing_Steel6915` at `0.65`, deep-drill steel `Thing_Steel6917` at `0.420000017`, raw rice `Thing_RawRice6922` at `0.256`, sown rice `Thing_Plant_Rice6926` at `0.207860008`, stump `Thing_ChoppedStump6928` at `0.55`, wild grass `Thing_Plant_Grass6929` at `0.201600015`, and ambrosia `Thing_Plant_Ambrosia6931`/`6932`/`6933` at `0.68`. Cell readback also proved collapsed rocks persisted through the regenerated compressed-map id at `(99,100)` with contamination `0.67`.
- C3 caveat: RimWorld serializes slag chunks as compressed map things and reloads them under regenerated ids/positions around the ship chunk. The runtime leavings contract proves slag contamination before save, but the durable leavings assertion is the stable component product. `rimbridge/list_logs` returned no warning-or-higher entries for the final C3 run.
- `C4-cleaning-environment` is covered for the active core configuration by live evidence from 2026-05-29. RimWorld 1.6 decompiler checks confirmed the current shapes of `Pawn_FilthTracker.Notify_EnteredNewCell`, `Fire.DoComplexCalcs`, `JobDriver_ClearSnowAndSand.MakeNewToils`, and `JobDriver_ClearPollution.MakeNewToils`. GABS loaded `EMPTY`; `zombieland/contamination_cell_fire_contract` passed with entry pawn contamination `0.009139201`, entry cell `0.8`, component/fire-cell contamination reduced from `0.4` to `0.292`; `zombieland/contamination_clear_snow_contract cleanup:false` passed with snow cleared from depth `1` to `0` and worker contamination `0.0443520024`; `zombieland/contamination_clear_pollution_contract cleanup:false` returned a dependency skip because `Wastepack` or `ClearPollution` is unavailable in the active config.
- The C4 save `ZL_Contamination_Persistence_25_cleaning_environment.rws` was created and reloaded. Generic readback confirmed entry cell `(100,100)` at `0.8`, fire cell `(104,100)` at `0.292`, snow-cleared cell `(101,100)` at `0.72`, entry pawn `Thing_Human6898` at `0.009139201` with contamination need/hediff, snow worker `Thing_Human6903` at `0.0443520024` with contamination need/hediff, and component `Thing_ComponentIndustrial6901` at `0.292`. `rimbridge/list_logs` returned no warning-or-higher entries for the C4 run.
- `C5-player-effects` is covered by live evidence from 2026-05-29 for active player-effect persistence. `zombieland/contamination_rest_comfort_contract` passed after fixing null cell-target handling in `ContaminationManager.UsesCellBackedContamination`: pawn contamination reached `0.00352` and ground fell to `0.79648`, matching `restEqualize`. `zombieland/contamination_melee_equalize_contract` passed with real melee damage and expected attacker/target contamination `0.132800013`/`0.667199969`. `zombieland/contamination_tending_contract` passed with medicine stack consumption and expected doctor/patient contamination `0.4`/`0.18`.
- Active C5 mental effects: `zombieland/contamination_sleepwalk_contract` passed, waking the bed occupant into `Flee`; `zombieland/contamination_breakdown_contract` passed and kept `ContaminationJobBreakdown`; `zombieland/contamination_hoard_driver_flow_contract` passed pickup/drop flow and kept `ContaminationJobHoard` through the 30-tick window. Custom job-driver `ExposeData()` coverage was added for hallucination, mimic, sleepwalk, and hoard fields before the final save-load check.
- The C5 save `ZL_Contamination_Persistence_30_player_effects.rws` was created and reloaded after the scribe fix. `zombieland/read_contamination_effect_state` confirmed three active contamination jobs after reload and after 30 ticks: sleepwalker `Thing_Human6899` kept `ContaminationJobSleepwalk` with `waitUntil=5252`; breakdown pawn `Thing_Human6905` kept `ContaminationJobBreakdown` and continued pathing toward `(123,164)`; hoarder `Thing_Human6958` kept `ContaminationJobHoard`, `state=moveToThing`, selected `Thing_Silver6957`, and continued pathing. `rimbridge/list_logs` returned no warning-or-higher entries for the final non-hallucination C5 run.
- C5 focused failures were resolved on 2026-05-29 after the broader save-load run. `zombieland/contamination_hallucination_contract` passed after avoiding recursive path callbacks and making ghost motes use offscreen-safe static motes; it kept `ContaminationJobHallucination`, ran the 30-tick source-derived loop, and moved the ghost. `zombieland/contamination_mimic_contract` passed after the fixture gained a second farther victim; it kept `ContaminationJobMimic`, switched tracked victim, applied `ZombieScare`, and started `Flee` on the scared victim. `zombieland/contamination_hoard_pather_failure_contract` passed after the hoard driver no longer starts paths inside pather callbacks and no longer calls `StopDead()` on failure; the follow-up read advanced 360 real ticks with the pawn still in `ContaminationJobHoard`, first moving to silver and then carrying it back to storage.
- Final combined C5 run: after adding a persisted assigned-bed reference to `JobDriver_ContaminationHoard` and filtering unsaved dummy rejected things from `rejectedThings` serialization, GABS loaded `EMPTY`, ran hoard pather-failure, hallucination, mimic, sleepwalk, and breakdown in one map, saved `ZL_Contamination_Persistence_30_player_effects.rws`, reloaded it, and read state with `zombieland/read_contamination_effect_state`. Immediately after reload all five active jobs were present: `ContaminationJobHoard`, `ContaminationJobHallucination`, `ContaminationJobMimic`, `ContaminationJobSleepwalk`, and `ContaminationJobBreakdown`. After 30 ticks they all remained active. After another 240 ticks, hoard had `roomCellCount=25`, `state=moveToStorage`, and carried `Thing_Silver6947`; hallucination kept `ghostPresent=true`; mimic remained in `ContaminationJobMimic`; sleepwalk stayed in its `waitUntil=5323` window; breakdown continued pathing. `rimbridge/list_logs` returned no warning-or-higher entries for the final save-load run.
- `C6-ui-quest` is covered for generation, UI persistence, and accepted success by live evidence from 2026-05-29. RimWorld 1.6 decompiler checks confirmed the patched UI targets still have the expected shapes: `Thing.SpecialDisplayStats()`, `Widgets.ThingIcon(Rect, Thing, Single, Nullable<Rot4>, Boolean, Single, Boolean)`, `MainTabWindow_Quests.DoRow` with `Mathf.Max(quest.challengeRating, 1)`, `MouseoverReadout.MouseoverReadoutOnGUI` with `UI.MouseCell()`, glow label, and `LabelMouseover`, `InspectPaneUtility.PaneWidthFor(IInspectPane)`, `InspectPaneFiller.DoPaneContentsFor(ISelectable, Rect)` with `DrawHealth(row, thing)`, and `BeautyDrawer.DrawBeautyAroundMouse()`.
- Generic bridge operations first proved the visual path without custom code: from `EMPTY`, `rimworld/spawn_thing` created `Steel x25`, `zombieland/write_contamination_state` set both steel and cell `(99,100)` to `0.65`, `rimworld/set_hover_target` hovered the cell, and screenshot `zl_c6_hover_contaminated_steel.png` showed `Steel x25, 65.00 % contaminated` plus `Lit (90%) Contaminated (65.00 %)`.
- The reusable `zombieland/contamination_ui_quest_contract` then prepared a screenshot-ready fixture from `EMPTY`: selected contaminated `Thing_Steel6898` at `(100,100)`, set overlay enabled, and returned `contaminationStatPresent=true` with stat entry `Zombie Contamination` = `65.00%`; patch-helper semantics returned `Steel x25, 65.00 % contaminated` and `Lit (90%) Contaminated (65.00 %)`. Screenshot `zl_c6_contamination_ui_fixture.png` showed the inspect-pane `65.00 % contamination` bar, selected steel icon overlay, and green contamination overlay marker on the map. `rimbridge/list_logs` returned no warning-or-higher entries for the bounded C6 visual run.
- The stale 1.4 `Util_TransportShip_Pickup` dependency was replaced by `ZombieLand.QuestNode_CreateDecontaminationPickupTransporter`, which creates the Core-safe `ZombieLand_DecontaminationTransportPod` thing with `CompTransporter` and a dedicated `QuestPart_DecontaminationPickupTransporter`. This avoids DLC shuttle code in the active Core-only configuration while keeping the legacy target signals: pickup `Destroyed`, `SentUnsatisfied`, and loaded-colonist `SentSatisfied`. From `EMPTY`, `zombieland/contamination_ui_quest_contract` returned `success=true`, `pickupPathAvailable=true`, `oldPickupUtilityDefPresent=false`, generated `Decontamination Offer`, and found the expected parts: `QuestPart_DecontaminationPickupTransporter`, `QuestPart_DecontaminateColonists`, and `QuestPart_TransporterPawns_TendWithMedicine`.
- The same C6 run proved challenge-rating UI semantics: `decontaminationMax.value=0` while an ordinary quest preserved `ordinaryMax.value=3`. After saving `ZL_Contamination_Persistence_30_quest.rws` and reloading it, the read-only contract stayed green with `decontaminationQuestPresent=true`, contaminated `Steel x25` still at `(100,100)`, and no new quest creation. The Quests tab layout showed row text `Decontamination Offer` with expiry `7d` and no rating text; screenshot `zl_c6_decontamination_quest_tab.png.png` captured the tab. `rimbridge/list_logs` returned no warning-or-higher entries for the final generation/UI C6 run.

- Accepted C6 success is covered by live evidence from `EMPTY` after replacing the missing 1.4 pickup shuttle utility with `QuestNode_CreateDecontaminationPickupTransporter` and `QuestPart_DecontaminationPickupTransporter`. The first accepted-flow attempt proved why vanilla `ThingDefOf.Shuttle` is not viable in the active Core-only configuration: a custom `TransportShipDef` fixed the `ShipJob_Arrive.TryStart()` null dereference, but `CompShuttle.PostSpawnSetup()` still logged the expansion-system error for `Shuttle`. The Core-safe pickup pod path uses `ZombieLand_DecontaminationTransportPod` with `CompTransporter` and no shuttle/DLC comp. `zombieland/contamination_accepted_quest_contract` accepted the quest, advanced `3500` ticks to pod arrival, loaded one colonist into the real transporter container, observed `partSentSatisfied=true`, forced the treatment return timer, and ended with `questState=EndedSuccess`; `rimbridge/list_logs` returned no warning-or-higher entries.
- Accepted C6 legacy outcome branches are covered by live evidence from fresh `EMPTY` loads on 2026-05-30. `outcomeMode=destroyPickup` accepted the quest, advanced `3500` ticks to pod arrival, destroyed the spawned `ZombieLand_DecontaminationTransportPod`, and ended with `questState=EndedFailed` while the treatment part stayed `NeverEnabled`. `outcomeMode=sendUnsatisfied` sent the pickup target signal and ended with `questState=EndedFailed` while the pod remained spawned. `outcomeMode=killSentPawn` loaded one colonist, observed `partSentSatisfied=true`, killed the sent pawn while away, and ended with `questState=EndedSuccess`, matching the RimWorld 1.4 `ColonistsDied` quest outcome. `QuestPart_DecontaminateColonists.Notify_PawnKilled` now ignores unrelated pawn deaths and only emits the branch signal for the sent subject. `rimbridge/list_logs minimumLevel=warning` returned no entries after the branch run.

Assertions:
- Serialized cell and thing contamination values match before/after reload within defined tolerance.
- Handoff chains preserve or clear contamination according to code rules.
- Need/hediff/effect/mental jobs progress and clear as expected.
- Quest part can start and remains valid across save-load.
- Log summary contains no Scribe node, component, job driver, or quest exceptions.

Artifacts:
- Before/after reload contamination state dump per runtime batch.
- Save files named by the batch table above.
- Deduped log summary.

Completion:
- Covered when every runtime batch above passes through a save-load cycle and any remaining untested row in `TEST_PATCH_AUDIT.md` has an explicit reason such as unavailable DLC/mod setup or obsolete disabled code.

## S-Defense-Room

Goal: prove the main player defensive tools work together: shocker, thumper, chainsaw, wall push, doors, power, repairs, and colonist combat/work.

Prior evidence:
- `fb618b6 Add zombie shocker bridge smoke`
- `ca51d4f Add thumper impact bridge smoke`
- `add9e67 Add chainsaw equip toggle bridge smoke`
- `3533ac7 Add chainsaw slaughter bridge smoke`
- `09cf58c Fix chainsaw repair workgiver`
- `18aa8dc Cover chainsaw pressure drop`
- `394e740 Optimize chainsaw ticking`
- `4f4a288 Recheck chainsaw contracts`
- `0cefc00 Restore thumper shockwave effect`
- `bddf65f Align thumper wave origin with damage`
- `895b05b`, `3b04020`, `2288209`, `7ff08fd` for wall push.

Source checks:
- Zombieland: `Source/Chainsaw.cs`, `Source/ZombieThumper.cs`, `Source/ZombieShocker.cs`, `Source/JobDriver_ZapZombies.cs`, `Source/JobDriver_FixBrokenChainsaw.cs`, `Source/CompActivatable.cs`, `Source/CompBreakable.cs`, `Source/PlaceWorker_ZombieShocker.cs`, `Source/WorkGivers.cs`.
- RimWorld 1.6 decompiler: power net connection/update, building placement, gizmo generation, job driver toil lifecycle, building damage.

Fixture:
- Generated save: `ZL_Defense_Room_base.rws`.

Setup:
- Small powered base room with battery/conduit, shocker, thumper, door, wall segment, repair resources, chainsaw-equipped colonist, and zombie group outside.

Runtime:
- Toggle shocker and thumper gizmos.
- Step attack wave through door/wall pressure.
- Use chainsaw in combat and against a building.
- Damage/break chainsaw or building and run repair/fix workgiver.
- Save with devices active, reload, continue.

Assertions:
- Placement/power/fuel/battery gates reject and accept correctly.
- Shocker affects room/zombies and consumes or depends on power as designed.
- Thumper visual wave origin matches damage origin.
- Chainsaw state, pressure, damage, and repair persist.
- Wall push warnings and damage occur only under gate conditions.
- Log summary contains no gizmo, comp, power-net, or job-driver exceptions.

Artifacts:
- State dump for devices before/after reload.
- Screenshot only for thumper wave/shocker effect if visual check is needed.

Completion:
- Covered when one room scenario exercises all devices and persists their state.

Current runtime evidence:
- Partial coverage added 2026-05-30 with reusable `zombieland/defense_room_state`, a scenario-level setup/read tool rather than another single-behavior smoke. RimWorld 1.6 decompiler checks confirmed the relevant current members before implementation: `PowerNetManager.UpdatePowerNetsAndConnections_First()`, `CompPowerTrader.PowerOn`, `CompPowerTrader.PostExposeData()`, and `ThingWithComps.GetGizmos()`.
- From `EMPTY`, `zombieland/defense_room_state setupFixture:true activateChainsaw:true advanceTicks:120` created a durable powered defense fixture and returned `success=true`: shocker `Thing_ZombieShocker6914` was on a wall, had a valid 9-cell room, `PowerOn=true`, one battery in its power net, and build-copy/reconnect gizmos; thumper `Thing_Thumper6918` was fueled `150 / 250`, switchable active, `intensity=0.25`, `intervalTicks=208`, `radius=12`, and exposed forbid, on/off, target-fuel, auto-refuel, and settings gizmos; equipped chainsaw `Thing_Chainsaw6923` was held by drafted colonist `Thing_Human6920`, activated through its command-action gizmo, running, and consuming fuel; spawned chainsaw `Thing_Chainsaw6925` was broken and tracked by `BrokenManager`; battery `Thing_Battery6917` held about `600 / 600 Wd`; placement checks accepted the valid shocker wall cell and rejected inside-room/no-room cells.
- The fixture was saved as `ZL_Defense_Room_base.rws`, reloaded, advanced 120 ticks, and read again with `zombieland/defense_room_state setupFixture:false`. Post-reload state stayed green: `poweredShocker=true`, `activeThumper=true`, `equippedRunningChainsaw=true`, `brokenChainsaws=1`, `brokenManagerCount=1`, `batteryCount=1`, one colonist, and one zombie. The equipped chainsaw remained unspawned but referenced to its pawn, running, drafted, and fuel-bearing; the broken ground chainsaw remained spawned, broken, allowed, fueled, and tracked. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Post-reload action checks added 2026-05-30 using the same generic tool from fresh `ZL_Defense_Room_base.rws` reloads. `actionMode:zapShocker` ran the real `ZapZombies` job, hit at tick 309, set `paralyzedUntilAfter=321719`, and emitted 8 zap motes. `actionMode:repairChainsaw` ran the real `FixBrokenChainsaw` job, completed at tick 1000, repaired the saved broken chainsaw, deregistered it from `BrokenManager`, and consumed one component from the stack (`3 -> 2`). `actionMode:chainsawBuilding` used the saved drafted colonist with a running chainsaw against an adjacent spawned wall and zombie; the wall took 80 HP damage, the chainsaw dropped/spawned, HP dropped `500 -> 480`, fuel decreased, the chainsaw became broken, and `BrokenManager` tracked it. `actionMode:thumperImpact` forced an active thumper impact from the saved fixture; fuel dropped `150 -> 149`, impact landed at tick 145, `lastImpactTicks` advanced `5159 -> 5305`, and distance falloff hit walls for 2 HP at range 3, 1 HP at range 12, and 0 HP at range 22. `rimbridge/list_logs minimumLevel=warning` returned no entries after these runs.
- Door/wall pressure coverage added 2026-05-30 after a RimWorld 1.4/1.6 decompiler comparison found that `JobDriver_AttackStatic.MakeNewToils()` moved the tick delegate from `<MakeNewToils>b__1` in 1.4 to `<MakeNewToils>b__2(int delta)` in installed 1.6. `Source/Patches.cs` now selects the generated tick delegate by its one-`int` parameter signature instead of hardcoding the stale closure name. From a fresh `ZL_Defense_Room_base.rws` reload, `actionMode:wallDoorPressure` passed: wall push started from `(102,108)`, crossed wall `(103,108)`, landed at `(104,108)` on tick 102, left the wall intact, cleared the constructed roof at the destination, and produced exactly one `DangerousSituation` letter for a home-area wall. The door half started a real zombie `AttackStatic` job against closed door `Thing_Door6935`, confirmed Harmony had patched installed 1.6 target `Verse.AI.<>c__DisplayClass5_0::<MakeNewToils>b__2(System.Int32 delta)` with owner `net.pardeike.zombieland`, opened the door, and observed the zombie leave `AttackStatic` on the next tick. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Remaining work: this proves durable setup, gizmo/readback, placement gates, serialization state, real shocker zap, real thumper impact/damage falloff, chainsaw building damage/breakage, repair workgiver behavior, wall pushing, wall-push warning letters, and the door-open static-attack stop path from the saved fixture. It does not yet prove player-facing visual/audio evidence beyond bridge-visible motes/damage/warning-letter state.

## S-Incident-Threat

Goal: prove player-visible incident scheduling, spawn modes, letters, threat forecast, and zombie limit behavior.

Prior evidence:
- `2cc1beb Cover event special zombie spawning`
- `6dd03e1 Cover incident scheduling calculation`
- `aab7620 Cover incident alert wave letters`
- `2ef5967 Cover random zombie type weights`
- `f44b336 Cover child zombie generation gate`
- `c2dd3b5 Cover zombie faction world state`
- `b7d560e Cover incident infection and ticking budget`

Source checks:
- Zombieland: `Source/ZombieIncidents.cs`, `Source/TickManager.cs`, `Source/ZombieWeather.cs`, `Source/Alerts.cs`, incident/letter/faction defs.
- RimWorld 1.6 decompiler: `IncidentWorker.TryExecute`, letter stack API, map edge spawn helpers, room/fog APIs, weather/GUI tooltip path.

Fixture:
- Generated save: `ZL_Incident_Threat_base.rws`.

Setup:
- Configure deterministic incident interval and small zombie cap.
- Prepare cells for edge spawn, soft ground spawn, dark spawn, fogged-room spawn, and blocker replacement.

Runtime:
- Step enough real ticks for a scheduled incident.
- Force each spawn mode if waiting would be wasteful, but still run one natural scheduled incident.
- Toggle dynamic threat and zero-threat death settings.
- Save before forecast changes, reload, verify forecast and scheduling state.

Assertions:
- Letter text/type and target are correct.
- Spawn location respects selected mode and blocked areas.
- Special mix and child gates respect settings.
- Zombie cap is not exceeded.
- Threat forecast and tooltip match current settings.
- Zero-threat behavior kills or preserves zombies according to settings.

Artifacts:
- Incident state dump.
- Letter list.
- Screenshot of forecast tooltip if UI path is used.
- Deduped log summary.

Completion:
- Covered when one natural incident plus forced mode checks pass and persist through reload.

Current runtime evidence:
- Partial coverage added 2026-05-30 with reusable `zombieland/incident_threat_state`, a scenario-level setup/read/action tool. RimWorld 1.6 decompiler checks confirmed the incident hook targets before relying on runtime behavior: `IncidentWorker.TryExecute`, `IncidentWorker_Raid.TryExecuteWorker`, and `PawnGroupKindWorker.GeneratePawns`.
- From `EMPTY`, `setupFixture:true actionMode:read` created three capable armed colonists and prepared a deterministic all-over-map spawn field at `(100,124)`. The spawn field is intentional fixture setup: the baseline `EMPTY` map is reachable but all `SterileTile`, and all-over-map validation rejects removable top-layer/floor terrain while edge spawning does not. After the fixture edit, diagnostics reported 2,401 all-over valid spawn cells and 4,365 valid event spots. The fixture was saved as `ZL_Incident_Threat_base.rws`.
- From a fresh `ZL_Incident_Threat_base.rws` reload, `actionMode:all` returned `success=true`. The alert-wave matrix spawned four normal zombies from edges and produced one `ThreatSmall` `Zombie attack` letter, then spawned four normal zombies in all-over-map mode and produced one `ThreatSmall` `Direct attack of zombies` letter. Explicit event spawning matched all requested types: suicide bomber, toxic splasher, tanky operator, miner, electrifier, albino, dark slimer, healer, and normal.
- The same run covered the incident hook/faction slice: generated human incident pawns received a zombie bite, animals did not, and the `IncidentWorker.TryExecute` postfix reduced incident-generated bites to harmless; zombie-faction normal pawn generation returned a spawned Zombieland `Zombie`, while blob and spitter pawn kinds stayed on the vanilla generation path. The threat forecast contract toggled dynamic threat on/off and confirmed `ZombieWeather` forecast labels and disabled-threat fallback. The scheduler-driven wave forced the real scheduler calculation through `ZombiesRising.ZombiesForNewIncident`, executed `SpawnEventProcess`, spawned 14 zombies without exceeding the computed cap, and produced one `ThreatSmall` `Zombie attack` letter. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Spawn-mode and zero-threat coverage was added to the same rollup later on 2026-05-30. After rebuilding with `scripts/build-quiet.sh` at 0 warnings and 0 errors, a fresh `ZL_Incident_Threat_base.rws` reload passed `zombieland/incident_threat_state actionMode:all`. Ambient population checks forced `TickManager.IncreaseZombiePopulation` with `populationSpawnCounter=-1`: `AllTheTime/AllOverTheMap` spawned one zombie on soil with no removable top layer, `AllTheTime/FromTheEdges` spawned one zombie in an edge-reachable region, `WhenDark/AllOverTheMap` spawned one zombie on a roofed `PsychGlow.Dark` soil cell, and `InEventsOnly` spawned none. Fog checks in the same scenario covered fogged-door player entry spawning two room zombies while hostile entry did not, fog-blocker deconstruction spawning two room zombies and unfogging the room, and `DestroyMode.WillReplace` blocker destruction spawning no zombies and preserving fog. The scenario wrapper cleans temporary fog fixtures before final readback. Zero-threat checks ran the actual `Zombie.CustomTick` branch at threat level `0`: with `zombiesDieOnZeroThreat=true`, the sampled zombie took `405` injury severity and died at tick sample `548`; with the setting disabled, the comparison zombie took no injury and survived. The final rollup log check again returned no warning-or-higher entries.
- Raid-worker patch coverage was added to the same tool after RimWorld 1.6 decompiler inspection of `IncidentWorker_Raid.TryExecuteWorker(IncidentParms)`: the vanilla worker still generates raid info, sends the standard raid letter, makes lords, teaches weapon/shield concepts, and returns `true`. The bridge scenario temporarily probes `ZombiesRising.TryExecute` and then invokes the real installed `IncidentWorker_Raid.TryExecuteWorker` method with a zombie-faction `IncidentParms`. `EdgeWalkIn` observed `spawnHowType=FromTheEdges` inside `ZombiesRising.TryExecute`, returned `false` from the raid worker, and restored the sentinel `AllOverTheMap` mode afterward. `CenterDrop` observed `spawnHowType=AllOverTheMap`, returned `false`, and restored the sentinel `FromTheEdges` mode afterward. The probe prevents coroutine side effects, so the check ended with `newZombieCount=0` and `newLetterCount=0` while proving the call route and restore behavior.
- Forecast UI coverage was added to the same tool after extracting the actual `GlobalControlsUtility.DoDate` patch's forecast formatter and geometry helpers. `actionMode:forecastUi` spawns one temporary zombie to force the zombie-count readout, enables dynamic threat, computes the same right-aligned readout rectangles used by the patch, and verifies the vanilla date rect, `1 Zombies` rect, `0% threat level` rect, and 720x320 tooltip rect are all on-screen and non-overlapping. It also opens a preview window that renders `ZombieWeather.GenerateTooltipDrawer`; `rimworld/take_screenshot` captured that window at `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_forecast_tooltip_preview__clip.png`, and visual inspection confirmed the tooltip renders the `Threat Forecast`, `Next 14 days`, and `Next 4 quadrums` graph sections.
- Current H coverage: scheduler-driven wave execution, cap accounting, edge/all-over alert letters, special-type event requests, incident infection reduction, zombie-faction pawn generation, ambient spawn modes, fog-room spawn hooks, blocker replacement suppression, zero-threat branches, raid worker patch boundary, and forecast tooltip UI/visual rendering. `zombieland/incident_threat_state actionMode:all` includes the non-window forecast UI geometry check and returned `success=true`; `rimbridge/list_logs minimumLevel=warning` returned no entries.

## S-Hostility-Area-Workflow

Goal: prove the player-facing dangerous-area workflow and the surrounding avoidance/hostility behavior that is too broad for the older narrow contracts.

Prior evidence:
- `f651efa Add colonist avoidance interrupt smoke`
- `0416696 Add workgiver avoidance smoke`
- `0c6dacb Add avoid grid door and danger smoke`
- `d7f1a04 Cover zombie hostility rules`
- `6beb859 Cover zombie target cache filtering`
- `e4f9d64 Cover zombie area risk classifications`
- `a0302d6 Cover colonist outside area risk`

Source checks:
- Zombieland: `Source/Patches_Hostility.cs`, `Source/ZombieAreaManager.cs`, `Source/ZombieAvoider.cs`, `Source/WorkGivers.cs`, and the avoidance/workgiver patches in `Source/Patches.cs`.
- RimWorld 1.6 decompiler rows are tracked in `TEST_PATCH_AUDIT.md` under `hostility-static`, `area-manager-static`, and `patches-avoidance-doors-workgivers`.

Fixture:
- Generated save: `ZL_Area_Workflow_base.rws`.
- Area labels: `ZL_Area_Ignore`, `ZL_Area_ColonistInside`, `ZL_Area_ColonistOutside`, `ZL_Area_ZombieInside`, and `ZL_Area_ZombieOutside`.

Runtime:
- Use `zombieland/area_workflow_state` as the scenario-level setup/read tool.
- Open the real `RimWorld.Dialog_ManageAreas` when visual proof is needed, then capture it with `rimworld/take_screenshot`.
- Use `actionMode:behavior` for the combined area-assignment, dangerous-area warning, avoid-grid danger, drafted-exemption, player-forced exemption, and undrafted work-interruption proof.
- Use `actionMode:targeting` for the mixed player/enemy/animal target-choice proof. This action builds and destroys a transient combat fixture from the reusable area save; do not save the spawned target pawns as a persistent colony state.

Assertions:
- Additional allowed areas can be created and custom area order is not overwritten by `SortAreas()`.
- The manage-area constructor resets selected area, selected index, and scroll state before the scenario selects the target area.
- The real patched manage-area dialog renders Zombieland area rows, color-coded risk labels, selected-area content, and the `Show Zombie Risk` mode button.
- Area risk modes and colors persist through save-load.

Artifacts:
- Save file: `ZL_Area_Workflow_base.rws`.
- Save file: `ZL_Area_Workflow_behavior.rws`.
- Screenshot: `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_area_workflow_manage_areas__clip.png`.
- Screenshot: `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_area_workflow_danger_overlay__clip.png`.
- Deduped log summary.

Completion:
- Covered when the area-management workflow and the mixed-faction combat extensions both pass from reusable saves and produce no warning-or-higher logs.

Current runtime evidence:
- Added 2026-05-30 with `zombieland/area_workflow_state`. From `EMPTY`, `setupFixture:true openManageDialog:true` returned `success=true`: `AreaManager.CanMakeNewAllowed()` and `TryMakeNewAllowed` were true, `SortAreas()` preserved the custom order, the `Dialog_ManageAreas` constructor reset selected/scroll state, all five `ZL_Area_*` fixture areas had four active cells, and each non-ignore area reported the expected `AreaRiskMode` and label color.
- The tool opened the real `RimWorld.Dialog_ManageAreas`; `rimworld/get_screen_targets` reported it as the focused top dialog, and `rimworld/take_screenshot` captured `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_area_workflow_manage_areas__clip.png`. Visual inspection confirmed the Zombieland area list, selected `ZL_Area_ZombieInside`, the orange selected row, color sliders, and `Show Zombie Risk` button reading `Zombie inside`.
- The fixture was saved as `ZL_Area_Workflow_base.rws`, reloaded, then `zombieland/area_workflow_state setupFixture:false openManageDialog:false` returned `success=true` with all five areas, modes, colors, and active-cell counts intact. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Behavior coverage was added after RimWorld 1.6 decompiler checks confirmed the live shapes of `JobDriver.DriverTick`, `DangerUtility.GetDangerFor`, and `JobGiver_ConfigurableHostilityResponse.TryGetAttackNearbyEnemyJob`. From `ZL_Area_Workflow_base.rws`, `zombieland/area_workflow_state setupFixture:true actionMode:behavior` returned `success=true`: the colonist was assigned to `ZL_Area_ColonistInside`, the normal tracking zombie was in `ZL_Area_ZombieInside`, `ZombieAreaManager.pawnsInDanger` contained both entries, the avoid-grid cost at the colonist was `972`, normal danger was `Deadly`, player-forced danger stayed non-Deadly, drafted `Wait_Combat` remained non-flee after 5 ticks, undrafted `Wait_Combat` became a player-forced `Flee` on tick 1 with destination `(96,94)` outside avoid danger, and a non-forced undrafted `AttackMelee` job in avoid danger also became player-forced `Flee` on tick 1 with a safe destination.
- The behavior run produced the live danger overlay screenshot `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_area_workflow_danger_overlay__clip.png`, saved the extended fixture as `ZL_Area_Workflow_behavior.rws`, reloaded that save, and reran `actionMode:behavior` successfully. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Mixed-targeting coverage was added to the same tool after decompiler checks confirmed the RimWorld 1.6 shapes of `Verse.AI.AttackTargetFinder.BestAttackTarget`, private `GetAvailableShootingTargetsByScore`, private `GetShootingTargetScore`, and both private friendly-fire score offset methods. From `ZL_Area_Workflow_behavior.rws`, `zombieland/area_workflow_state setupFixture:true actionMode:targeting` returned `success=true` and cleaned up all spawned attackers/targets before returning. The transient fixture created an armed player colonist, armed hostile colonist, player warg, normal zombie, roped zombie, confused zombie, active electrifier, albino, suicide bomber, spitter, and blob.
- The targeting run proved: player rifle filtering kept normal/albino/suicide/spitter/blob candidates while excluding roped, confused, and active electric zombies; enemy filtering returned no candidates with `enemiesAttackZombies=false`; with it enabled, enemy filtering allowed ordinary normal/roped/suicide targets and excluded confused, albino, spitter, blob, and active electric targets for an unsuitable ranged weapon; public `BestAttackTarget` returned null for roped/confused player-specific validators, null for enemy normal while enemies were disabled, normal while enabled, null for enemy spitter/blob/electric validators, null for animals while `animalsAttackZombies=false`, and normal while enabled. The shooting-score patch produced `normalScore=100.487808` and `suicideScore=112.218658`. Friendly-fire patch evidence confirmed both target methods have `net.pardeike.zombieland` owners and the helper removes zombies while preserving a non-zombie pawn.
- The first live targeting pass exposed a real bug in `AttackTargetFinder_BestAttackTarget_Patch`: the friendly fallback returned a cached zombie before the caller validator was re-applied. The patch now updates `thing` after fallback selection and lets the existing final validator guard null invalid results. After that fix and the violence-capable shooter generation guard, `scripts/build-quiet.sh` passed with 0 warnings/0 errors, the final live targeting run passed, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Direct hostility and target-cache coverage were folded into the same targeting action after decompiling `AttackTargetsCache.RegisterTarget`, `DeregisterTarget`, `TargetsHostileToColony`, `Notify_ThingSpawned`, `Notify_ThingDespawned`, and `TargetsHostileToFaction`. The final live run proved player `HostileTo(zombie)=true`, enemy and non-colony animal `HostileTo(zombie)` obey `enemiesAttackZombies`/`animalsAttackZombies`, infecting enemy pawns are not hostile to zombies, spitter/blob are non-hostile to a non-player faction, zombie active-threat checks return false for player/null/enemy-disabled settings, true for enemy faction with `AttackMode.Everything`, and false for `OnlyColonists`. The replacement `TargetsHostileToColony` cache contained the real hostile human before destruction, excluded normal/spitter/blob zombies, and removed the hostile after destruction.
- The first target-cache pass exposed a real stale-entry bug: `AttackTargetsCache_DeregisterTarget_Patch` removed from the replacement cache too late for the despawn path, and the replacement cache did not defend against destroyed entries at read time. The patch now removes on `DeregisterTarget` prefix and `PlayerHostilesWithoutZombies` purges null, destroyed, despawned, wrong-map, and no-longer-hostile entries before returning the cache. After that fix, `scripts/build-quiet.sh` passed with 0 warnings/0 errors, `actionMode:targeting` returned `success=true`, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Drafted wait auto-attack coverage was folded into `actionMode:targeting` after decompiling `JobDriver_Wait.CheckForAutoAttack`. The live run confirmed the Harmony owner `net.pardeike.zombieland` is installed on `CheckForAutoAttack`; active adjacent normal zombies and player-faction spitters were damaged by a real `Wait_Combat` job, player-faction blobs and ordinary hostile pawns started real melee attacks, and roped/confused adjacent normal zombies were neither attacked nor damaged through 900 ticks. The first fixture pass used `GenTicks.TicksGame` for `paralyzedUntil` and falsely let the confused zombie become attackable once real ticks advanced; the fixture now uses `GenTicks.TicksAbs`, matching `ZombieStateHandler` and `Zombie.IsRopedOrConfused`. The harness now generates relation-free/no-random-gear test pawns, equips the wait auto-attack actor with a materialized melee weapon, maintains the intended roped state during the roped negative probe, and treats blob/ordinary-hostile positives as attack-start checks because adjacent melee damage can be masked by stun/retaliation while still proving those targets were not filtered out. After that correction, `scripts/build-quiet.sh` passed with 0 warnings/0 errors, `actionMode:targeting` returned `success=true`, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Non-pawn turret coverage was added to the same targeting action after decompiling `AttackTargetFinder.BestAttackTarget`, `AttackTargetsCache.GetPotentialTargetsFor`, `Building_TurretGun.CurrentEffectiveVerb`, and `Building_TurretGun.AttackVerb`. The live run used a real materialized `Turret_MiniTurret`/`Building_TurretGun`, confirmed `net.pardeike.zombieland` owns the installed `BestAttackTarget` patch, and proved the turret selected normal, spitter, and blob targets while rejecting roped, confused, and active-electric zombies when its bullet verb could not harm electric zombies. The general search assertion accepts any returned target that is allowed by the same state rules, because a real cache can also contain earlier valid zombies from the larger mixed-targeting fixture. Final `scripts/build-quiet.sh` passed with 0 warnings/0 errors, `actionMode:targeting` returned `success=true`, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Friendly/mech/thing available-target branch coverage was folded into the same targeting action after decompiling `AttackTargetFinder.GetAvailableShootingTargetsByScore`. The branch probes use the existing prefix/postfix invocation helper against real spawned friendly human, friendly mech, player mech, player turret, and hostile turret searchers plus the mixed zombie target set. The first live pass exposed a real bug: `removeAllZombies` removed ordinary `Zombie` instances but let `ZombieSpitter` and `ZombieBlob` survive for a player mech under `AttackMode.OnlyHumans`. The patch now removes all three Zombieland pawn target types when `removeAllZombies` is active and makes `removeSpitter` remove both spitter and blob. The final live run proved all branch cases passed, including empty player-mech `OnlyHumans`, allowed player-mech `Everything`, player thing filtering, enemy thing disabled/enabled filtering, and friendly human/mech filtering. `scripts/build-quiet.sh` passed with 0 warnings/0 errors and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Tar-smoke melee targeting was added to `actionMode:targeting` after checking the `Verb.CanHitTargetFrom` patch branch. The live run spawned an adjacent player melee actor and hostile human target, verified the melee verb could hit before smoke, spawned real `TarSmoke` on the target cell, verified `gasAtTargetAfter=TarSmoke`, and proved the same melee verb still returned `CanHitTargetFrom=true`. This complements the earlier focused ranged tar-smoke tools (`zombieland/tar_smoke_blocks_ranged_targeting` and `zombieland/tar_smoke_blocks_human_ranged_targeting`) by covering the source branch that deliberately returns early for melee attacks. The same final run had `actionSucceeded=true`, `scripts/build-quiet.sh` passed with 0 warnings/0 errors, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Tar-smoke standard aim chance was added to the same targeting action after decompiling `ShotReport.HitReportFor`, `CoverUtility.CalculateCoverGiverSet`, and `ShotReport.AimOnTargetChance_StandardTarget`. A real rifle shooter/hostile target pair produced `AimOnTargetChance_StandardTarget=0.612535536` before smoke; real `TarSmoke` on the target cell made the standard aim chance `0` and made ranged `CanHitTargetFrom=false`. The cover-list branch is covered directly with a synthetic `ShotReport.covers` entry containing a `TarSmoke` `CoverInfo`, which also returned `0`; this is intentionally recorded separately because RimWorld 1.6 cover discovery uses `GetCover`, while `TarSmoke` is a gas. The final `actionMode:targeting` run returned `success=true`, `scripts/build-quiet.sh` passed with 0 warnings/0 errors, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Downed-zombie combat continuation was added to `actionMode:targeting` after checking the two `Pawn.Downed` replacement patch shapes. The bridge records runtime Harmony targets by patch class: `Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch` owned `Verse.AI.<>c__DisplayClass6_0::<FollowAndMeleeAttack>b__0(System.Int32 delta)`, while `JobDriver_AttackStatic_TickAction_Patch` owned seven installed methods: the two `JobDriver_AttackStatic` generated delegates, `Toils_Jump.<JumpIfTargetDowned>b__0`, `TargetingParameters.CanTarget`, `VerbUtility.AllowAdjacentShot`, `Stance_Warmup.StanceTick`, and `Verb_Shoot.WarmupComplete`. The decompiled installed `Pawn_MindState.MeleeThreatStillThreat` no longer reads `Pawn.Downed`, so it correctly did not appear in the runtime patch list. The live fixture forced `doubleTapRequired=false`, spawned health/public-downed normal zombies, then proved a drafted melee colonist kept `AttackMelee` with `job.killIncappedTarget=false` and damaged the zombie by tick 10, and a drafted rifle colonist kept `AttackStatic` with `job.killIncappedTarget=false` and entered firing cooldown by tick 129. `scripts/build-quiet.sh` passed with 0 warnings/0 errors, `actionMode:targeting` returned `success=true`, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.

## S-External-Mod-Audit

Goal: prove optional integration code is either compatible with installed 1.6 mods or explicitly marked unavailable with source/decompiler evidence.

Prior evidence:
- `560065a Cover RimConnect zombie actions`
- `1143ea7 Cover RimConnect super drop`

Source checks:
- Zombieland: `Source/CETools.cs`, `Source/RimConnectSupport.cs`, `Source/CameraPlusSupport.cs`, `Source/DubsTools.cs`, `Source/SoSTools.cs`, `Source/VehicleTools.cs`.
- RimWorld 1.6 decompiler plus optional mod assemblies where installed.

Fixture:
- No single save. Use one small generated save per installed integration if runtime testing is possible.

Runtime matrix:
- RimConnect: action registration, settings UI injection, spawn/kill/rage/super drop, notification.
- Combat Extended: armor reroute, projectile distance, armor utility, ammo user interactions.
- CameraPlus: zombie marker/color hooks.
- Dubs Bad Hygiene: draw-name compatibility hook.
- Save Our Ship 2: space zombie generation, hologram exclusion, floating zombie mesh altitude.
- Vehicle Framework: vehicle direct-flee avoidance and move speed/timestamp behavior.

Assertions:
- If mod assembly is installed: target types/methods resolve and runtime smoke passes.
- If mod assembly is not installed: integration cleanly disables without load errors.
- No integration patch creates hard dependency load errors.

Artifacts:
- Installed/unavailable matrix.
- Decompiler target member ids for each installed integration.
- Runtime log summary for each installed integration.

Completion:
- Covered when every optional integration is either runtime-tested or explicitly classified unavailable with source audit evidence.

## S-Uninstall-Hygiene

Goal: prove the Zombieland removal flow creates a copy save that can load without Zombieland and has no dangling zombie defs, components, hediffs, thoughts, filters, factions, tales, or battle-log references.

Prior evidence:
- No specific post-baseline coverage found in commit subjects.

Source checks:
- Zombieland: `Source/ZombieRemover.cs`, `Source/Dialog_SaveThenUninstall.cs`, `Source/ContaminationSerializer.cs`, settings/world/map component serializers.
- RimWorld 1.6 decompiler: `RimWorld.Dialog_SaveFileList`, `GameDataSaveLoader.SaveGame`, `LoadedModManager.RunningMods`, faction/world pawn containers, filters, tales, battle log.
- Decompiler fact already checked: `Dialog_SaveFileList` remains an abstract save-file dialog with protected `DoFileInteraction(string)`, protected `ShouldDoTypeInField`, and public `PostClose()`, matching the override pattern in `Dialog_SaveThenUninstall`.

Fixture:
- Generated disposable save: `ZL_Uninstall_Hygiene_source.rws`.
- Output copy: `ZL_Uninstall_Hygiene_no_zl.rws`.

Setup:
- Create a deliberately dirty save: zombies, special zombies, zombie corpses, zombie faction, contamination, bitten/infected pawns, zombie thoughts/memories, battle log entries, tales, filters containing zombie extract/serum/corpses, active map/world components, and optional Zombieland items/buildings.

Runtime:
- Run save-then-uninstall on the copy, not on source.
- Disable Zombieland or inspect the save XML/type refs before attempting load if disabling mods is not automatable.
- Load the output save without Zombieland if possible.

Assertions:
- No remaining `ZombieLand`, zombie defs, Zombieland component class names, zombie faction, zombie hediffs, zombie thoughts, zombie battle/tale refs, or filters requiring removed defs.
- Player pawns retain safe non-zombie state.
- Save loads without unresolved cross-reference/type errors.
- Source save remains intact.

Artifacts:
- Source and output save names.
- XML/type-ref scan summary.
- Load log summary without Zombieland if runtime path is possible.

Completion:
- Covered when a dirty save copy loads or scans clean without Zombieland references and the source save is unchanged.

## S-Dense-Performance

Goal: bound runtime cost after correctness scenarios stabilize.

Prior evidence:
- `c6f2b87 Optimize zombie tick sampling`
- `4e90c65 Trim zombie ticking hot path`
- `394e740 Optimize chainsaw ticking`

Source checks:
- Zombieland: `Source/TickManager.cs`, `Source/ZombieStateHandler.cs`, `Source/PheromoneGrid.cs`, `Source/ZombieWanderer.cs`, `Source/Chainsaw.cs`, contamination tick paths.
- RimWorld 1.6 decompiler: tick manager and map component tick dispatch if measured results look suspicious.

Fixture:
- Generated save: `ZL_Dense_Performance_base.rws`.

Setup:
- Use a fixed-size map with deterministic colonists, weapons, buildings, and a dense but bounded zombie count.
- Include at least one chainsaw, one shocker/thumper if they are expected to be common, and a small contaminated area.

Runtime:
- Warm up for a fixed number of ticks.
- Run paused single-tick stepping and normal-speed stepping for fixed windows.
- Capture tick duration, frame duration if available, zombie count, active jobs, and log summary.

Assertions:
- No runaway allocations/log spam.
- Tick duration remains within an agreed local baseline for the machine.
- Zombie count and job state remain stable enough to compare runs.

Artifacts:
- Timing sample JSON or compact text.
- Save name.
- Deduped log summary.

Completion:
- Covered when there is a repeatable local baseline after correctness scenarios pass.
