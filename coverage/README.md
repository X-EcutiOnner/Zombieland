# Coverage Index

This directory contains an advisory coverage ledger generated from an external methodology pass. It is a planning aid, not the source of truth.

Authoritative project state remains:

1. `TEST_COVERAGE.md` for the current coverage matrix and method.
2. `TEST_SCENARIOS.md` for durable scenario definitions and runtime evidence.
3. `TEST_PATCH_AUDIT.md` and `scripts/coverage-inventory.sh` for local Harmony patch inventory.
4. Current local source, local save files, RimWorld logs, decompiler member checks, and live RimBridge/GABS results.

Use `ZL_COVERAGE_INDEX.tsv` to choose and de-duplicate future work:

- `row_type` separates feature, patch, scenario, def, integration, bridge, and negative-space rows.
- `owner_cluster` keeps related surfaces together so a session can work a coherent slice.
- `required_passes` describes the evidence still needed before a row can be called covered.
- `evidence_state` and `open_questions` help identify gaps, but must be refreshed against current local docs and runtime state before acting.
- `duplicate_guard` explains what the row owns, which helps avoid retesting behavior already owned by another row.

Operating rule: consult this index at the start of a new coverage slice or when the next target is unclear. Do not interrupt an active named scenario just because the index contains a different high-priority row. Finish or explicitly park the current scenario with a hard session boundary first.

