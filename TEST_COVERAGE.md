# Zombieland 1.6 Test Coverage Matrix

This file defines the test surface for the RimWorld 1.6 port from the mod code outward. It is not a work log. Update it only when a test surface is added, removed, merged, or proven covered by durable evidence. Detailed scenario definitions live in `TEST_SCENARIOS.md`.

## Method

Use three axes for every test:

1. Code surface: source files, defs, Harmony targets, components, jobs, and player UI touched by the behavior.
2. Player behavior: what a real colony player can observe or break.
3. Evidence tier: source/decompiler reasoning, bridge contract, scenario save, visual/log evidence, save-load persistence, or performance smoke.

Prefer reusable scenario fixtures over one-off bridge tools. Existing bridge contracts are evidence for narrow behavior, but they do not automatically prove player-facing flows, multi-system interactions, save-load behavior, or visual quality.

Before adding a new test, search the commit history since `a0de6c309ab789a488a6bc106d281b5d0ca79020` and this matrix. A commit named `Cover ...`, `Add ... bridge smoke`, `Recheck ...`, or `Record ... evidence` counts as prior evidence only for the behavior its changed files actually exercise.

Useful history query:

```bash
git log --reverse --format='%h %ad %s' --date=short a0de6c309ab789a488a6bc106d281b5d0ca79020..HEAD
```

Use `scripts/coverage-inventory.sh` for a compact current-state inventory of source files, Harmony patch density, bridge tool density, gameplay classes, serialization/tick/UI hooks, and evidence commit subjects. Use `scripts/coverage-inventory.sh --patch-groups` for the TSV patch-group inventory used by `S-Source-Patch-Audit`; its `targeting` column distinguishes concrete `static` targets, `dynamic` `TargetMethod(s)` lookups, and class-level `base` rows that only provide a Harmony type context. Use `scripts/coverage-inventory.sh --dynamic-patches` to split dynamic patch targets into closure search, multi-target, typed-reflection, external string, external type-name, and owner buckets before doing decompiler or optional-mod checks. Use `scripts/coverage-inventory.sh --static-summary` to group static rows into scenario-owned audit slices. Record target-level dispositions in `TEST_PATCH_AUDIT.md` when the detail would make this matrix noisy.

## Evidence Tiers

| Tier | Meaning | Use when |
| --- | --- | --- |
| Source | Current Zombieland and RimWorld 1.6 code prove the target shape and expected branch behavior. | Patch target signatures, branch gates, serializer formats, settings calculations. |
| Contract | A focused RimBridge/Zombieland tool verifies a narrow behavior. | Pure rules, small deterministic fixtures, regression guards. |
| Scenario | A named save plus generic setup/step/read operations verifies a player-facing flow. | Multi-system behavior, job flows, incidents, map state, save-load, settings effects. |
| Visual | Screenshot or render-state evidence verifies what a player sees. | Pawn rendering, overlays, UI, motes, projectiles, gas/effects. |
| Log | Deduped RimWorld log proves no new load/runtime errors during the scenario. | Cold start, save load, incident waves, dense tests. |
| Performance | A bounded dense-map smoke measures tick/frame cost or allocation-sensitive behavior. | Zombie tick loops, pathing, chainsaw, contamination propagation. |

## Coverage Clusters

### A. Startup, Load, Defs, Harmony

Code surface: `About`, `LoadFolders.xml`, `Defs`, `Source/Main.cs`, `Source/Assets.cs`, `Source/Patches.cs`, `Source/CustomPawnState.cs`, `Source/ZombieRenderCompat.cs`.

Already evidenced:
- `90f62d4 Establish RimWorld 1.6 port baseline`
- `787156f Fix zombie render tree initialization on load`
- `13f3b89 Restore Zombieland menu texture`
- `12510be Use explicit suicide bomber overlay`
- `0892412 Record visual lineup verification`

Required tests:
- Cold mod load: start RimWorld 1.6 with Zombieland enabled, verify no load errors, custom defs resolve, Harmony patches apply, main menu settings category opens.
- Load existing 1.6 scenario save: verify zombies on map have render trees, no pawn render async errors, no missing texture/material errors.
- Patch target audit: for each `[HarmonyPatch]` group, confirm RimWorld 1.6 target signatures with the decompiler before relying on runtime smoke.

Current gap:
- There is no current whole-mod patch target inventory or automated stale-patch audit. The many individual contract commits reduce risk but do not prove every patch target is still semantically correct.

Current source-audit findings:
- Current inventory has 256 static patch rows, 40 dynamic patch rows, and 4 class-level base rows. Dynamic rows split into 18 contamination, 13 core, 7 optional-integration, and 2 specialized rows. The static scanner ignores commented-out Harmony blocks so disabled historical patches do not become test obligations.
- `Source/CustomPawnState.cs:16` `Pawn_DrawTracker_Constructor_Patch` resolves in RimWorld 1.6 to `Verse.Pawn_DrawTracker..ctor(Pawn pawn)` (`88df42e0e943404ba42043aebd37585c:06002675:M`).
- `Source/Patches_Hostility.cs:450` `AttackTargetFinder_FriendlyFire_Patch` resolves in RimWorld 1.6 to private static `FriendlyFireBlastRadiusTargetScoreOffset(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)` (`88df42e0e943404ba42043aebd37585c:06006558:M`) and `FriendlyFireConeTargetScoreOffset(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)` (`88df42e0e943404ba42043aebd37585c:06006559:M`).
- `Source/Patches.cs:2973` `PawnGraphicSet_HeadMatAt_Patch` targets `Verse.PawnGraphicSet:HeadMatAt`, which exists in RimWorld 1.4 but not RimWorld 1.6. RimWorld 1.6 `Verse.PawnRenderNode_Head.GraphicFor(Pawn pawn)` (`88df42e0e943404ba42043aebd37585c:060027BD:M`) returns `null` when `pawn.health.hediffSet.HasHead` is false, and Zombieland already patches `PawnRenderNode_Head.GraphicFor`; visual coverage still needs to prove no head-stump regression before the inert old patch is removed or ignored.
- `Source/Patches.cs:1095` `JobDriver_AttackStatic_MakeNewToils_b__1_Patch` now selects the `JobDriver_AttackStatic.MakeNewToils()` tick delegate semantically by its generated one-`int` parameter signature. Decompiler comparison showed RimWorld 1.4 used `<MakeNewToils>b__1` for the tick delegate, while installed RimWorld 1.6 moved that behavior to `<MakeNewToils>b__2(int delta)` (`88df42e0e943404ba42043aebd37585c:06005E2E:M`). Runtime `S-Defense-Room` evidence confirmed Harmony owner `net.pardeike.zombieland` patched the 1.6 tick delegate and that a zombie left `AttackStatic` one tick after the target door opened.

