# Zombieland 1.6 Porting Notes

This file is the single live coordination document for the RimWorld 1.6 port. Keep it compact: update it when a decision changes how future work should proceed, when a blocker is discovered, or when a repeatable test/build command changes.

## Work Cycle

1. Read `AGENTS.md` and this file before starting a new work unit.
2. Pick one narrow compile/runtime cluster and record the target in Current Unit.
3. Use `scripts/build-quiet.sh` for compile passes. For live GABS testing on this machine, deploy with `RIMWORLD_MOD_DIR="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods" scripts/build-quiet.sh`; the active local Zombieland root is inside the Steam app bundle, not the sibling `common/RimWorld/Mods` directory. Do not paste full compiler logs into notes.
4. Use `scripts/validate-unity-assets.sh` before runtime tests that depend on Unity bundles. It loads deployed bundles with Unity `2022.3.62f3` from a temporary project copy.
5. Use `scripts/export-unity-assets.sh` only after the missing source assets for the deployed bundle are recovered. It exports from a temporary copy so the tracked Unity `Library` does not churn.
6. When runtime testing begins, summarize `Player.log` with `scripts/summarize-rimworld-log.sh` before inspecting raw logs.
7. Promote repeated manual RimWorld/GABS actions into scripts or saved scenarios.
8. For time-based runtime effects, derive the minimum tick window from Zombieland or RimWorld source before asserting. Advance that measured window instead of checking immediately after the trigger or guessing a long wait.
9. Revisit this file after each completed unit and prune stale notes instead of appending a new work log.

## Current Unit

