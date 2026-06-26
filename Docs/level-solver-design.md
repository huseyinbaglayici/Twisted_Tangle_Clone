# Twisted Tangle — Level Solver & AI Generation Design

Two editor-time tools:
1. **Auto-solver** — tells a designer whether a level is solvable and how hard.
2. **AI level-generation** — designer provides context, gets back a playable level as JSON, then verifies and commits it.

Everything runs at editor time. Nothing here is called at runtime. AI generation is the main goal; the solver exists to validate what the AI produces.

---

## 1. The Model

A level is a grid of **holes**. **Pins** sit on holes. A **rope** connects exactly two endpoint pins and is stored as a polyline (`RopeData.Path`). The path's first and last entries are the movable endpoint pins; any middle entries are either real inner pegs (physical pins the rope wraps around) or virtual bend points (routing-only, no physical pin).

### 1.1 Waypoint types

A `RopeWaypoint` has a grid coordinate and an `IsBendPoint` flag:

- `IsBendPoint = false` — a **real physical peg**. The rope physically touches this pin. It contributes to crossing detection and blocks other ropes.
- `IsBendPoint = true` — a **virtual bend point**. Routing-only; no physical pin at this cell. It does not block other ropes and is excluded from bend-on-segment crossing detection.

This distinction drives several solver decisions downstream.

### 1.2 Moving a pin

A move relocates one **movable endpoint pin** to an empty hole. Two hard constraints:

- **Reach.** Every rope attached to the moved pin must keep its adjacent waypoint within `max(|dx|, |dy|) <= 3` (Chebyshev). For a **straight rope** (2-waypoint path), both endpoints must be within reach of each other. For a **bent rope**, each endpoint must be within reach of its immediately adjacent inner waypoint — the endpoint-to-endpoint distance can legitimately exceed 3 if a bend sits between them.
- **No solid ropes.** Ropes do not block each other mid-move. Only the final state's crossing configuration decides resolution.

Layer is a **soft preference**, not a constraint. The solver tries higher-layer pins first, so solutions read top-down. It does not prevent moving a low-layer pin.

### 1.3 Locked pins

A pin is locked when its `EntityDefinitionSO.Tags` contains `"nailed"` or `"locked"`. The editor collects these via `LevelCreator.NailedCells()` and passes them to the solver as `SolveOptions.LockedCells`. The solver never generates moves for locked pins.

A rope with two locked endpoints can only be cleared by moving **other ropes** away. If that is geometrically impossible, the level is unsolvable.

---

## 2. Crossings

Three categories of crossing exist, all detected in `CrossingSolver` and replicated inside `LevelSolver.BuildCrossings`.

### 2.1 Segment-segment

Two rope segments from different ropes intersect strictly in their interiors (both parameters in (0,1)). Segments that only touch at a shared pin endpoint are not counted. This is the standard case.

### 2.2 Pin-crossing (`IsPinCrossing = true`)

Rope A's inner waypoint (real or virtual) shares its grid cell with rope B's **endpoint pin**. Even a virtual bend routes through that exact cell, so it is physically blocked by B's pin.

**Special case — virtual bend in the same direction:** if the inner waypoint is a virtual bend (`IsBendPoint = true`) and both ropes leave the shared cell in the same direction (cross product < 0.1 and dot product > 0), they run alongside each other and this is not a topological crossing.

Pin-crossings are **unresolvable by peeling** — both rope A and rope B are marked as stuck in the peel model (both `underCount` values are incremented). A pin must physically move to break the shared-cell constraint.

### 2.3 Bend-on-segment (`IsBendOnSegment = true`)

Rope A's inner waypoint is a **real physical peg** (`IsBendPoint = false`) that lies strictly on the interior of one of rope B's segments. Virtual bends are skipped here — they have no physical presence.

For this type, only rope B's endpoint pins are candidate moves (moving rope A's endpoints cannot fix it, because the inner waypoint is fixed).

---

## 3. Over/Under and Peelability

### 3.1 ResolveOverUnder

`CrossingSolver.ResolveOverUnder` assigns an over/under value to every crossing. For each **rope-pair**, crossings are sorted along the lower-id rope's path, then **alternated**: the first crossing is seeded by Layer (higher layer = on top, ties broken by rope id); each subsequent crossing in the pair flips. Manual `CrossingOverride` entries flip individual crossings on top of this alternation.

This alternation model is what creates genuine braid tangles. Without it, every tangle would be trivially peelable (a stack).

### 3.2 PeelResidual — the real win condition