### B. Settings, UI, Defaults, Persistence

Code surface: `Source/Main.cs`, `Source/ZombieSettings.cs`, `Source/SettingsDialog.cs`, `Source/Dialog_Settings.cs`, `Source/Dialog_AdvancedSettings.cs`, `Source/Dialog_ApparelBlacklist.cs`, `Source/Dialog_BiomeList.cs`, `Source/Dialog_ThumperSettings.cs`, `Source/Dialog_SaveThenUninstall.cs`, `Source/Dialog_ZombieDebugActionMenu.cs`, `Source/ColonistSettings.cs`.

Already evidenced:
- `13f3b89 Restore Zombieland menu texture`
- `dc443f1 Fix map-target debug actions`
- Several behavior contracts indirectly toggle settings for their fixture, but those are not UI persistence tests.
- Live partial `S-Settings-Persistence` evidence: `zombieland/settings_state actionMode:prepare openSettingsDialog:true` from `EMPTY` created a reusable settings fixture with three keyframes at 0, 2, and 5 days, representative fields across spawn/attack/smash/eating/infection/avoidance/visual/contamination/apparel/biome surfaces, and a spawned `ZL_Settings_Colonist` whose `autoAvoidZombies=false`, `autoDoubleTap=false`, and `autoExtractZombieSerum=true` config persisted. The fixture was saved as `ZL_Settings_Persistence_00.rws`, reloaded, and `actionMode:verify openSettingsDialog:true` proved the world `ZombieSettings` component, keyframe ticks, deterministic day 0/1/2/3/6 `CalculateInterpolation` samples, real `Dialog_ModSettings` construction, game settings category, and colonist toggles survived save-load. A follow-up `actionMode:modal` run opened and validated real apparel blacklist, biome blacklist, advanced settings, and thumper settings dialogs; proved apparel invalid-entry cleanup plus real `Apparel_PlateArmor` persistence, real `AridShrubland` biome persistence, temporary advanced constant JSON write/restore, and thumper settings dialog backing fields; saved `ZL_Settings_Persistence_01_modals.rws`; and reloaded it with verification still green. A downstream `actionMode:behavior` run from `ZL_Settings_Persistence_01_modals.rws` reused the persisted colonist config and proved serum extraction accepts non-forced and forced work on a real `ZombieCorpse`, double-tap rejects non-forced but accepts forced work on an infected corpse, and `autoAvoidZombies=false` prevents active avoid danger (`avoidCost=972`, `shouldAvoid=true`) from converting a non-forced `Goto` into `Flee`. A direct `PriorityWork.GetGizmos()` `actionMode:gizmos` run on the same saved map spawned a temporary capable colonist, preserved a vanilla clear-priority command, exposed enabled avoid/extract/double-tap Zombieland commands, and proved invoking those command delegates flips all three `ColonistConfig` booleans from false to true. `rimbridge/list_logs minimumLevel=warning` returned no entries.

Required tests:
- Defaults workflow: open mod settings from main menu, change a representative value in each settings section, save, restart, verify defaults persist.
- New-game workflow: create/load a clean map, verify initial game settings are applied and saved into `ZombieSettings`.
- In-game workflow: open Zombieland menu, change critical settings, verify immediate behavior changes for spawning, attack mode, smash mode, infection, eating, avoidance, custom textures, and contamination.
- Keyframe workflow: add at least two settings keyframes, verify `CalculateInterpolation` behavior at lower/middle/upper game ticks, save-load the keyframes, and verify the UI still represents them.
- Modal workflow: apparel blacklist, biome blacklist, advanced constants, thumper settings, save-then-uninstall confirmation.
- Colonist toggles: auto avoid, auto double-tap, and auto extract per colonist persist across save-load and affect workgivers.

Current gap:
- This remains a large surface, but the core in-game world settings/keyframe/colonist-toggle save-load path, modal construction/backing-state paths, downstream behavior for the three persisted colonist toggles, and direct per-colonist gizmo command presence/actions now have live evidence. Remaining B work is narrower: default-settings restart persistence, actual mouse-driven modal row edits where needed, save-then-uninstall, and proof that UI-edited toggles affect every relevant downstream behavior from a visible UI path.

### C. Core Zombie Lifecycle, Movement, Senses

Code surface: `Source/Zombie.cs`, `Source/ZombieGenerator.cs`, `Source/ZombieStateHandler.cs`, `Source/JobDriver_Stumble.cs`, `Source/JobGiver.cs`, `Source/ZombiePathing.cs`, `Source/PheromoneGrid.cs`, `Source/ZombieWanderer.cs`, `Source/ZombieLeaner.cs`, `Source/ZombieDamageFlasher.cs`, `Source/ZombieCorpse.cs`, `Source/ZombieStains.cs`.

