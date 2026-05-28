# Zombieland 1.6 Porting Notes

This file is the single live coordination document for the RimWorld 1.6 port. Keep it compact: update it when a decision changes how future work should proceed, when a blocker is discovered, or when a repeatable test/build command changes.

## Work Cycle

1. Read `AGENTS.md` and this file before starting a new work unit.
2. Pick one narrow compile/runtime cluster and record the target in Current Unit.
3. Use `scripts/build-quiet.sh` for compile passes. Do not paste full compiler logs into notes.
4. Use `scripts/validate-unity-assets.sh` before runtime tests that depend on Unity bundles. It loads deployed bundles with Unity `2022.3.62f3` from a temporary project copy.
5. Use `scripts/export-unity-assets.sh` only after the missing source assets for the deployed bundle are recovered. It exports from a temporary copy so the tracked Unity `Library` does not churn.
6. When runtime testing begins, summarize `Player.log` with `scripts/summarize-rimworld-log.sh` before inspecting raw logs.
7. Promote repeated manual RimWorld/GABS actions into scripts or saved scenarios.
8. Revisit this file after each completed unit and prune stale notes instead of appending a new work log.

## Current Unit

- Target: validate special zombie pawn rendering on a native RimWorld 1.6 map.
- Completed: compile baseline, deployment baseline, Unity bundle validation, GABS startup/play-data baseline, native 1.6 debug-map creation, and first special-zombie visual fixture. As of 2026-05-28, GABS starts RimWorld with Core, Harmony, RimBridgeServer, and Zombieland; a native 1.6 save named `ZombielandVisualLineup` contains Electrifier, Suicide Bomber, Healer, Dark Slimer, Albino, Tanky, Toxic Splasher, and Miner zombies in a compact comparison pattern.
- Next blocker cluster: fix the one-frame unresolved render-tree errors on save load, then validate infection, corpse conversion, and special zombie behaviors beyond visuals.

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
- `Source/ZombielandBridgeTools.cs` exposes repeatable Zombieland bridge actions. Prefer `zombieland/spawn_reference_lineup` over repeated manual debug-action/UI spawning for the eight-type visual fixture.

## Known Risks

- RimWorld 1.6 pawn rendering was significantly refactored. Any Harmony patch touching pawn graphics should be treated as suspicious even after it compiles.
- `Source/ZombieRenderCompat.cs` is a compile-enabling compatibility layer for old `PawnRenderer.graphics` usage. It currently dirties the 1.6 render tree instead of fully rebuilding custom head/body/apparel render nodes, so zombie visuals and special apparel are the next high-risk runtime validation area.
- Loading `ZombielandVisualLineup` currently emits one `Attempted to draw <zombie> without a resolved render tree` error per saved zombie before settling into a usable visual-ready state. Do not reintroduce graphics preparation in `PawnRenderer.ParallelPreRenderPawnAt`; that path touches map pawn lists off the main thread. Find a main-thread pre-init or render-tree setup path instead.
- Suicide bomber bomb vests are restored for the visual baseline through an explicit overlay draw in `RenderExtras`. The old `PawnGraphicSet.ResolveApparelGraphics` injection does not participate in RimWorld 1.6 render-tree apparel drawing and still needs a cleaner render-node redesign later.
- The old `PathFinder.FindPathNow` cost-injection transpiler is disabled for the runtime-load baseline because RimWorld 1.6 moved map pathing to the new `Verse.PathFinder` job/data model. Redesign zombie avoidance costs against 1.6 path grid customization instead of reviving the old `PathFinderNodeFast.knownCost` patch.
- The cosmetic Zombieland main-menu button atlas patch is disabled because `Widgets.ButtonTextWorker` no longer contains the old draw call shape. This is not gameplay-critical.
- Compile success is not runtime success. The first runtime phase must validate mod load, map start, zombie spawning, pawn rendering, infection, corpse conversion, and special zombie behaviors.
- Generated logs and build output can consume context quickly. Keep artifacts on disk and summarize.

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