- Target: validate special zombie visuals and behavior on a native RimWorld 1.6 map.
- Completed: compile baseline, deployment baseline, Unity bundle validation, GABS startup/play-data baseline, native 1.6 debug-map creation, first special-zombie visual fixture, shared debug/bridge runtime actions, infection bridge smoke, cure infection recipe smoke, direct pawn conversion bridge smoke, corpse-driven conversion bridge smoke, double-tap infected-corpse smoke, zombie serum extraction smoke, rope zombie job smoke, suicide-bomber explosion bridge smoke, toxic-splasher goo smoke, dark-slimer tar smoke, dark-slimer damage smoke, healer behavior smoke, healer tick smoke, electrifier EMP smoke, electrifier active handler smoke, miner mining smoke, miner job mining smoke, tanky pheromone smoke, tanky armor smoke, tanky smash smoke, spitter projectile smoke, spitter impact smoke, blob spawn/render smoke, blob save/load smoke, albino damage-filter smoke, albino scream smoke, albino sabotage hack smoke, and `ZombieTest` movement smoke. As of 2026-05-28, GABS starts RimWorld with Core, Harmony, RimBridgeServer, and Zombieland; a native 1.6 save named `ZombielandVisualLineup` contains Electrifier, Suicide Bomber, Healer, Dark Slimer, Albino, Tanky, Toxic Splasher, and Miner zombies in a compact comparison pattern. Loading that save now reaches visual-ready state without the previous unresolved render-tree errors, and reloading it from an already-loaded map no longer crashes in `ZombieWanderer`. `zombieland/cure_zombie_infection_recipe` verifies that the real cure recipe worker finds the curable bite part, accepts `ZombieSerumSimple`, makes the bite harmless, removes it from curable recipe parts, clears `mayBecomeZombieWhenDead`, and prevents corpse conversion after death/rotting. `zombieland/convert_infected_corpse_to_zombie` verifies that a bitten pawn can die into a corpse, transition from Fresh to Rotting through `Corpse.RotStageChanged`, enter `TickManager.colonistsToConvert`, and convert into a normal wandering zombie while the game is paused. `zombieland/double_tap_infected_corpse` verifies that `WorkGiver_DoubleTap` can create a real `DoubleTap` job, the 1.6 `JobDriver_DoubleTap` reaches the corpse using `ClosestTouch`, full game ticks remove the corpse brain, and a later rot-stage change does not queue conversion. This fixed the stale driver `PathEndMode.OnCell` use, which made the job fall through before the smash toil in 1.6; the bridge fixture marks the job `playerForced` so Zombieland's avoidance patch does not intentionally interrupt the commanded test job. `zombieland/extract_serum_from_zombie_corpse` verifies that killing a real zombie produces a tracked `ZombieCorpse`, `WorkGiver_ExtractZombieSerum` creates the job, full game ticks produce `ZombieExtract`, and the corpse is destroyed/removed from tracking. `zombieland/rope_zombie_job` verifies the same command path as the float-menu patch, drafting the pawn and using `TryTakeOrderedJob` to set `zombie.ropedBy` and make the zombie count as roped/harmless. `zombieland/detonate_suicide_bomber` verifies that `Zombie.Kill` queues one `TickManager` explosion for a suicide bomber and that `ExecuteExplosions` drains it without errors. `zombieland/kill_toxic_splasher` verifies that `Zombie.Kill` makes a toxic splasher drop `StickyGoo` around its death cell. `zombieland/move_dark_slimer` verifies that moving a dark slimer through the 1.6 `Thing.Position` patch drops `TarSlime` near the move path. `zombieland/damage_dark_slimer` verifies that real bullet damage reaches the damage-worker patch, advances the RimWorld explosion window derived from `Explosion.GetCellAffectTick`, and produces custom dense black `TarSmoke` without vanilla `BlindSmoke`; `TarSmoke` now has an explicit black mote-shader draw path so it does not look like standard white transparent smoke in RimWorld 1.6. Screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_dark_slimer_tar_smoke_black_mote__cell_rect.png`. `zombieland/heal_wounded_zombie` verifies that the healer branch in `Zombie.CustomTick` clears a nearby zombie's hediffs and queues the heal effect state; `zombieland/heal_wounded_zombie_tick` advances the real paused game tick loop for the source-derived `Every12` window and verifies a fresh healer clears the wound and queues the effect without direct `CustomTick` calls. `zombieland/emp_electrifier` verifies that real EMP damage reaches the stun notification patch and temporarily disables an active electrifier; `zombieland/electrify_powered_building` verifies that the active electrifier handler sees a real RimWorld power net, starts fire through `FireUtility`, and disables itself for the expected quarter-hour window. The electrifier fixture explicitly registers a spawned `PowerConduit` and connects a `StandingLamp` through RimWorld's public power APIs because ad hoc spawned power buildings do not auto-settle before the handler runs. `zombieland/mine_with_miner` verifies that Zombieland's miner code damages an adjacent `Mineable` and sets mining cooldown. `zombieland/mine_with_miner_job` clears only the local pheromone neighborhood, points a normal-body miner at adjacent `MineableSteel`, and verifies that the real `JobDriver_Stumble` mining branch damages it and sets mining cooldown. `zombieland/move_tanky` verifies that tanky movement through the 1.6 `Thing.Position` patch bumps directional pheromone timestamps for following zombies. `zombieland/damage_tanky_armor` verifies that real bullet damage enters RimWorld's damage pipeline, reaches the tanky armor patch, degrades the shield, and deals no health damage while armor absorbs the hit. `zombieland/smash_with_tanky` verifies that a tanky target route through a wall makes the real `JobDriver_Stumble` path choose `AttackStatic`, and the vanilla attack job destroys the wall after Zombieland's paused bridge tick helper primes `ZombieTicker.managers`. `zombieland/spit_zombie_ball` verifies that a spitter in firing state can tick its real job driver, run the shoot path, and launch a `ZombieBall` projectile; this also fixed a `JobDriver_Spitter.GetReport` null window before driver init, and was rechecked after the paused bridge tick helper fix. `zombieland/impact_zombie_ball` verifies that `ZombieBall.Impact` destroys the projectile and spawns a delivered zombie. `zombieland/spawn_blob` verifies that `ZombieBlob.Spawn` creates a blob with the `Blob` job and a loaded `Metaballs` shader; screenshot evidence after removing the old debug square is in `~/Library/Application Support/RimWorld/Screenshots/zl_blob_spawn_no_debug_square__cell_rect.png`. `ZombieBlobSmoke` verifies that a saved blob reloads to visual readiness without the previous `ComputeBuffer` load-thread crash and still renders; screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_blob_reload_no_crash__cell_rect.png`. `zombieland/damage_albino` verifies that repeated real bullet damage is mostly blocked while real explosive damage still gets through; `zombieland/scream_with_albino` verifies that a real `JobDriver_Sabotage` scream reaches the first source-derived 40-tick pulse and forces a nearby humanlike pawn into `Vomit` with stun; `zombieland/hack_flickable_with_albino` verifies that the real sabotage hacking branch spends the source-derived 240-tick counter and then switches off a flickable `StandingLamp`. The scream smoke uses a player-faction test albino after spawn so the target pawn does not interrupt the measured scream window with melee damage. `ZombieTest` verifies that the toxic and normal zombies keep moving across repeated samples after Zombieland preserves destinations while RimWorld 1.6 async path requests are pending.
- Latest verified: `zombieland/zap_zombies_with_shocker` builds a sealed wall room with a `ZombieShocker`, battery, conduit network, actor, and live zombie, then runs the real `ZapZombies` job. The source-derived window is 90 job-wait ticks plus the 45-tick `SubEffecter_ZombieShocker` windup plus one tick per standable room cell; the fixture hit at tick 154, spawned 16 zap motes, consumed battery energy, and set `zombie.paralyzedUntil`. The fixture must keep the battery footprint out of the wall cells or the room becomes invalid.
- Latest verified: `zombieland/thumper_impact_cycle` spawns a real `ZombieThumper`, fuels it with Chemfuel through `CompRefuelable.Refuel`, enables the switchable comp, runs the source-derived cycle at minimum nonzero intensity, and verifies impact by fuel consumption and `lastImpactTicks` advancing. The fixture hit impact at tick 105 with `intervalTicks=100`, `upTicks=9`, `fallTicks=5`, `radius=2`, and consumed 1 fuel. Screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_thumper_impact_cycle__cell_rect.png`. Avoid large thumper intensity in synchronous bridge tests on jungle maps because the seismic dust wave can damage many plants and blow up test time; use separate narrow tests for full-radius wave damage/infestation-repel behavior.
- Latest verified: `zombieland/chainsaw_equip_toggle` spawns and fuels a real `Chainsaw`, equips it on a generated drafted colonist after clearing existing zombies and default equipment, starts it through the chainsaw gizmo action, advances real game ticks so `Pawn_EquipmentTracker.EquipmentTrackerTick` drives `Chainsaw.Tick`, verifies fuel consumption while running, and verifies the `Pawn_DraftController.Drafted` patch stops the motor on undraft. Do not call `SetFactionDirect` on chainsaws; RimWorld logs an error because the weapon cannot have a faction. Screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_chainsaw_equip_toggle__cell_rect.png`.
- Latest verified: `zombieland/chainsaw_slaughter_zombie` uses the same real fueled/equipped chainsaw setup, starts it through the gizmo, places a live normal zombie in the target adjacent cell, aligns the chainsaw angle as fixture setup, and advances real game ticks until `Chainsaw.Tick` reaches `Slaughter`. It killed the zombie on tick 1, queued one `VictimHead`, consumed fuel, and reduced chainsaw hit points by 1 without breaking it. Screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_chainsaw_slaughter_zombie__cell_rect.png`.
- Latest verified: `zombieland/fix_broken_chainsaw_job` breaks a spawned `Chainsaw`, adds it to `BrokenManager`, provides one `ComponentIndustrial`, and runs the real `WorkGiver_FixBrokenChainsaw`/`JobDriver_FixBrokenChainsaw` path. The job completed at tick 1000, consumed the component, cleared the broken comp, and removed the chainsaw from `BrokenManager`. `WorkGiver_FixBrokenChainsaw` now permits factionless chainsaws; faction ownership must not be a hard rejection because chainsaw faction state is not reliable across spawned/equipped weapon states. Screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_fix_broken_chainsaw_job__cell_rect.png`.
- Next blocker cluster: validate special zombie behaviors beyond visuals.

## Decisions

- Use `Krafs.Rimworld.Ref` `1.6.4633` as the stable 1.6 reference baseline. Avoid prerelease refs until there is a concrete reason.
- Older local RimWorld versions are available at `/Users/ap/Documents/OlderRimWorlds` (`RimWorldMac1.4.app`, `RimWorldMac1.5.app`, `RimWorldMac1.6.app`). Use them for decompiler comparisons when a 1.4 behavior needs to be preserved.
- Supported load folders are `v1.4` and `v1.6` only. Do not spend effort preserving RimWorld 1.5 compatibility. Current builds output to `1.6/Assemblies`; legacy tracked assemblies live under `1.4/Assemblies`.
- RimWorld 1.6 uses Unity `2022.3.35f1` on macOS. The local safe editor is `2022.3.62f3`; use it for Zombieland asset-bundle rebuilds. Existing checked-in bundles were built by Unity `2019.4.30f1`.
- Unity `2022.3.62f3` can load the existing deployed bundles and resolve `Dust` and `Metaballs`, so they are compatible enough for initial 1.6 runtime testing. A clean re-export is currently blocked because the deployed bundle contains `assets/_zombieland/dust.prefab`, `assets/_zombieland/metaballs.shader`, and smoke assets that are not present under `Originals/Effects/Assets`; preserve the current bundles until those source assets are recovered.
- Preserve gameplay behavior while getting the project compiling. Runtime behavior changes need source evidence from RimWorld 1.6 or live testing.
- Prefer small compatibility shims/patch retargets over broad rewrites until the 1.6 API shape is confirmed.
- Use GABS `games_start`/`games_stop`/RimBridge tools for runtime testing. Do not fall back to direct app launches when GABS startup fails; fix the GABS path or stop and report the blocker.
- Use native 1.6 saves for runtime fixtures. Do not load RimWorld 1.4 saves in 1.6; use `/Users/ap/Documents/OlderRimWorlds/RimWorldMac1.4-UserData/Saves/Test.rws` only as a visual/source reference.
- GABS process matching can be confused by a simultaneously running older RimWorld process because the executable name is still `RimWorld by Ludeon Studios`. Confirm the active 1.6 GABS connection before runtime tests.
- `Source/ZombielandBridgeTools.cs` exposes repeatable Zombieland bridge actions. Prefer `zombieland/spawn_reference_lineup` over repeated manual debug-action/UI spawning for the eight-type visual fixture. Debug actions and bridge endpoints share `Source/ZombieRuntimeActions.cs` for zombie spawn/remove, bite/infection, and direct pawn conversion operations; keep future test-only runtime mutations there instead of duplicating menu and bridge logic.
- The in-game bottom-right Menu tab uses `ListableOption_Zombieland` for the Zombieland settings option. It draws `ZombieButtonBackground` directly at the option level; avoid reviving the old global `Widgets.ButtonTextWorker` texture transpiler.
- Dark slimer damage must spawn Zombieland `TarSmoke` directly instead of routing through `GenExplosion` with `DamageDefOf.Smoke`; the explosion path can layer vanilla white smoke puffs over the black tar gas in RimWorld 1.6.

## Known Risks

- RimWorld 1.6 pawn rendering was significantly refactored. Any Harmony patch touching pawn graphics should be treated as suspicious even after it compiles.
- RimWorld 1.6 pawn pathing can leave `Pawn_PathFollower.curPath` null while an async `curPathRequest` is pending. Zombieland movement code must not treat `curPath == null` as an immediately invalid destination when the pather is still moving toward the same cell; doing so caused `ZombieTest` zombies to move one cell and then jitter in place.
- `Source/ZombieRenderCompat.cs` is a compile-enabling compatibility layer for old `PawnRenderer.graphics` usage. Custom body/head assignment now re-resolves each zombie render tree after dirtying it, which fixes the one-frame `Attempted to draw <zombie> without a resolved render tree` errors on `ZombielandVisualLineup` load. Do not reintroduce graphics preparation in `PawnRenderer.ParallelPreRenderPawnAt`; that path touches map pawn lists off the main thread.
- Suicide bomber bomb vests are restored for the visual baseline through an explicit overlay draw in `RenderExtras`. The old `PawnGraphicSet.ResolveApparelGraphics` injection does not participate in RimWorld 1.6 render-tree apparel drawing and still needs a cleaner render-node redesign later.
- The old `PathFinder.FindPathNow` cost-injection transpiler is disabled for the runtime-load baseline because RimWorld 1.6 moved map pathing to the new `Verse.PathFinder` job/data model. Redesign zombie avoidance costs against 1.6 path grid customization instead of reviving the old `PathFinderNodeFast.knownCost` patch.
- `ZombieWanderer.processor` is a static coroutine that can keep running across yields while RimWorld starts a long-event save load. It must stay guarded by `LongEventHandler.AnyEventNowOrWaiting` / `ShouldWaitForEvent` and by per-map validity checks; without those guards, loading a save from an already-loaded Zombieland map can crash in `MapInfo.ValidFloodCell -> PathGrid.WalkableFast`.
- Compile success is not runtime success. The first runtime phase must validate mod load, map start, zombie spawning, pawn rendering, infection, corpse conversion, and special zombie behaviors.
- Generated logs and build output can consume context quickly. Keep artifacts on disk and summarize.
- `zombieland/spit_zombie_ball` covers the spitter job-driver launch path, and `zombieland/impact_zombie_ball` covers the custom impact effect. A full in-flight projectile travel smoke is still deferred because a controlled `ZombieBall` remained spawned after the public cell-distance travel estimate; revisit with exact projectile private state if flight visuals or timing become suspicious.

## Fix Suggestions

### Unity `Originals/Effects` ZombieBlob Pink Material

Status: investigated in `/Users/ap/Desktop/Effects` against Unity `2022.3.62f3` on macOS/Metal, but not applied to this repo yet.

Problem:

- `Originals/Effects/Assets/Zombieland/ZombieBlob.shader` was authored for the old Unity project and uses `StructuredBuffer<float2> _Positions` plus `_Positions.Length`.
- Unity 2022's Metal compiler rejects buffer-size queries from shader code: pass a count explicitly or avoid the query.
- The tracked Unity YAML already contains old serialized GUID references, but this repo ignores and does not track `.meta` files. Unity therefore generates new GUIDs when opening the project, breaking the scene -> material and material -> shader links. The visible symptom is `ZombieBlob` rendering pink/magenta.

Minimal fix to apply:

1. Patch `Originals/Effects/Assets/Zombieland/ZombieBlob.shader` only in the small preview/test path:
   - Uncomment the existing static `cellCount` and `positions[cellCount]` array.
   - Remove or stop using `StructuredBuffer<float2> _Positions`.
   - Replace the `_Positions.Length` loop with `for(int idx = 0; idx != cellCount; ++idx)`.
   - Replace `pos = _Positions[idx];` with `pos = positions[idx];`.

   Expected source diff shape:

   ```diff
   -         /*
             static const int cellCount = 18;
             static const float2 positions[cellCount] =
             {
                 ...
             };
   -         */
   -
   -         StructuredBuffer<float2> _Positions;

   ...
   -            // for(int idx = 0; idx != cellCount; ++idx)
   -            int count = (int)_Positions.Length - 1;
   -            for(int idx = 0; idx != count; ++idx)
   +            for(int idx = 0; idx != cellCount; ++idx)
                {
   -               pos = _Positions[idx]; // positions[idx];
   +               pos = positions[idx];
                   power += cellPower(idx, coord, pos);
                }
   ```

2. Add `Originals/Effects/Assets/Zombieland/ZombieBlob.mat.meta` with the historical material GUID used by `Effects.unity`:

   ```yaml
   fileFormatVersion: 2
   guid: 450cf6f31c7c57b48b4247fb7d2cd73f
   NativeFormatImporter:
     externalObjects: {}
     mainObjectFileID: 0
     userData:
     assetBundleName:
     assetBundleVariant:
   ```

3. Add `Originals/Effects/Assets/Zombieland/ZombieBlob.shader.meta` with the historical shader GUID used by `ZombieBlob.mat`:

   ```yaml
   fileFormatVersion: 2
   guid: c1a5c4f8208d05b498f00011d22dc916
   ShaderImporter:
     externalObjects: {}
     defaultTextures: []
     nonModifiableTextures: []
     userData:
     assetBundleName:
     assetBundleVariant:
   ```

4. Optional, separate from the `ZombieBlob` pink fix: add `Originals/Effects/Assets/Zombieland/Rimworld.png.meta` with GUID `d75ba6bd32be5d34c888c16a0b983109`, because `Rimworld.mat` already references that texture GUID. This fixes the Rimworld material texture binding but is not required for `ZombieBlob` to render.

Avoid:

- Do not edit `Effects.unity`, `ZombieBlob.mat`, or `Rimworld.mat`; their serialized references already match the historical GUIDs.
- Do not add `Effects.unity.meta` or `Rimworld.mat.meta`; no tracked asset references those GUIDs.
- Do not broaden `.gitignore` unless there is a separate decision to start tracking Unity metas generally. For this targeted fix, force-add only the required `.meta` files because `.gitignore` currently ignores `*.meta`:

  ```bash
  git add -f Originals/Effects/Assets/Zombieland/ZombieBlob.mat.meta \
    Originals/Effects/Assets/Zombieland/ZombieBlob.shader.meta
  ```

Verification used in temp project:

- After adding the matching metas and restarting Unity, `/Users/ap/Desktop/Effects` rendered green blob shapes in the Scene view instead of a magenta plane.
- Before restarting Unity, the editor asset database still cached generated GUIDs, so a normal refresh was not enough. Restart Unity after changing these `.meta` GUIDs.