Already evidenced:
- `3981f82 Fix zombie movement with async path requests`
- `f43506a Add path avoidance recalculation smoke`
- `36915af Restore avoid-grid path costs`
- `2bce190 Add zombie door close smoke`
- `635be71 Restore zombie blood filth contract`
- `c5c9775 Suppress zombie combat log association`
- `8d4e084 Cover zombie fire attachment state`
- `7ccd5e4 Verify zombie fire rain vulnerability`
- `1233bb1 Extend burn-longer fire hooks`
- `c6f2b87 Optimize zombie tick sampling`
- `4e90c65 Trim zombie ticking hot path`
- Live `S-Core-Horde-Loop` sub-contract evidence: `zombieland/ambient_temperature_contract` from `EMPTY.rws` proved ordinary human and human corpse `AmbientTemperature` stayed at the vanilla cell temperature (`4.5`), while `Zombie`, `ZombieCorpse`, `ZombieSpitter`, and `ZombieBlob` all reported Zombieland's forced normal temperature (`21`). `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Live patch-boundary evidence: `zombieland/zombie_damage_log_association_suppression` from `EMPTY.rws` proved ordinary human damage still associated with the combat log (`combatTextDelta=1`), while normal zombie, spitter, and blob damage each produced a result hediff without combat-log text (`combatTextDelta=0`). The fixture now cleans up spawned pawns before returning; the rerun had no warning-or-higher log entries.

Required tests:
- Horde scenario: from an empty controlled save, spawn a mixed group and run real ticks through sensing, tracking, rage, door contact, wall contact, wandering fallback, and cleanup.
- Save-load lifecycle: save with active zombies in different states, reload, verify draw, jobs, path requests, fire state, corpse state, and tick manager registration.
- Pheromone/senses scenario: verify hearing/smell/tracking behavior through walls and fading trails over real time.
- Dense performance smoke: bounded count of zombies, paused-step and normal-speed run, capture tick budget and log summary.

Current gap:
- Many core primitives are covered, including ambient temperature normalization for Zombieland pawns/corpses versus ordinary vanilla things, but there is no single scenario proving the emergent zombie loop over time after save-load.

### D. Special Zombies

Code surface: `Source/Zombie.cs`, `Source/ZombieSpitter.cs`, `Source/ZombieBall.cs`, `Source/ZombieBlob.cs`, `Source/ZombieBlobRenderer.cs`, `Source/JobDriver_Spitter.cs`, `Source/JobDriver_Blob.cs`, `Source/JobDriver_Sabotage.cs`, `Source/TarSmoke.cs`, `Source/TarSlime.cs`, `Source/BombVest.cs`, `Source/OverlayDrawer.cs`.

Already evidenced:
- `2325c82 Add suicide bomber bridge smoke`
- `860832a Add toxic splasher bridge smoke`
- `81e6c2b Add dark slimer bridge smoke`
- `d6d2870 Add healer bridge smoke`
- `3daf2d5 Add electrifier bridge smoke`
- `b0d97f5 Add miner bridge smoke`
- `5ba1b80 Add tanky bridge smoke`
- `9638321 Add albino bridge smoke`
- `04c6412 Add spitter projectile bridge smoke`
- `58350bc Add zombie ball impact bridge smoke`
- `c181649 Add blob spawn bridge smoke`
- `aeeac24 Fix blob save load rendering`
- `b95123c Add tanky smash bridge smoke`
- `4d723cb Add miner job bridge smoke`
- `8c75244 Add healer tick bridge smoke`
- `1c0e9a4 Add albino scream bridge smoke`
- `1ac0117 Add electrifier active bridge smoke`
- `1bedcf1 Add albino sabotage hack bridge smoke`
- `dd90b2f Cover active electrifier attack verbs`
- `12cb6a2 Cover electrifier bullet absorption`
- `f3a44b2 Cover electrifier melee shock`
- `62343fa Cover albino melee bite filtering`
- `68ebd8c Cover suicide bomber countdown`
- `cd0e93b Cover miner and sticky goo contracts`
- `0146ec7 Cover albino special contracts`
- `5a22ef0 Recheck special zombie bridge contracts`
- Live patch-boundary evidence: `zombieland/zombie_skin_color_contract` from `EMPTY.rws` proved an ordinary human keeps a manually set `Pawn_StoryTracker.SkinColorBase` while a naturally generated `Zombie` returns `Color.white`. Spawned `ZombieSpitter` and `ZombieBlob` lacked natural story trackers in the fixture, so the contract attached temporary `Pawn_StoryTracker` instances for the probe and verified both patch type guards also return `Color.white`. Logs at warning level were empty.
- Live patch-boundary evidence: `zombieland/zombie_gene_rejection_contract` from `EMPTY.rws` proved `Pawn_GeneTracker.AddGene(GeneDef, bool)` and private `Pawn_GeneTracker.AddGene(Gene, bool)` still reject Zombieland pawn types. In the active Core-only fixture (`biotechActive=false`), temporary in-memory gene definitions let the human control accept both overloads and grow from 2 to 4 genes, while natural `Zombie` stayed at 0 genes and temporary-tracker `ZombieSpitter`/`ZombieBlob` stayed at 0 genes with null AddGene results. Logs at warning level were empty.

Required tests:
- Special-zombie gauntlet scenario: spawn one of each special type, run movement, target acquisition, special action, damage/death effect, and cleanup in one save.
- Visual gauntlet scenario: compare alive, active-effect, corpse/death-effect visuals after real tick stepping and after save-load.
- Cross-interaction scenario: specials in the same room with colonists, animals, raiders, doors, walls, turrets, fire, tar smoke, and powered buildings.

Current gap:
- The contract coverage is deep, and the skin-color/gene patch boundaries now have narrow runtime evidence. Missing proof is mostly integration and visuals: multiple specials interacting with the same live map and surviving save-load with correct visuals. A full Biotech DLC workflow remains additive because the active runtime fixture is Core-only.

### E. Infection, Corpses, Serum, Medical

Code surface: `Source/Hediff_Injury_ZombieBite.cs`, `Source/Hediff_ZombieInfection.cs`, `Source/HediffComp_Zombie_Infecter.cs`, `Source/HediffComp_Zombie_TendDuration.cs`, `Source/Recipe_CureZombieInfection.cs`, `Source/JobDriver_DoubleTap.cs`, `Source/JobDriver_ExtractZombieSerum.cs`, `Source/JobDriver_RopeZombie.cs`, `Source/WorkGivers.cs`, `Source/ZombieCorpse.cs`, `Source/ZombieSerumFilterWorker.cs`, `Source/Alerts.cs`.

