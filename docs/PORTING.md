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
- Completed: compile baseline, deployment baseline, Unity bundle validation, GABS startup/play-data baseline, native 1.6 debug-map creation, first special-zombie visual fixture, shared debug/bridge runtime actions, infection bridge smoke, direct pawn conversion bridge smoke, corpse-driven conversion bridge smoke, suicide-bomber explosion bridge smoke, toxic-splasher goo smoke, dark-slimer tar smoke, dark-slimer damage smoke, healer behavior smoke, electrifier EMP smoke, miner mining smoke, tanky pheromone smoke, tanky armor smoke, spitter projectile smoke, spitter impact smoke, albino damage-filter smoke, and `ZombieTest` movement smoke. As of 2026-05-28, GABS starts RimWorld with Core, Harmony, RimBridgeServer, and Zombieland; a native 1.6 save named `ZombielandVisualLineup` contains Electrifier, Suicide Bomber, Healer, Dark Slimer, Albino, Tanky, Toxic Splasher, and Miner zombies in a compact comparison pattern. Loading that save now reaches visual-ready state without the previous unresolved render-tree errors, and reloading it from an already-loaded map no longer crashes in `ZombieWanderer`. `zombieland/convert_infected_corpse_to_zombie` verifies that a bitten pawn can die into a corpse, transition from Fresh to Rotting through `Corpse.RotStageChanged`, enter `TickManager.colonistsToConvert`, and convert into a normal wandering zombie while the game is paused. `zombieland/detonate_suicide_bomber` verifies that `Zombie.Kill` queues one `TickManager` explosion for a suicide bomber and that `ExecuteExplosions` drains it without errors. `zombieland/kill_toxic_splasher` verifies that `Zombie.Kill` makes a toxic splasher drop `StickyGoo` around its death cell. `zombieland/move_dark_slimer` verifies that moving a dark slimer through the 1.6 `Thing.Position` patch drops `TarSlime` near the move path. `zombieland/damage_dark_slimer` verifies that real bullet damage reaches the damage-worker patch, advances the RimWorld explosion window derived from `Explosion.GetCellAffectTick`, and produces custom dense black `TarSmoke` without vanilla `BlindSmoke`; screenshot evidence is in `~/Library/Application Support/RimWorld/Screenshots/zl_dark_slimer_smoke_no_vanilla_fleck__cell_rect.png`. `zombieland/heal_wounded_zombie` verifies that the healer branch in `Zombie.CustomTick` clears a nearby zombie's hediffs and queues the heal effect state. `zombieland/emp_electrifier` verifies that real EMP damage reaches the stun notification patch and temporarily disables an active electrifier. `zombieland/mine_with_miner` verifies that Zombieland's miner code damages an adjacent `Mineable` and sets mining cooldown. `zombieland/move_tanky` verifies that tanky movement through the 1.6 `Thing.Position` patch bumps directional pheromone timestamps for following zombies. `zombieland/damage_tanky_armor` verifies that real bullet damage enters RimWorld's damage pipeline, reaches the tanky armor patch, degrades the shield, and deals no health damage while armor absorbs the hit. `zombieland/spit_zombie_ball` verifies that a spitter in firing state can tick its real job driver, run the shoot path, and launch a `ZombieBall` projectile; this also fixed a `JobDriver_Spitter.GetReport` null window before driver init. `zombieland/impact_zombie_ball` verifies that `ZombieBall.Impact` destroys the projectile and spawns a delivered zombie. `zombieland/damage_albino` verifies that repeated real bullet damage is mostly blocked while real explosive damage still gets through. `ZombieTest` verifies that the toxic and normal zombies keep moving across repeated samples after Zombieland preserves destinations while RimWorld 1.6 async path requests are pending.
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
