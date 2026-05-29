# Zombieland 1.6 Port Plan

This is the active goal document for the Zombieland RimWorld 1.6 port. Use it as the entry point for future work. Keep it short, update it after each scenario slice, and move detailed technical notes to `docs/PORTING.md` only when they change future decisions.

## Active Goal

Continue the RimWorld 1.6 Zombieland port through coverage-driven scenario slices. Use stable prepared save games, real game tick/frame stepping, source-derived timing, GABS/RimBridge automation, and old 1.4 references where fidelity matters. Keep bridge tools generic and maintainable, commit each stable scenario slice, and keep this file as the source of truth for current strategy.

## Work Cycle

1. Read `AGENTS.md`, this file, and the relevant part of `docs/PORTING.md`.
2. Pick the next scenario by risk from the coverage matrix, not randomly.
3. Load a stable scenario save. Use `EMPTY.rws` only as an immutable base.
4. Prefer `rimworld/step_game_ticks` for time-based or visual behavior. Use direct bridge tick loops only for narrow deterministic contracts where rendering/frame behavior is irrelevant.
5. For each scenario, use source-derived tick windows and old 1.4 references when preserving legacy behavior matters.
6. Verify with GABS, bridge output, log summary, and visual confirmation when applicable.
7. Update the coverage matrix and save registry, then commit the stable slice.

## Scenario Save Registry

| Save | Role | Mutability | Notes |
| --- | --- | --- | --- |
| `EMPTY.rws` | Clean 75x75-ish empty base map for fast, isolated fixtures | Immutable | Load, populate, and save under a new scenario name. Never save over it. |
| `ZombieTest.rws` | Broad legacy live repro map with existing zombies/settings | Stable reference | Use for broad behavior rechecks and known repros, not unrelated new fixtures. |
| `ZombielandVisualLineup.rws` | Visual comparison save for special zombie types | Stable reference | Use for rendering checks against the old 1.4 lineup. |

New scenario saves should use `ZL_<cluster>_<purpose>_base.rws`, be registered here, and be treated as immutable unless explicitly marked disposable.

## Coverage Matrix

| Cluster | Current status | Next useful scenario |
| --- | --- | --- |
| Startup, load folders, config | Load structure and GABS launch are working; Harmony ordering issue was user-corrected | Recheck from clean start before release packaging |
| Core zombie movement/pathing | Async path jitter and avoid-grid costs fixed with contracts | Scenario-level horde movement through doors/walls on a controlled save |
| Rendering and Unity assets | Toxic, spitter, blob, suicide bomber, tar smoke, and thumper have focused fixes | Real-step visual sweep on `ZombielandVisualLineup` plus asset-backed effects |
| Special zombies | Many direct contracts exist for tanky, albino, miner, healer, electrifier, toxic, dark slimer | End-to-end special zombie scenario: spawn, move, attack/take damage, special effect, death/cleanup |
| Infection, corpses, serum | Bite/cure/serum/double-tap/rope flows covered by contracts | Scenario save with corpse conversion chain across save/load |
| Incidents and director | Scheduling, letters, random weights, special spawning covered | Player-facing incident wave on clean map with real stepping |
| Buildings and items | Chainsaw, thumper, shocker, wall push, doors covered by focused contracts | Room/building scenario combining pathing, wall push, shocker, and player structures |
| Chainsaw | Five chainsaw contracts rechecked after optimization | Real-step visual/combat pass if animation or player-facing feedback looks suspect |
| Contamination | Broad handoff coverage exists but bridge file is very large | Consolidate scenario saves for replacement chains: blueprint/frame/building, corpse, stack, plant, pollution |
| UI, settings, debug actions | Menu texture and debug action targeting fixed | Settings surface scenario: toggle critical settings and verify persisted effect |
| RimConnect | Zombie actions and super drop covered | Recheck only after API or bridge surface changes |
| Save/load persistence | Some visuals and zombie render tree load behavior fixed | Explicit save/load scenario for zombies, contamination, assets, and active effects |
| Performance | Zombie tick and chainsaw allocations trimmed | Run a dense horde perf smoke after behavior stabilizes |

## Tooling Rules

- Use GABS `rimworld` for live tests. Do not directly launch RimWorld as a fallback.
- Stop RimWorld through GABS before deploy builds.
- Never run live GABS/RimWorld contracts in parallel.
- Use `scripts/check-bridge-health.sh` after crashes, reconnects, timeouts, or external RimWorld launches.
- Use `scripts/run-zl-contract.sh` for focused contracts and `scripts/step-real-game.sh` for controlled real-frame stepping through RimBridgeServer 1.2.0's `rimworld/step_game_ticks`.
- Use `scripts/export-unity-assets.sh`, `scripts/validate-unity-assets.sh`, and `scripts/inspect-unity-assets.sh` for Unity bundle work.

## Cleanup Rules

- This file should stay decision-oriented. Remove completed next steps when they no longer affect future work.
- `docs/PORTING.md` should hold stable commands, decisions, risks, and compact cluster summaries, not a chronological work log.
- Bridge code must not grow by copy-paste. If a new endpoint duplicates setup logic, first extract or reuse a shared helper.
- Every five scenario slices, prune stale notes and merge duplicated risks before continuing.
