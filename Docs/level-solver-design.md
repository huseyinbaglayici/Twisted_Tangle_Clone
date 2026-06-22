# Twisted Tangle — Level Solver & AI Generation Design

> **What this is.** The single source of truth for two editor-time tools in this project:
> 1. an **auto-solver** that tells a designer whether a level is untangle-able and roughly how hard, and
> 2. an **AI level-generation** flow (the project's centerpiece) where a designer hands the AI some basic
>    content and gets back a playable level to review, fix, and commit.
>
> **Scope & intent.** This is a **portfolio project**, not production. The goal is to learn and correctly
> demonstrate the AI level-generation integration at a **basic, industry-standard** level — kept simple on
> purpose. Everything runs **at editor time**; nothing here is called by the game at runtime, and there is
> no bulk/rapid generation.
>
> **Audience of this doc.** The engineer/agent building these tools, and the designer using them.
> Status tags: ✅ done · 🟡 planned · ⚠️ open question.

---

## 1. The Model (game rules the solver assumes)

A level is a small grid of **holes**. **Pins** sit on holes. A **rope** connects exactly two pins and is
treated as a **straight segment** between them. The puzzle is solved when **no two ropes cross**.

### 1.1 Board & pieces
- Pins live only on discrete grid holes. A rope connects exactly two pins.
- A pin may belong to several ropes (an "octopus" pin; degree is small in practice).
- `pinA`/`pinB` is **just a label** (the rope's first/last authored waypoint). The two ends are symmetric —
  the solver treats them identically. We reason **rope-centric**: a rope is solved when it crosses nothing.
- A rope is stored as a polyline (`RopeData.Path`) whose middle waypoints describe an initial visual wrap.
  The solver uses only the **two endpoints** (straight edge); middle waypoints are ignored (**assumption A**,
  revisit only if wraps ever affect play).

### 1.2 Moving a pin
A move grabs one pin and drops it on an **empty hole**, subject to two hard rules and one soft preference:

- **Reach (hard).** Every rope attached to the moved pin must keep its two pins within **3 units**.
  Distance is direction-independent → **Chebyshev / king-move**: `max(|dx|, |dy|) ≤ 3`.
  *(Metric is isolated in one helper; swappable to Euclidean/Manhattan if needed.)*
- **No solid ropes (hard).** Ropes do not block each other mid-move; only the **final** crossing state
  decides resolution. (So you may park a rope in a crossing state on the way to untangling.)
- **Layer = soft preference, not a rule.** A bottom rope *can* be moved too, but a player generally starts
  from the **top**. The solver therefore tries higher-`Layer` ropes first, so solutions read top-down.
  Over/under uses the global `Layer` (ties broken by rope id); per-crossing overrides are ignored for now.

Because of the reach limit, relocating a rope far away is done by walking its two pins in turn (**inchworm**).

### 1.3 Win condition & limit
- **Win:** no two ropes cross.
- **Limit:** a move count (this project surfaces it as a **timer** in the UI). ⚠️ The time↔move-budget
  mapping is still open; the solver reports a move count and the budget is derived from it.

### 1.4 Locked / needle pins (core)
- A **locked pin cannot be moved** (fixed position; the solver never relocates it).
- Designers place locked pins as **anchors/context**: "these x, y, z are fixed — build the level around them."
  AI generation takes them as fixed input; the solver treats them as fixed nodes.
- They directly affect solvability — locked pins can force unavoidable crossings, making a level unsolvable.
- Concrete current form: a **needle pin** (fully immovable; a difficulty modifier). Movement-restriction is a
  family of future difficulty features, so movability is modeled generically (extensible to per-pin allowed
  cells later).
- ✅ **Representation (implemented) — type-based:** a pin is nailed/locked when its `EntityDefinitionSO.Tags`
  contains `"nailed"` or `"locked"` (the existing `tags` field). The editor collects pegs of tagged types and
  passes them to the solver as `LockedCells` (`LevelCreator.NailedCells()`); the AI prompt lists those typeIds as
  immovable. The default `pin.nailed` is now created with the `"nailed"` tag (existing `pin.nailed` assets need the
  tag added in the Inspector). A rope may have 0, 1, or 2 nailed endpoints; the solver handles all three (a
  two-nailed rope is cleared only by moving other ropes away — else it reports unsolvable).

### 1.5 Other special pins (later phase) 🟡
- **Octopus** — one pin carries multiple ropes (degree 2); already handled as a normal node.
- **Key / Lock** — untangling the key's ropes opens the lock → a sequencing constraint.
- **Barrier** — a static obstacle a rope may not cross → a forbidden region in the crossing test.

---

## 2. The Solver

### 2.1 Formal model
```
State = assignment of each movable pin → a hole
Move  = relocate one movable pin to an empty hole, if it is reach-legal (1.2)
Goal  = zero crossings
Cost  = number of moves
```
Ropes (edges) are fixed; only pin positions change. Crossings are **derived** from positions, never stored.
States are hashed so the search never revisits a configuration.

### 2.2 Why it stays tractable
- **Straight ropes ⇒ any two ropes cross at most once**, so the tangle is just a *set of pairwise
  crossings* (not knot theory).
- Only pins **involved in a crossing** are worth moving → small branching.
- Plenty of empty holes ⇒ a crossing-free arrangement almost always exists; the difficulty is the move count.

### 2.3 Algorithm (best-first search)
1. Build a graph: nodes = endpoint pins, edges = ropes.
2. Best-first (A*-ish) search. Heuristic `h = current crossing count`; `f = g + h`.
3. Generate moves **top-layer first**; the priority queue breaks ties by insertion order, so an
   equally-good solution that starts from the top rope is the one returned.
4. Each candidate move must land on an empty hole and pass the reach check.
5. A visited-set skips repeats; a move budget + an expansion cap keep it bounded.

Atomic operation: a **segment–segment intersection test** (reused from `CrossingSolver.SegmentsIntersect`).

> Note: a formal **planarity pre-check** (reject non-planar graphs outright) is a possible optimization but
> is **not** implemented — the bounded search already answers "solvable within budget?", which is enough at
> this scope.

### 2.4 Outputs
- **Solvable?** — a solution was found within the move/expansion budget. If the cap was hit, the result is
  *inconclusive* rather than a definite "no".
- **Solution steps** — each step names the rope and its from→to hole (e.g. `Rope 2: (2,5) → (0,0)`).
- **Diagnostics** — initial crossings, ropes that start over the reach limit, nodes expanded.

### 2.5 Difficulty (kept deliberately simple)
A plain **move-count bucket**, the most common industry proxy: `≤2 → Easy`, `3–5 → Medium`, `≥6 → Hard`
(thresholds are one-liners and tunable). No calibration or multi-signal formula — that would be
over-engineering for this project. Richer signals (branching, locked-pin pressure) can be added later if a
real need shows up.

---

## 3. AI Level Generation Integration (the centerpiece) 🟡

The reason the solver exists: let a designer generate a level with AI in the editor, then verify and fix it.

### 3.1 Flow
1. Designer provides **basic content/context**: grid size, available entity types & palette, any **locked
   pins** to build around, and a **target difficulty**.
2. The editor builds a prompt ("Copy prompt"); the designer pastes it into **any AI chat** (Claude, Gemini,
   ChatGPT, …), then copies the AI's JSON answer back ("Import JSON").
3. The pasted JSON is parsed into a `LevelDataSO` and loaded into the editor.
4. The **solver** reports solvable? + difficulty; structural **validation** catches malformed output.
5. The designer edits as needed and **commits** the level.

Human-in-the-loop, one level at a time. No runtime generation, no bulk runs.

### 3.2 Output contract
The model's JSON must map cleanly onto the existing data model: `gridWidth/Height`, `timeSeconds`,
`pegs[]` (coord + type id), and `ropes[]` (ordered pin path + color + layer). The generation prompt carries
the same rules this doc defines, so generated levels respect reach, locked pins, etc.

### 3.3 Decisions (settled)
- **Provider-agnostic, manual, free:** the system isn't tied to any AI vendor. Editor **"Copy prompt"** →
  paste into **any AI chat** (Claude, Gemini, ChatGPT, …) → paste the JSON answer back → **"Import JSON"**.
  No API key, no cost. (A live in-editor API call was intentionally dropped to stay vendor-neutral and free —
  the prompt + JSON parser are all that's needed, and they work the same with any model.)
- **Reliable JSON:** the prompt inlines the exact JSON shape; parsed with Unity's `JsonUtility` (no Newtonsoft),
  tolerating ```json fences / surrounding prose.
- **Validation loop:** parse → `LevelValidator` + `LevelSolver` → show solvable?/difficulty → designer edits → commit.
- **Difficulty targeting:** pass Easy/Medium/Hard in the prompt; accept/reject via the solver's move-count bucket.
- **Entity selection (v1):** an include/exclude toggle per entity type (all on by default); only checked types
  go into the prompt. The list auto-rebuilds when types change, plus a Refresh button for externally-added assets.
  **v2 (later):** per-entity appearance frequency (Rare/Some/Many) — it strongly affects difficulty, so kept separate.
- **Nailed pins:** types tagged `"nailed"`/`"locked"` are sent to the AI as immovable and to the solver as `LockedCells`.

---

## 4. Implementation Status

- ✅ `Assets/Scripts/Editor/Solver/LevelSolver.cs` — graph build, reach-limited best-first search,
  layer-preference tie-break, tiny min-heap. Returns solvable? / moves / crossings / over-stretched ropes /
  named solution steps.
- ✅ `Assets/Scripts/Editor/LevelCreator.cs` — a **Solve** button (Solver section) showing the result, the
  simple difficulty bucket, and the named untangle steps.
- ✅ `Assets/Scripts/Editor/Validation/LevelValidator.cs` (pre-existing) — structural validation + its own
  static difficulty score. May later defer its difficulty to the solver's move-based one.
- 🟡 AI level-generation flow (section 3) — not started; the next major piece.

---

## 5. Open Items
1. ⚠️ **Reach metric** — Chebyshev assumed; confirm vs Euclidean/Manhattan (one-line change).
2. ⚠️ **Locked-ness representation** — `PegData.Locked` flag vs a "fixed" entity type. Solver is decoupled
   (takes a locked set as a parameter).
3. ⚠️ **Time ↔ move-budget mapping** (1.3).
4. 🟡 **Per-crossing overrides / layer cycles** — would enable true deadlock detection; ignored for now.
5. ⚠️ **Rope wraps (assumption A)** — solver uses straight endpoint-to-endpoint edges; revisit if authored
   wraps ever affect play.
6. 🟡 **AI generation phase** (section 3) — the main open design.

---

## 6. Sources (real-game research)
- Twisted Tangle — App Store: https://apps.apple.com/us/app/twisted-tangle/id6447757125
- Twisted Tangle — CrazyGames: https://www.crazygames.com/game/twisted-tangle-nmt
- Rollic Help Center — How to play: https://rollic.helpshift.com/hc/en/11-twisted-tangle/faq/254-how-to-play-twisted-tangle/

Finding: you drag a rope's end to another hole (it reattaches); the goal is that no ropes cross; the limit is
moves/time. No source mentions a length limit or solid ropes — the **3-unit reach limit and the layer
preference are this project's design choices**, not the original game's mechanics.
