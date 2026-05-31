This is a project for a Mod for the game RimWorld. Last time it was worked on and was fully functional was with RimWorld 1.4. Since then, RimWorld was updated twice with major refactoring of the game code, first to 1.5 and then to 1.6 which makes this mod very incompatible with the latest version of the game.

This mod is not a small simple mod. It is almost a game in itself. It uses a lot of complex mechanics and has lots of diverging concepts that are different or change the typical game functions. As such, several programmers have tried to upgrade and refactor it and have not succeeded or only made partial changes.

Because of this, it is mandantory to work in clear defined units, keep changes minimal and efficient. At first, the mod will not start at all. It will have many many problems and it won't compile at first but even after making it compile it will most likely start with many errors. Once those are addressed, the next expected thing will be that even without errors, the functionality will most like not work, not show, do something differently. This is the hard part because RimWorld is a complex game with many different situations, environments, pawns and interactions and Zombieland introduces so many features and functions that it will be hard to test each and every of them with all these aspects. That requires a lot of testing and maybe a full test strategy.

Your job as an agent is to keep track of this situation, not get lost or off track, find the specific fixes that make Zombieland work again, test it with as many different scenarios as possible.

While I am confident in your capabilities, it is very important that you find effective ways to work. That includes working evidence based - may it be on the source code level where you use the MCP decompiler to learn about RimWorld 1.4 and 1.6 code, or use the MCP GABS server to control RimWorld during test cycles. Even the test automation will take too much tokens, context or time if not automated in itself so you need to create a collection of saved game states and saved lua scripts that run specific sequences like load, set up, test in one go to save time.

You will also need to not get down the rabbit hole of quick decisions on how to adapt to the structural changes that 1.4->1.6 brings with it. Pawns and aspects of them are rendered completely different. Zombieland does a lot of Harmony patches and its not just about making them compile again but also finding out if some of them are still 'the right way to do it' and maybe need to be redesigned. There are new pawn types and behavious that need not only a refactoring but decisions on how the Zombieland logic should behave on these new things.

So go ahead and create a work cycle that keeps track of the overall progress, the problems, the decisions and the pitfalls and make sure it just does not grow endlessly. I think you should build up some meta rules similar to i.e. "we need to clean out outdated and useless information/guidelines from our collection of documents regularily". Or that it is maybe wise to keep a collection of architectural decissions that we also revisit sometimes. And how to improve the code, test, fix cycle. Or how to track token consumption to avoid spending all the weekly allowance in the first two days by working efficient, removing token exploding files/structures and minimizing context polution (no compiler output, just ok or error for example).

Rules:

- do not waste time formatting the code too much (a bit is ok, but it is not top priority)
- automate repeated tasks with scripts (either bash or lua inside GABS)
- working small iterations is good but has the problem that the overall ceremony is slow and uses a lot of tokens. find a balance
- find a way to analyse the rimworld log in a token efficient way (with a script that de-dups errors/stacktraces)
- I will run in in high-thinking and non-fast mode to prevent hitting the token limit. if you have a particular har problem, find a way to run a sub-agent with xhigh-thinking on a limited problem/time.
- use regionshot efficiently and use it only if you really need to (saves tokens)
- you have a tendency to double check or multi-compile things. find a way to have a clear work cycle that is not wasteful
- keep track of what you are doing but make sure you do not simply pile up work log after work log
- make sure you read the *important* work documents regularily - they have no purpose if you only creete but never read them

Operating model:

- Static/decompiler pass first. For every Harmony patch group, prove the target exists, the signature still matches, closure/delegate targets still have the same role, and the patch still makes semantic sense in RimWorld 1.6 before launching the game. This catches stale targets and 1.4-era assumptions early.
- Generic bridge primitives second. Prefer broad reusable tools such as spawn thing, spawn pawn, set contamination, select, hover, run ticks, save, load, list logs, screenshot, and read semantic state. These are the stable automation surface.
- Custom bridge contracts only when they become reusable evidence. A contract is justified when it covers a named matrix row or scenario cluster. A one-off tool that only proves one hunch should usually be deleted, folded into a generic helper, or promoted into a broader scenario contract afterward.
- Scenario fixtures over microscopic contract sprawl. Use isolated contracts to find and fix narrow behavior, then escalate to combined save-load fixtures that prove the behaviors coexist in one map/session.
- Treat logs and screenshots as first-class evidence. For UI, rendering, quests, and native crashes, no warning-or-higher logs after build/save/load/runtime steps is a useful regression signal, not fluff.
- Use hard session boundaries. A good session ends with build status, tests run, save names touched, files changed, temporary diagnostics removed or explicitly marked, docs updated, and next blocker or next slice.
- Do not add artificial pauses to stretch token usage unless the user explicitly asks again.
- Use `coverage/ZL_COVERAGE_INDEX.tsv` as a planning index when starting a new coverage slice or when the next target is unclear. It is advisory, not authoritative: verify rows against local source, `TEST_COVERAGE.md`, `TEST_SCENARIOS.md`, `TEST_PATCH_AUDIT.md`, local inventory scripts, decompiler evidence, logs, and live RimBridge results before claiming coverage. Do not pivot away from an active named scenario solely because the index suggests another high-priority row; finish or explicitly park the active scenario first.

Anything else should be solved in the same spirit as what I just wrote above.