`CrossingSolver.PeelResidual` simulates the game mechanic: repeatedly lift any rope that is on top at all of its remaining active crossings (or has no crossings). Each time a rope is peeled, it removes its crossings from the active set and decrements the `underCount` of its crossing partners.

**Return value:** number of crossings still active when no more ropes can be peeled. Zero means the tangle is **fully separable** — the level is solved.

A single clean over-crossing (rope A strictly on top of rope B, no other entanglement) peels immediately: PeelResidual = 0. This is considered solved even though a geometric crossing exists.

Pin-crossings are never resolvable by peeling — both participating ropes are treated as stuck (`underCount` incremented for both sides regardless of which is geometrically "over").

The optional `unpeeled` output parameter receives the set of rope ids still stuck when peeling stalls — this is used by the solver to identify which pins are worth moving.

---

## 4. The Solver

**File:** `Assets/Scripts/Editor/Solver/LevelSolver.cs`

### 4.1 Formal model

```
State  = int[] where state[movableSlot] = encoded cell index of that pin
Move   = relocate one movable pin to an empty hole, reach-legal per §1.2
Goal   = PeelResidual(BuildCrossings(state)) == 0
Cost   = number of moves
```

Ropes are fixed. Only endpoint pin positions change. Crossings are recomputed from positions on demand; they are never stored in the state.

### 4.2 State encoding

Each movable endpoint pin has a **slot index**. The state array has one entry per movable pin: `state[slot] = y * gridWidth + x`. Locked pins are excluded from the state; their cells are pre-computed into `fixedOccupied`. A state's hash key is the comma-joined array string.

### 4.3 Move candidates — CrossingSlots

At each search node, the solver does not consider all movable pins. It calls `CrossingSlots(state)`:

