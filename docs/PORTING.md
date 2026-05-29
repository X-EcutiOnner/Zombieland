# Zombieland 1.6 Porting Notes

This is the compact technical reference for the RimWorld 1.6 port. `PLAN.md` is the active goal and coverage entry point; this file keeps durable commands, decisions, and risks that should affect future work. Do not append chronological work logs here.

## Current Focus

- Continue coverage-driven scenario slices from `PLAN.md`.
- Favor native 1.6 save fixtures over ad hoc live-map mutation.
- Use source-derived tick windows and real game stepping for effects that unfold over time.
- Keep bridge endpoints generic. Zombieland-specific test contracts may exist, but broadly useful harness features belong in `RimBridgeServer`.

## Commands

- Compile only:
  ```bash
  env -u RIMWORLD_MOD_DIR scripts/build-quiet.sh
  ```
- Deploy Zombieland to the active Steam RimWorld local Mods folder:
  ```bash
  RIMWORLD_MOD_DIR="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods" scripts/build-quiet.sh
  ```
- Check the live bridge after crashes, reconnects, or confusing failures:
  ```bash
  scripts/check-bridge-health.sh
  ```
- Run a focused Zombieland bridge contract:
  ```bash
  scripts/run-zl-contract.sh zombieland/<tool_name>
  ```
- Step the real game by an exact number of ticks, one Unity update frame per tick:
  ```bash
  scripts/step-real-game.sh --save EMPTY --ack-attention --ticks 5
  ```
- Summarize RimWorld logs before inspecting raw output:
  ```bash
  scripts/summarize-rimworld-log.sh
  ```
- Validate/inspect/export Unity bundles:
  ```bash
  scripts/validate-unity-assets.sh
  scripts/inspect-unity-assets.sh
  scripts/export-unity-assets.sh
  ```

## Runtime Harness

- Use GABS `rimworld` for live tests. Do not directly launch RimWorld as a fallback.
- Stop RimWorld through GABS before deploy builds.
- Never run live GABS/RimBridge contracts in parallel.
- The active Steam RimWorld currently uses the local app-bundle mod folder:
  `/Users/ap/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods`.
- The active local `RimBridgeServer` was updated to `1.2.0` for this port work. Its general `rimworld/step_game_ticks` tool advances a paused game by `N` ticks with one real Unity `Root.Update` frame per tick.
- The decompiler-confirmed vanilla reference is `RimWorld.TimeControls.DoTimeControlsGUI`: `KeyBindingDefOf.Dev_TickOnce` only fires while `TickManager.CurTimeSpeed == TimeSpeed.Paused`, then calls `TickManager.DoSingleTick()` and optionally plays `SoundDefOf.Clock_Stop`.
- Use direct in-mod `AdvanceGameTicks` helpers only for deterministic contract setup where visual/render-frame behavior is irrelevant.
- `EMPTY.rws` can log the RimWorld ideology repair message `Faction had no ideoligions after loading. Adding random one.`; this opens GABS attention. Acknowledge it only when that fixture-level warning is not part of the behavior under test.

## Scenario Saves

- `EMPTY.rws`: immutable clean base map. Load, populate, and save under a scenario-specific name; never save over it.
- `ZombieTest.rws`: broad legacy repro map with existing zombies/settings.
- `ZombielandVisualLineup.rws`: special-zombie visual comparison save.
- New saves should use `ZL_<cluster>_<purpose>_base.rws` and be registered in `PLAN.md`.
- Do not load RimWorld 1.4 saves in 1.6. Use `/Users/ap/Documents/OlderRimWorlds/RimWorldMac1.4-UserData/Saves/Test.rws` only as visual/source reference.

## Version Decisions

- Support RimWorld `1.4` legacy and current `1.6`; do not spend effort preserving `1.5`.
- Use `Krafs.Rimworld.Ref` `1.6.4633` unless a concrete runtime mismatch requires updating.
- Current builds output to `1.6/Assemblies`; legacy tracked assemblies live under `1.4/Assemblies`.
- RimWorld 1.6 uses Unity `2022.3.35f1`; the local safe editor is `2022.3.62f3`.
- Current checked-in bundles were rebuilt with Unity `2022.3.62f3`; previous bundles came from Unity `2019.4.30f1`.

## Unity Assets