Already evidenced:
- `623822f Add corpse conversion bridge smoke`
- `fd1c12f Fix double tap corpse job for RimWorld 1.6`
- `d637d99 Add zombie serum extraction bridge smoke`
- `2fd1969 Add cure infection recipe bridge smoke`
- `2cf6be8 Add rope zombie bridge smoke`
- `89a3c45 Add zombie extract filter smoke`
- `9c46bc0 Add zombie corpse management smoke`
- `bac414e Cover zombie corpse eating`
- `7127f37 Split bridge eating contracts`
- `0235865 Cover non-flesh eating rejection`
- Live partial `S-Infection-Medical` evidence: `zombieland/infection_medical_state` from `EMPTY.rws` covered the medical patch cluster in one runtime pass. Hidden, infectable, and infecting zombie bites returned `CanHealNaturally=false`; harmless bites, an ordinary `Cut`, and an animal zombie bite with infection state `None` returned true. After severity was forced to zero, hidden/infectable/infecting bites returned `ShouldRemove=false`, while harmless bite and ordinary cut returned true. `Recipe_RemoveBodyPart.GetPartsToApplyOn` returned the four distinct bitten body parts exactly once each with no missing or duplicate bitten parts. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Live corpse/alert/forbid evidence: `zombieland/zombie_corpse_alert_forbid_contract` from `EMPTY.rws` proved a human corpse still counts for the unburied-colonist alert and is auto-forbidden outside home, while normal and former zombie corpses do not count for that alert and remain unforbidden outside home. The same run kept serum extraction available on both zombie corpse variants and double-tap unavailable for both; `rimbridge/list_logs minimumLevel=warning` returned no entries.

Required tests:
- Infection progression scenario: bite, hidden stage, visible treatable stage, untreatable conversion, treatment in and out of bed, amputation/removal path if feasible.
- Alerts scenario: verify all infection alerts point to the correct colonists and clear when treatment/conversion happens.
- Corpse conversion save-load: save during countdown, reload, verify timer and double-tap prevention.
- Serum workflow: extract area, filter visibility, auto-extract workgiver, recipe cure, inventory/product handling.

Current gap:
- Focused contracts exist, and the natural-healing/remove-body-part/ShouldRemove medical patch cluster plus corpse alert/forbid boundaries now have live evidence. Remaining E gaps are infection alert/UI timing, save-load progression during corpse conversion, and proving cure/double-tap/extract/medical UI together as one player workflow.

### F. Contamination

Code surface: `Source/ContaminationManager.cs`, `Source/ContaminationSerializer.cs`, `Source/ContaminationEffect.cs`, `Source/ContaminationFactors.cs`, `Source/ContaminationNeed.cs`, `Source/Hediff_Contamination.cs`, `Source/MentalState_Contamination.cs`, `Source/JobDriver_Contamination*.cs`, `Source/ContaminationPatches*.cs`, `Source/QuestDecontaminateColonists.cs`.

Already evidenced:
- `11a0c90 Cover core contamination state sync`
- `2bee432 Cover contamination ground and fire hooks`
- `22b45b3 Cover zombie death contamination`
- `fa02d3f Restore contamination effect registration`
- `7e27948 Fix contamination effect duration factor`
- `2c02244 Cover contamination mimic job`
- `324cfb2 Cover contamination sleepwalk job`
- `ae37dda Cover contamination breakdown job`
- `b394e63 Cover contamination tending transfer`
- `23b889f Harden contamination hoarding path failures`
- `68fb03e Cover contamination hoarding driver flow`
- `cf0479e Preserve contamination through building replacement`
- `662d902 Preserve contamination in recipe products`
- `065b1a5 Cover contamination frame construction`
- `5f73f13 Cover contamination construction and corpse handoff`
- `fff7028 Cover contamination stack and mineable yield`
- `f73fe81 Cover contamination rest and plant stump paths`
- `92b46aa Cover contamination roof and drill yields`
- `40c3613 Cover contamination carry and dispenser handoffs`
- `33b4ad2 Cover contamination filth and leavings`
- `a125cdb Cover smooth wall contamination handoffs`
- `f8f709d Cover contamination snow clearing`
- `a908279 Add pollution cleanup contamination fixture`
- `5f9f91e Cover contamination pollution and melee handoffs`
- `45520ed Cover plant harvest contamination`
- `5b550e4 Cover plant sow contamination`
- `beeb01c Cover wild plant contamination`
- `51b739b Cover ambrosia sprout contamination`

Required tests:
- Serializer save-load scenario: contaminated pawns, cells, terrain, buildings, stacks, plants, and active contamination effects survive reload with no precision or map-index loss.
- Player contamination scenario: exposure, need/hediff progression, mental-state jobs, treatment, tending, quest handoff, and cleanup in a single colony save.
- Quest scenario: decontamination quest generation, pickup transport/send flow, success, legacy failure/success outcome branches, and save-load while active.
- Replacement-chain scenario: blueprint -> frame -> building -> minified -> installed, plus plant -> harvest -> products and mineable -> yield.