1. Build crossings for the current state.
2. Call `PeelResidual` with the `unpeeled` output to get the set of rope ids still stuck.
3. For each crossing where at least one rope is stuck:
   - **Segment-segment:** add movable endpoints of both stuck ropes.
   - **Pin-crossing with a virtual bend:** add movable endpoints of both stuck ropes (either rerouting solves it).
   - **Pin-crossing with a real inner peg:** add only the movable endpoint of the stuck rope B (rope B's endpoint is the one that must move away from the shared cell).
   - **Bend-on-segment:** add only rope B's movable endpoints.
4. Sort result by descending layer (higher-layer pins first).

This filters the candidate set to only the pins that can actually break the unpeelable core, keeping branching small.

### 4.4 Algorithm

Best-first (A*-ish) search:

- `h = TangleResidual(BuildCrossings(state))` — peel residual of the current state, not raw crossing count.
- `f = g + h`.
- Priority queue is a binary min-heap. Ties are broken by insertion order (FIFO within same priority), so an equally-good solution that starts from a top-layer rope is returned first.
- Each candidate: target cell must be empty (not in `fixedOccupied` or current movable positions); must pass the reach check.
- Visited set rejects states already seen.
- Search terminates when `h == 0` (goal) or when `g >= MaxMoves` or expansion count hits `MaxExpansions`.

### 4.5 Reach check details

`ReachOk(state, node, targetCell)` checks two neighbor lists built at construction time:

- `reachNodes[node]` — other movable endpoint pins this node must stay within reach of. For a straight rope this is the other endpoint (dynamic, from state); for a bent rope this is skipped at the endpoint level (the adjacent inner waypoint is the reach anchor).
- `reachFixed[node]` — static cell encodings (locked endpoints or inner waypoints) this node must stay within reach of.

Bent ropes: each endpoint's reach anchor is its immediately adjacent inner waypoint (`rope.Path[1]` for the first endpoint, `rope.Path[^2]` for the last), added to `reachFixed` or `reachNodes` depending on whether that waypoint is itself movable.

### 4.6 Outputs (`SolveResult`)

| Field | Meaning |
|---|---|
| `Solvable` | A solution was found within budget |
| `Moves` | Length of the found solution (best-first; not guaranteed minimal) |
| `InitialCrossings` | Raw crossing count before search |
| `InitialTangle` | Peel residual before search (the real tangle depth) |
| `OverStretchedRopes` | Ropes whose **adjacent-waypoint** segment already exceeds reach in the authored layout |
| `ExpandedNodes` | Nodes expanded |
| `HitExpansionLimit` | If true, the result is inconclusive rather than a definite "unsolvable" |
| `Solution` | List of `SolveMove` (from-cell, to-cell, which rope) |

`OverStretchedRopes` counts per-segment violations (not endpoint-to-endpoint), consistent with how reach is checked during search.

---

## 5. Difficulty

Two independent difficulty systems:

### 5.1 Solver difficulty (move-count bucket)

Used in the Solve panel (`LevelCreator.RunSolve`). Based on the solution length:

```
<= 2 moves  → Easy
<= 5 moves  → Medium
>  5 moves  → Hard
```

Simple and tunable. Shown only when the solver finds a solution.

### 5.2 Validator difficulty (weighted score)

Used in `LevelValidator.BuildMetrics`. A formula over static properties of the level (no search needed):

```
score = crossings×1.0 + ropes×0.5 + colors×1.5 + totalPathLength×0.2 + overrides×1.0
score <  10  → Easy
score <  24  → Medium
score >= 24  → Hard
```

Color variety is the strongest signal (weight 1.5). Shown in the Validation panel alongside structural errors. Separate from solver difficulty; the two can disagree.

---

## 6. AI Level Generation Integration

### 6.1 Flow

1. Designer sets grid size, selects entity types, picks difficulty, optionally places locked pins.
2. "1 · Copy prompt" — `LevelAiGenerator.BuildManualPrompt` builds a self-contained text prompt with rules + exact JSON shape.
3. Designer pastes into any AI chat (Claude, Gemini, ChatGPT, …), copies the JSON response back.
4. "2 · Import JSON" — `LevelAiGenerator.TryParseLevelJson` extracts the JSON object (tolerates code fences and surrounding prose), parses with `JsonUtility` into a `LevelDataSO`.
5. Solver runs, validation runs, designer edits as needed, then commits.

No API key, no vendor dependency, one level at a time.

### 6.2 Output contract (JSON shape)

```json
{
  "gridWidth": <int>, "gridHeight": <int>, "timeSeconds": <int>,
  "gridEntities": [ { "x": <int>, "y": <int>, "typeId": "<allowed id>" } ],
  "ropes": [ { "ropeId": <int>, "color": "#RRGGBB", "layer": <int>,
               "path": [ { "x": <int>, "y": <int> } ] } ]
}
```

Every rope endpoint and waypoint must match an entity in `gridEntities`. The AI prompt includes the reach rule (`max(|dx|,|dy|) <= 3` per segment), the nailed-pin constraint, and the solvability requirement. Parsed with `JsonUtility` (no Newtonsoft); field names must match the DTO exactly.

### 6.3 Settled decisions

- **Provider-agnostic:** copy-paste prompt, no live API call.
- **Entity selection:** include/exclude toggle per entity type; only checked types enter the prompt. Auto-rebuilds on asset change plus a manual Refresh button.
- **Nailed pins:** types tagged `"nailed"`/`"locked"` appear in the prompt as immovable and are passed to the solver as `LockedCells`.
- **Difficulty targeting:** Easy/Medium/Hard string in the prompt; validated post-import by solver move count.
- **v2 (not started):** per-entity frequency (Rare/Some/Many) in the prompt.

---

## 7. Implementation Status

| File | Status |
|---|---|
| `Assets/Scripts/Editor/Solver/LevelSolver.cs` | Done |
| `Assets/Scripts/Editor/Geometry/CrossingSolver.cs` | Done |
| `Assets/Scripts/Editor/Validation/LevelValidator.cs` | Done |
| `Assets/Scripts/Editor/Generation/LevelAiGenerator.cs` | Done (prompt builder + JSON parser) |
| `Assets/Scripts/Editor/LevelCreator.cs` | Done (Solve button, AI section wired) |

---

## 8. Open Items

1. **Reach metric** — Chebyshev assumed; confirm or switch to Euclidean/Manhattan (one-line change in `WithinReach`).
2. **Time ↔ move-budget mapping** — solver reports move count; how that maps to `TimeSeconds` is not yet defined.
3. **Per-crossing overrides / layer cycles** — `CrossingOverrides` are passed to the solver but full deadlock detection via layer cycles is not implemented.
4. **Rope wrap semantics** — solver uses straight endpoint-to-adjacent-waypoint segments; revisit if authored wraps affect game feel.

---

## 9. Sources

- Twisted Tangle — App Store: https://apps.apple.com/us/app/twisted-tangle/id6447757125
- Twisted Tangle — CrazyGames: https://www.crazygames.com/game/twisted-tangle-nmt
- Rollic Help Center — How to play: https://rollic.helpshift.com/hc/en/11-twisted-tangle/faq/254-how-to-play-twisted-tangle/

The 3-unit reach limit, the layer preference, and the peel-residual win condition are this project's design choices, not the original game's mechanics.