- `Originals/Effects` can be opened and exported with local Unity `2022.3.62f3`.
- `CreateAssetBundles.BuildStandaloneAssetBundles` generates the missing `_Zombieland` bundle-source assets at export time, labels them for the `zombieland` bundle, validates the seven-asset contract, and writes platform bundles.
- The thumper visual wave uses the bundle `Dust` prefab. `scripts/inspect-unity-assets.sh` verifies it is a billboard `ParticleSystem` using `Smoke_thin` with Unity's built-in `Particles/Standard Surface` shader.
- Custom shaders in the generated bundles are `Custom/Metaballs` and `Custom/ZombieBlob`.
- Targeted `.meta` files were restored for `ZombieBlob.mat`, `ZombieBlob.shader`, and `Rimworld.png` to keep historical GUID references stable. Do not broadly start tracking Unity metas without a separate decision.

## Bridge Structure

- Zombieland extension tools live under `Source/BridgeTools/` as partial `ZombielandBridgeTools` files.
- Shared map, settings, tick, cleanup, and description helpers belong in `ZombielandBridgeTools.Common.cs` or another narrow shared helper.
- Debug actions and bridge endpoints share `Source/ZombieRuntimeActions.cs` for zombie spawn/remove, bite/infection, and direct pawn conversion. Keep future test-only runtime mutations there instead of duplicating menu and bridge logic.
- Bridge fixtures that disable generated pawn work should use `DisablePawnWork`; direct `workSettings?.DisableAll()` can log fixture-only errors for some generated pawns.
- `scripts/run-zl-contract.sh` is the preferred repeated-contract runner. It handles GABS state, save loading, dynamic tool resolution, bridge readiness, operation events, compact log evidence, and `_rimBridgeTimeoutMs`.

## Known Risks

- RimWorld 1.6 pawn rendering was substantially refactored. Any patch touching pawn graphics remains suspicious after compile success.
- `Source/ZombieRenderCompat.cs` is a compatibility layer for old `PawnRenderer.graphics` usage. Do not reintroduce graphics preparation in `PawnRenderer.ParallelPreRenderPawnAt`; it can touch map pawn lists off the main thread.
- The suicide bomber overlay is visually restored through explicit `RenderExtras` overlay draw, but it is still a compatibility workaround rather than a 1.6 render-node-native implementation.
- RimWorld 1.6 pawn pathing can leave `Pawn_PathFollower.curPath` null while an async `curPathRequest` is pending. Zombieland movement must not treat that as immediate destination invalidation.
- The old `PathFinder.FindPathNow` `PathFinderNodeFast.knownCost` transpiler remains disabled. Zombie avoidance path costs now apply through per-request `PathRequest.IPathGridCustomizer` snapshots and disposal hooks.
- `ZombieWanderer.processor` is a static coroutine and must stay guarded by long-event/load checks plus per-map validity checks.
- Contamination is easy to lose when game elements are replaced, such as blueprint to frame to building, pawn to corpse, plant to products, or pollution to wastepack. Prefer generalized handoff helpers over case-by-case patches.
- Compile success is not runtime success. Validate mod load, map start, zombie spawning, pawn rendering, infection, corpse conversion, special zombie behavior, and save/load persistence in real scenarios.

## Cluster Summary

- Startup/config/load order: local 1.6 GABS stack works when Harmony is above Core and RimBridgeServer local `1.2.0` is loaded.
- Movement/pathing: avoid-grid and async path jitter have focused contracts; next useful work is a controlled horde scenario through doors/walls.
- Rendering/assets: toxic, spitter, blob, suicide bomber, tar smoke, and thumper wave have focused fixes; next useful work is a real-step sweep on `ZombielandVisualLineup`.
- Special zombies: direct contracts cover many individual effects; next useful work is an end-to-end special zombie scenario covering spawn, movement, combat/effect, death, and cleanup.
- Infection/corpses/serum: core contracts pass; next useful work is a save/load conversion-chain fixture.
- Buildings/items: chainsaw, thumper, shocker, wall push, and doors have focused coverage; next useful work is a combined room/building scenario.
- Contamination: many handoff contracts exist; next useful work is scenario saves for replacement chains rather than adding unbounded endpoint-specific notes.
- UI/settings/debug actions: menu texture and debug action targeting were fixed; next useful work is a persisted-settings scenario.
- Performance: zombie tick and chainsaw allocation work landed; run dense-horde smoke after behavior stabilizes.

## Maintenance Rule

Every five scenario slices, prune `PLAN.md` and this file together. Remove stale next steps, collapse duplicated risks, and keep detailed evidence in commits, scripts, screenshots, or save names rather than expanding these docs into another work log.