Current gap:
- Handoff coverage is extensive and `TEST_PATCH_AUDIT.md` now has source/decompiler dispositions for all 68 static contamination rows plus the dynamic contamination rows. `C0-state-serializer` is covered by live evidence: `EMPTY` loaded, core/effect contamination contracts passed before and after saving/reloading `ZL_Contamination_Persistence_00_setup.rws`, generic reads confirmed spawned pawns/things plus a contamination mental job persisted, and `zombieland/read_contamination_state` read exact post-reload values including `ComponentIndustrial6902` contamination `0.6`. `C1-stack-container` is covered: stack absorb, split, carry ticks, ingestion, contaminated pawn/meal/steel stack save-load, real `ThingOwner.TryTransferToContainer` partial/full frame transfers, post-reload held-resource loss, and reloaded frame completion fallback all passed. `C2-build-replace` is covered: real frame construction, minify/install/reinstall blueprints, terrain/foundation construction, smooth wall, and smoothed-wall reversion all passed and the durable fixture reloaded with exact expected contamination. `C3-world-products` is covered for stable world-product persistence: filth/leavings runtime transfer, mineable yield, deep drill output, plant harvest/sow/stump/wild spawn, ambrosia sprout, and roof-collapse products passed from `EMPTY`, saved to `ZL_Contamination_Persistence_20_world_products.rws`, and reloaded with exact expected contamination for the durable products. One explicit caveat remains inside C3: RimWorld reloads compressed slag chunks under regenerated ids/positions, so the contract proves slag leavings at runtime while the persisted leavings assertion is represented by the stable component leaving. `C4-cleaning-environment` is covered for the active core configuration: cell entry, fire reduction, and snow clearing passed from `EMPTY`, saved to `ZL_Contamination_Persistence_25_cleaning_environment.rws`, and reloaded with exact expected contamination; pollution clearing is explicitly unavailable because the active mod set lacks `Wastepack`/`ClearPollution`. `C5-player-effects` is covered for focused runtime contracts and combined save-load evidence: hallucination, mimic, sleepwalk, breakdown, and hoard pather-failure all ran together in `ZL_Contamination_Persistence_30_player_effects.rws`; after reload and 270 total post-reload ticks, all five contamination jobs were still active, hoard retained a 25-cell room and carried silver back toward storage, hallucination kept a ghost mote, mimic kept chasing a victim, sleepwalk stayed in its wait window, and breakdown continued pathing. The final C5 save/load run produced no warning-or-higher log entries. The hoard crash root cause was `Building_Door_PawnCanOpen_Patch` calling `FreePassage` from a `PawnCanOpen` postfix; RimWorld 1.6 `FreePassage` can call `WillCloseSoon`, which scans nearby pawns and calls `PawnCanOpen` again. `C6-ui-quest` is now covered for player-facing visibility, decontamination quest generation, challenge-rating suppression, quest-tab row integration, save/reload persistence, and accepted pickup/send/return success in the active Core-only configuration: a live `EMPTY` fixture proved contaminated thing stats, mouseover labels, inspect-pane contamination bar, selected thing icon overlay, and map contamination overlay; `QuestNode_CreateDecontaminationPickupTransporter` replaced the missing 1.4 `Util_TransportShip_Pickup` dependency with a Core-safe `ZombieLand_DecontaminationTransportPod`, avoiding DLC shuttle code that logs expansion-system errors without Royalty/Ideology/Biotech/Anomaly/Odyssey; the contract returned `decontaminationMax.value=0` and `ordinaryMax.value=3`; after saving/reloading `ZL_Contamination_Persistence_30_quest.rws`, the read-only contract stayed green and the Quests tab layout showed `Decontamination Offer` with expiry `7d` and no rating text. The accepted-flow contract then accepted the quest, advanced 3500 ticks to pod arrival, loaded a colonist into the real `CompTransporter`, observed `SentSatisfied`, forced the treatment return timer, and ended the quest with `QuestState.EndedSuccess` without warning-or-higher logs. C6 outcome branches are also covered from fresh `EMPTY` loads: pickup destruction ended `QuestState.EndedFailed`, `SentUnsatisfied` ended `QuestState.EndedFailed`, and killing the sent pawn while away ended `QuestState.EndedSuccess` as defined by the RimWorld 1.4 legacy `ColonistsDied` outcome. The accepted contract filters pawn-death notifications to the sent subject so unrelated pawn deaths do not trigger the decontamination outcome. The final C6 branch run produced no warning-or-higher log entries.

### G. Buildings, Items, Gizmos

Code surface: `Source/Chainsaw.cs`, `Source/ZombieThumper.cs`, `Source/ZombieShocker.cs`, `Source/CompActivatable.cs`, `Source/CompBreakable.cs`, `Source/CompProperties_*`, `Source/JobDriver_ZapZombies.cs`, `Source/JobDriver_FixBrokenChainsaw.cs`, `Source/PlaceWorker_ZombieShocker.cs`, `Source/Graphic_Breakable.cs`, `Source/VariableGraphic.cs`, `Source/VariableMaterial.cs`.

Already evidenced:
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

Required tests:
- Build-use-maintain scenario: construct or spawn shocker, thumper, and chainsaw, exercise gizmos, power/fuel/battery state, damage, break, repair, and save-load.
- Room defense scenario: powered shocker, thumper impact, chainsaw pawn, zombies at doors/walls, and visual/audio feedback.
- Placement rules scenario: place workers, forbidden placement, missing power/battery, room-size rejection.

Current gap:
- Narrow behavior exists for the main items. `S-Defense-Room` now has a durable live fixture covering setup/read/save-load state for a powered shocker, active fueled thumper, drafted colonist with running chainsaw, broken spawned chainsaw, repair component, placement accept/reject gates, gizmo inventories, battery/power state, and a zombie target in `ZL_Defense_Room_base.rws`. The post-reload action sequence is covered through `zombieland/defense_room_state` action modes: real shocker zap/paralyze/mote emission, thumper impact and distance falloff, chainsaw building damage/drop/break tracking, repair workgiver/component consumption from the saved broken chainsaw, wall push over a home-area wall with warning letter, and the door-open stop path for real zombie `AttackStatic`. The post-action runs had no warning-or-higher log entries. Remaining G work is any visual/audio evidence needed for player-facing effects.

### H. Incidents, Director, Threat, Weather

Code surface: `Source/ZombieIncidents.cs`, `Source/TickManager.cs`, `Source/ZombieWeather.cs`, `Source/Alerts.cs`, `Defs/Zombie_Quests.xml`, `Defs/Zombie_Faction.xml`, `Defs/Zombie_Letter.xml`.

Already evidenced:
- `2cc1beb Cover event special zombie spawning`
- `6dd03e1 Cover incident scheduling calculation`
- `aab7620 Cover incident alert wave letters`
- `2ef5967 Cover random zombie type weights`
- `f44b336 Cover child zombie generation gate`
- `c2dd3b5 Cover zombie faction world state`
- `b7d560e Cover incident infection and ticking budget`

