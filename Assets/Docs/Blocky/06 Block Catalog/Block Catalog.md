---
tags: [block-catalog]
---

# Block Catalog (MVP parity)

Source: [[Welcome|TDD §9]]. Semantics column there is normative — it defines yield behaviour.

## Events
`event.when_play_clicked` · `event.when_looked_at` (`angle_threshold`, shared raycast) · `event.when_key_pressed` (`key`, rising edge, restart-on-retrigger) · `event.when_collided` (`tag_filter`, from collision relay)

## Motion
`motion.move_forward`, `motion.turn_direction`, `motion.rotate_axis` (all: `duration = 0` → instant, else `Retry` until elapsed) · `motion.set_position`, `motion.set_rotation` (instant)

## Looks
`looks.change_color` (`color`, `duration`) · `looks.set_visible` (`visible`) · `looks.set_scale` (`x`,`y`,`z`,`duration`)

## Control
`control.wait` (`seconds`, sets Sleeping+WakeAt) · `control.if` (1 branch) · `control.if_else` (2 branches) · `control.repeat` (`times`) · `control.repeat_forever` · `control.repeat_until` (`condition` checked before each iteration)

`condition` params are `Bool` literals in v1, become `Reporter` slots later with no schema change (see [[02 Data Model/Data Model|Data Model]]).

## Status
**Full v1 catalog implemented and live as real assets** (Milestone 7) — all 18 blocks exist as `BlockDefinition` `.asset` files under `Assets/Resources/Blocks/`, generated via an Editor `eval` script (not hand-placed one by one) and verified to load through `BlockyRuntime.Registry`: `BlockRegistry.LoadFromResources()` returns opcodes=18, and `OpTableBuilder` binds all 14 non-trigger ones (4 triggers correctly unbound).

**Op implementation pattern worth remembering:** any duration-based op converges using only the single `float` in `Thread.Scratch` — no separate "start value" memory exists. Two techniques cover every case in the catalog:
- **Relative delta** (`move_forward`, `turn_direction`, `rotate_axis`): apply `amount * step / duration` as an *increment* each tick — no start value needed because the change is inherently relative.
- **Remaining-fraction convergence** (`change_color`, `set_scale`, both target an *absolute* value over time): each tick, `Lerp(current, target, step / remaining)` — this converges exactly to target when `remaining` hits 0, without ever remembering where it started. See [[04 Runtime/Runtime VM|Runtime VM]] and `ChangeColorOp`'s doc comment.

`set_position`/`set_rotation` have no duration param at all (instant-only, per the TDD catalog table) — no convergence logic needed there.
