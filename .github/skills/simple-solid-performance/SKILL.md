---
name: simple-solid-performance
description: 'Keep this project simple, SOLID, and fast. Use when refactoring C# code, splitting large classes, reducing complexity, and improving runtime/allocation performance without breaking behavior.'
argument-hint: 'Target symbol or file and desired performance goal'
user-invocable: true
---

# Simple SOLID Performance Refactor

## When to Use
Use this skill when asked to:
- simplify a complex class or method
- apply SOLID principles in this project
- split responsibilities into smaller focused classes
- improve performance while preserving behavior
- add tests that protect refactors

Trigger phrases:
- keep this project simple
- refactor with SOLID
- split this class
- improve performance
- reduce allocations
- optimize without changing behavior

## Outcome
Produce a maintainable implementation with:
- clear responsibilities
- minimal public API churn
- measurable or reasoned performance improvements
- regression safety through targeted tests

## Workflow
1. Identify boundaries first.
- Confirm what must stay stable: public methods, DTOs, response shapes, and behavior.
- Separate what can change freely: private helpers, internal structure, file layout.

2. Baseline current behavior.
- Locate existing tests for the target symbol.
- If gaps exist, add focused tests for currently supported behavior before large refactors.

3. Split by responsibility.
- Keep facade classes thin (orchestrate only).
- Extract parser/validator/mapper/matcher/executor roles into dedicated internal classes.
- Keep class names outcome-oriented and precise.

4. Apply SOLID decisions.
- Single Responsibility: one reason to change per class.
- Open/Closed: extend via new classes instead of modifying stable orchestrators.
- Liskov: preserve method contracts and null semantics.
- Interface Segregation: only introduce interfaces when multiple implementations or testing seams are needed.
- Dependency Inversion: depend on abstractions at boundaries, not inside trivial local flows.

5. Apply performance rules.
- Prefer straightforward, low-allocation logic over abstraction-heavy designs.
- Avoid extra passes over collections when one pass is enough.
- Preserve streaming and async behavior; do not introduce blocking calls.
- Keep hot-path data structures simple and local.
- Do not micro-optimize before correctness and readability are locked.

6. Keep changes minimal and local.
- Avoid unrelated renames or formatting-only edits.
- Move code in small steps and keep compileable states.

7. Validate and regress.
- Build target project.
- Run focused tests for the modified symbol or workflow.
- If broader suite is noisy, report targeted results and known unrelated failures separately.

8. Final quality gate.
- Confirm no unsupported feature was accidentally implied as implemented.
- Confirm docs and names are aligned with current API.
- Summarize: what changed, why it is simpler, and why performance is at least preserved.

## Decision Branches
- If class is large but cohesive:
  keep one class and extract only algorithmic helpers.
- If class has mixed concerns:
  split into facade plus internal components by responsibility.
- If test infrastructure is unstable:
  run targeted tests and document external failures without blocking local refactor validation.
- If performance and readability conflict:
  choose the clearest approach that avoids obvious overhead, then profile before deeper optimization.

## Completion Checklist
- Public behavior preserved or explicitly approved for change.
- Responsibilities clearly separated.
- No dead code left behind.
- Build succeeds for modified project.
- Focused regression tests pass for touched behavior.
- Summary includes risks, assumptions, and next improvements.

## Example Prompts
- Refactor StreamJsonExtractionExtensions with this skill and keep behavior unchanged.
- Split this service into small SOLID classes and keep allocations low.
- Apply this skill to simplify parser and matcher code, then run targeted tests.