Required tests:
- Incident wave scenario: real-time incident scheduling from a clean colony, letter display, spawn location, special mix, max zombie limit, and post-wave cleanup.
- Threat forecast scenario: dynamic threat level changes, zero-threat zombie death option, forecast UI tooltip, and save-load.
- Spawn mode scenario: all-time, dark-only, event-only, soft-ground, map-edge, fogged-room, and blocker replacement behavior.

Current gap:
- `S-Incident-Threat` now has durable live evidence for the main incident/threat spine: `ZL_Incident_Threat_base.rws` is generated from `EMPTY` with three armed colonists and a deterministic soil field because the baseline map is otherwise all `SterileTile`, which is invalid for all-over-map spawn validation. From that saved fixture, `zombieland/incident_threat_state actionMode:all` passed a scheduler-driven wave through `ZombiesRising.ZombiesForNewIncident` and `SpawnEventProcess` with 14 zombies, cap accounting, and one `ThreatSmall` `Zombie attack` letter; edge and all-over alert waves each spawned four normal zombies and produced the expected `Zombie attack` / `Direct attack of zombies` letters; explicit special-type event spawning matched all nine requested types; generated human incident pawns received zombie bites while animals did not, and the incident postfix reduced incident bites to harmless; zombie-faction generation routed the normal zombie pawn kind through Zombieland while blob/spitter kinds remained vanilla-generated; dynamic threat forecast and disabled-threat behavior matched `ZombieWeather`; `GlobalControlsUtility.DoDate` forecast UI geometry covered the zombie-count readout, dynamic threat readout, tooltip placement, and no overlap with the vanilla date readout; the rendered forecast tooltip preview was captured at `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_forecast_tooltip_preview__clip.png`; ambient spawn mode checks covered all-over soft-ground spawning, edge spawning, dark-only spawning, and event-only ambient suppression; fog checks covered fogged-door spawn, fog-blocker removal spawn, and blocker replacement suppression; zero-threat `Zombie.CustomTick` killed the enabled zombie at threat level `0` while preserving the disabled comparison zombie; zombie-faction raid worker routing hit the installed `IncidentWorker_Raid.TryExecuteWorker` prefix and called `ZombiesRising.TryExecute` under both `EdgeWalkIn` and `CenterDrop`; `rimbridge/list_logs minimumLevel=warning` returned no entries.
- `S-Incident-Threat` is now runtime-covered for its planned H scope. Future end-to-end storyteller firing would still be additive, but the remaining patch-boundary and UI/visual gaps have direct evidence.

### I. Hostility, Combat, Avoidance, Areas

Code surface: `Source/Patches_Hostility.cs`, `Source/ZombieAreaManager.cs`, `Source/ZombieAvoider.cs`, `Source/WorkGivers.cs`, attack/workgiver patches in `Source/Patches.cs`.

Already evidenced:
- `74f6efd Restore harmless zombie flee filtering`
- `f651efa Add colonist avoidance interrupt smoke`
- `0416696 Add workgiver avoidance smoke`
- `0c6dacb Add avoid grid door and danger smoke`
- `d7f1a04 Cover zombie hostility rules`
- `2770e6c Cover special zombie active threat counts`
- `6beb859 Cover zombie target cache filtering`
- `e4f9d64 Cover zombie area risk classifications`
- `a0302d6 Cover colonist outside area risk`
- `391c041 Document avoidance contract recheck`
- Live area-workflow evidence: `zombieland/area_workflow_state setupFixture:true openManageDialog:true` created/refreshed five `ZL_Area_*` risk-mode areas, proved `AreaManager.CanMakeNewAllowed()` and `TryMakeNewAllowed` stayed enabled, proved `SortAreas()` preserved the custom order, proved the manage-area constructor reset selected/scroll state, opened the real `RimWorld.Dialog_ManageAreas`, and captured the patched UI at `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_area_workflow_manage_areas__clip.png`. The fixture was saved as `ZL_Area_Workflow_base.rws`, reloaded, and read back with all five risk modes/colors/cell counts intact; `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Live area behavior evidence: the same scenario tool now has `actionMode:behavior`, run from `ZL_Area_Workflow_base.rws` and again after reloading `ZL_Area_Workflow_behavior.rws`. It assigns a colonist to `ZL_Area_ColonistInside`, places a tracking zombie in `ZL_Area_ZombieInside`, proves the dangerous-area manager tracks both entries, captures the real danger overlay at `/Users/ap/Library/Application Support/RimWorld/Screenshots/zl_area_workflow_danger_overlay__clip.png`, proves avoid-grid `DangerUtility.GetDangerFor` returns `Deadly` for the normal active colonist and non-Deadly while the job is player-forced, proves drafted `Wait_Combat` is not converted to forced `Flee`, proves undrafted `Wait_Combat` converts to a player-forced `Flee` on tick 1 with a safe destination, and proves a non-forced undrafted `AttackMelee` job in the same avoid danger also becomes player-forced `Flee` on tick 1 with a safe destination. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Live mixed-targeting evidence: `zombieland/area_workflow_state setupFixture:true actionMode:targeting`, run from `ZL_Area_Workflow_behavior.rws`, creates a transient combat fixture and cleans it before returning. The run proved player target filtering excludes roped, confused, and active electric zombies for an unsuitable rifle; enemy filtering removes all zombies while `enemiesAttackZombies=false` and allows normal/suicide/roped ordinary zombies while still excluding spitter, blob, albino, confused, and unsuitable electric targets when enabled; animals respect `animalsAttackZombies`; special shooting score boosts made the suicide zombie score `112.218658` versus normal `100.487808`; friendly-fire patch owners were installed for both private target-score methods and the helper removed zombies while preserving non-zombies. Additional direct available-target branch probes now cover friendly human, friendly mech, player mech, player turret, and hostile turret searchers. They proved friendly human/mech searchers keep ordinary nearby zombies and reject roped/confused/harmless/special unsuitable targets, player mechs remove all Zombieland targets under `AttackMode.OnlyHumans` and keep allowed player targets under `Everything`, player turrets reject harmless/roped/confused/electric targets while keeping spitter/blob, and hostile turrets obey `enemiesAttackZombies` while rejecting harmless/special/electric targets. Non-pawn turret targeting now uses a real `Turret_MiniTurret`/`Building_TurretGun` and `CurrentEffectiveVerb`; it proved the Zombieland `BestAttackTarget` patch owner is installed, the turret cannot harm electric zombies, normal/spitter/blob targets are selectable, and roped/confused/active-electric zombies are rejected. Direct hostility checks now prove player/enemy/non-colony animal `GenHostility.HostileTo` gates, infecting enemy suppression, spitter/blob non-hostility to non-player factions, active-threat exclusion for player/null/enemy-disabled settings, and enemy active-threat inclusion only under `AttackMode.Everything`. Target-cache checks prove the replacement colony-hostile cache contains a real hostile human, excludes normal/spitter/blob zombies, and removes the hostile after destruction. Drafted `Wait_Combat` adjacent-target checks prove `JobDriver_Wait.CheckForAutoAttack` is patched by `net.pardeike.zombieland`, active normal zombies, player-visible spitters/blobs, and ordinary hostile pawns are auto-attacked, while roped and confused zombies are skipped. Tar-smoke melee targeting is now included: a real adjacent melee verb could hit a hostile human before smoke, real `TarSmoke` was spawned on the target, and the same melee verb still returned `CanHitTargetFrom=true` after smoke. The first passes exposed and fixed real bugs: `AttackTargetFinder_BestAttackTarget_Patch` could return a cached zombie that failed the caller validator, the replacement target cache could keep stale destroyed hostile targets, and `AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch` removed ordinary `Zombie` targets but left `ZombieSpitter`/`ZombieBlob` for player mechs under `AttackMode.OnlyHumans`. The Wait auto-attack fixture also corrected the confused-zombie setup from `GenTicks.TicksGame` to `GenTicks.TicksAbs`, matching the source `paralyzedUntil` comparison used during real ticking, and now checks ordinary hostile pawns by real attack start instead of random melee damage. Final build was 0 warnings/0 errors and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Tar-smoke aim-chance coverage is now included in `actionMode:targeting`: a real rifle shooter/hostile target pair had non-zero standard aim chance before smoke, real target-cell `TarSmoke` forced `ShotReport.AimOnTargetChance_StandardTarget` to `0` and made ranged `CanHitTargetFrom=false`, and a direct synthetic `ShotReport.covers` entry with `TarSmoke` also forced the cover-list branch to `0`. The same pass tightened the drafted wait auto-attack fixture so roped targets remain intentionally roped through the negative probe and blob positives assert attack start rather than random melee damage. Final build was 0 warnings/0 errors, `actionMode:targeting` returned `success=true`, and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.
- Downed-combat targeting evidence is folded into the same `actionMode:targeting` pass. The bridge records the actual Harmony targets for `Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch` and `JobDriver_AttackStatic_TickAction_Patch`, proves `Pawn_MindState.MeleeThreatStillThreat` is not patched because installed 1.6 no longer reads `Pawn.Downed`, and verifies real drafted melee and rifle jobs continue against health/public-downed normal zombies with `job.killIncappedTarget=false`. The melee case damaged the zombie by tick 10; the ranged `AttackStatic` case entered firing cooldown by tick 129. Final build was 0 warnings/0 errors and `rimbridge/list_logs minimumLevel=warning` returned no warning-or-higher entries.

Required tests:
- Area workflow scenario: create/manage zombie avoidance areas, assign colonists, show danger overlays, verify colonists respect or ignore risk based on toggles and draft state.
- Combat mixed-faction scenario: colonists, raiders, animals, manhunters, turrets, specials, harmless/electrical zombies, and tar smoke targeting.
- Work interruption scenario: hauling, sleeping, building, ranged attack, melee, and drafted behavior near zombies.

Current gap:
- Area creation/manage-dialog UI, area assignment, dangerous-area warning state, avoid-grid danger classification, drafted exemption, player-forced danger exemption, ordinary undrafted job interruption, melee job interruption, the main mixed-faction target-choice rules, friendly/mech/thing target-list branches, non-pawn turret targeting, drafted adjacent `JobDriver_Wait.CheckForAutoAttack` behavior, direct `GenHostility`/active-threat gates, target-cache register/deregister behavior, tar-smoke melee targeting and aim chance, and downed-zombie melee/ranged attack continuation are now covered. No open I-slice gap remains from this pass; broader future I work should move to new functionality rather than re-testing these same branches.

### J. Rendering, Assets, Audio, Visual Effects

Code surface: `Source/GraphicsDatabase.cs`, `Source/Assets.cs`, `Source/ZombieRenderCompat.cs`, `Source/ZombieSpitter.cs`, `Source/ZombieBlobRenderer.cs`, `Source/ZombieThumper.cs`, `Source/OverlayDrawer.cs`, `Resources`, `Textures`, `Sounds`, Unity asset project under `Originals/Effects`.

Already evidenced:
- `ff3d043 Make dark slimer smoke dense`
- `226125b Suppress vanilla dark slimer smoke flecks`
- `aeeac24 Fix blob save load rendering`
- `f758d40 Render tar smoke as black gas`
- `efdf9d7 Fix tar smoke rendering and verify zombie ball flight`
- `6f024d4 Apply Unity blob source compatibility fix`
- `190ca31 Fix spitter duplicate body rendering`
- `d914fd9 Guard Unity asset bundle exports`
- `a30ef98 Harden Unity asset temp projects`
- `dd58881 Add Unity bundle inspection helper`
- `1cd82e3 Rebuild Unity asset bundles from generated sources`
- `928b367 Record runtime recheck after asset rebuild`
- `012497a Refresh Unity asset bundles`
- `0ebb256 Record suicide bomber overlay evidence`
- `0892412 Record visual lineup verification`

Required tests:
- Visual lineup scenario: alive, moving, attacking, damaged, corpse, fire, tar/gas, projectile, thumper wave, shocker effect after real tick stepping and after save-load.
- UI overlay scenario: health bars, zombie stats, danger highlight, thought bubble motes, menu textures, forecast tooltip.
- Audio smoke: night ambient, zombie hit/rage/tracking/eating, special sounds, thumper/chainsaw/shocker sounds at least verify no missing sound defs and correct trigger paths.

Current gap:
- Visual assets have good recent evidence. UI overlays and sound triggers are much less covered.

### K. Social, Selection, Thought Hygiene

Code surface: social/thought/selection patches in `Source/Patches.cs`, `Source/BridgeTools/ZombielandBridgeTools.Social.cs`, `Source/ZombieCorpse.cs`, and former-colonist zombie state.

Already evidenced:
- `6d2afda Restore zombie social suppression`
- `2ca0e7b Add former-colonist selection smoke`
- Live social hygiene evidence: `zombieland/zombie_social_thought_suppression` from `EMPTY.rws` proved a colonist and normal zombie do not count as social-memory partners, do not know each other, have zero bidirectional opinion, return no social thoughts about the zombie, cannot interact with each other through `Chitchat`, and suppress observed zombie-corpse thought/history-event returns. The same run proved an ordinary colonist can receive `DebugBad` while the normal zombie cannot. `rimbridge/list_logs minimumLevel=warning` returned no entries.
- Live selection evidence: `zombieland/zombie_selection_respects_former_colonist` from `EMPTY.rws` proved ordinary live zombies and zombie corpses are not map-click selectable, former-colonist live zombies and corpses are map-click selectable, the real selector rejects ordinary zombie corpses and accepts former-colonist zombie corpses, and former-colonist zombies receive the Zombieland label color while ordinary zombies do not. `rimbridge/list_logs minimumLevel=warning` returned no entries.

Required tests:
- Social suppression scenario: ordinary colonist controls plus zombie participant through memories, opinions, social thoughts, interactions, observed corpses, and thought eligibility.
- Selection scenario: live/corpse ordinary zombies versus former-colonist zombies through map-click selection, real selector selection, label color, and inspect tab behavior.
- Interaction ticking scenario: prove zombie `Pawn_InteractionsTracker.InteractionsTrackerTickInterval` is skipped while ordinary pawn interaction ticking remains intact.

Current gap:
- The social-memory, thought, interaction-call, observed-corpse, label-color, selector, and map-click boundaries now have direct runtime evidence. Remaining K work is a direct interaction-tick probe and broader player-facing inspect-tab UI coverage.

### L. External Mod Integrations

Code surface: `Source/CETools.cs`, `Source/RimConnectSupport.cs`, `Source/CameraPlusSupport.cs`, `Source/DubsTools.cs`, `Source/SoSTools.cs`, `Source/VehicleTools.cs`.

Already evidenced:
- `560065a Cover RimConnect zombie actions`
- `1143ea7 Cover RimConnect super drop`

Required tests:
- RimConnect scenario: action registration, settings UI injection, spawn/kill/rage/super drop actions, alert notifications.
- Combat Extended source audit plus runtime smoke if installed: armor reroute, projectile distance, bullet absorption, ammo user interaction.
- CameraPlus marker/color integration source audit and runtime visual smoke if installed.
- Dubs Bad Hygiene patch source audit if installed.
- Save Our Ship 2 source audit and runtime smoke if installed: space zombie generation, hologram exclusion, floating zombie mesh/altitude.
- Vehicle Framework source audit and runtime smoke if installed: vehicle flee avoidance and move speed/timestamp logic.

Current gap:
- Only RimConnect has direct evidence. Other integrations are almost entirely unproven for 1.6 unless the optional mods are unavailable; if unavailable, retain source/decompiler audits and record the missing runtime dependency.

### M. Remove/Uninstall and Save Hygiene

Code surface: `Source/ZombieRemover.cs`, `Source/Dialog_SaveThenUninstall.cs`, serialization in settings/contamination/colonist components.

Already evidenced:
- No specific post-baseline coverage found in the commit subjects beyond baseline compile.

Required tests:
- Save-then-uninstall scenario: create a save with zombies, zombie faction, corpses, hediffs, thoughts, tales, battle logs, filters, map/world components, contamination, and special things; run remover; load resulting save without Zombieland; verify no unresolved def/type references and no zombie faction/components remain.
- Filter hygiene scenario: outfits, food restrictions, storage filters, serum/extract filters, and corpse filters after removal.
- World-pawn hygiene scenario: world pawns, dead pawns, relations, memories, and battle log entries referencing zombies are removed or made safe.

Current gap:
- This is high risk and basically uncovered. It needs source audit plus a destructive-copy save scenario.

## Next Test Definitions To Write

The next tests should be scenario definitions, not more isolated endpoint smokes:

1. `S-Source-Patch-Audit`: inventory every Harmony patch group and gameplay extension surface against RimWorld 1.6 target signatures and current scenario coverage.
2. `S-Settings-Persistence`: settings defaults, in-game settings, keyframes, colonist toggles, save-load.
3. `S-Core-Horde-Loop`: mixed zombie horde sensing, movement, doors/walls, rage, eating, fire, save-load.
4. `S-Special-Gauntlet`: all special zombies in one controlled map with action/death/cleanup/visual pass.
5. `S-Contamination-Persistence`: contaminated cells/pawns/things/effects/jobs/quest across save-load.
6. `S-Defense-Room`: shocker, thumper, chainsaw, wall push, doors, powered buildings, colonist work/fight flow.
7. `S-Incident-Threat`: real incident scheduling, letters, spawn mode matrix, threat forecast UI.
8. `S-External-Mod-Audit`: CE, CameraPlus, Dubs, SoS, Vehicles, RimConnect source/runtime matrix.
9. `S-Uninstall-Hygiene`: save copy removal and load without Zombieland.
10. `S-Dense-Performance`: dense horde tick/frame/log smoke after behavior scenarios are stable.

Each scenario definition should include:

- Fixture save name and whether it is immutable or disposable.
- Setup steps using generic RimBridge/RimWorld tools wherever possible.
- Required source/decompiler checks for the RimWorld 1.6 APIs involved.
- Runtime assertions and visual/log/performance artifacts.
- Prior commits that already cover the primitives so the scenario does not duplicate them.
