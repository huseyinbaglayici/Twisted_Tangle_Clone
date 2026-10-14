# Twisted Tangle — Level Validation & AI Generation Design

Editor-time tooling that keeps levels playable:

1. **Validator** — checks a level against the runtime rules and estimates how many moves it takes.
2. **AI level generation** — builds a prompt grounded in the shipped levels, turns the answer into a level file and validates it.

Nothing here runs in the game. The runtime only reads the level JSON files.

---

## 1. Level data

Levels live in `Assets/Resources/Levels/level_NNN.json` and are loaded by `LevelRepository` (`Runtime/Level/LevelJson.cs`).

```
LevelJson   levelNumber, difficulty, timeLimitSeconds, stages[], tutorial?
StageJson   mapId, gridWidth, gridHeight, pins[], ropes[]
PinJson     id, x, y, locked
RopeJson    id, pinA, pinB, colorIndex, layer, path[25 × {x, y, h}]
TutorialJson pinId, toX, toY      (pinId = -1 means no tutorial)
```

- Map `N` is a `(3+N) × (5+N)` grid. Every cell is a hole; a pin sits on one cell.
- `path` is the rope's start shape: 25 points in cell space (`x`, `y`) plus a height `h` above the board. The runtime places the physics rope particles on these points, so the path decides both the initial shape and which rope lies on top at every crossing.
- `layer` is the editor draw order and the default stacking order for generated paths. The runtime ignores it; heights in the path are what count.

## 2. Rules

Ropes are physics ropes with a rest length of 2.5 cells. The rules the validator and the prompt follow are the runtime's (`PinInputController`, `WinChecker`, `BoardModel`, `TensionLimits`):

- **Move.** The player drags one unlocked pin to an empty cell. Locked pins never move.
- **Reach.** The drag is clamped to 2.85 cells from the other pin of each attached rope; a drop slot may be up to 3.6 cells away.
- **Tension.** Tension = length / rest length − 1. A rope turns red at 45 % of max tension (~3.91 cells) and a drag past 55 % (~4.22 cells) is cancelled.
- **Merge.** A rope disappears once it has touched no other rope for a short settle time (0.15 s) **and** at least one of its pins holds only that rope. While a rope merges, pins chained to it through shared pins are frozen.
- **Win / lose.** Win when every rope is gone; lose when the per-level time limit runs out.
- **Start state.** Every rope must touch another rope. A free rope at the start would merge immediately.

## 3. Crossings and over/under

Contact between two ropes is tested on the authored paths (`RopeGeometry.PathsTouch`, 0.15 cell threshold, end segments at the pins skipped). The straight pin-to-pin chord is not enough: slack ropes bulge sideways and the chord misjudges about a third of the pairs.

Each strict crossing of two paths gets an over/under from the path heights at the crossing point: the rope with the larger `h` is on top. In the shipped levels a rope lying on the board sits at `h ≈ 0.1`, a rope passing over another one is ~0.2 higher, and both ends sit at `h = 0.2` at the pin.

A rope pair is classified as:

| Kind | Meaning |
|---|---|
| Touch | in contact, no strict crossing |
| Over | one rope is on top at every crossing |
| Hook | the top rope changes between crossings — the ropes are wound around each other |

## 4. Hook rule and difficulty estimate

Physics tests showed that straight-segment solvers are optimistic: dragging the lower rope pulls the rope lying on it along. So:

- an **Over** pair is cleared by moving the **top** rope,
- a **Hook** pair needs **both** ropes moved (≈ 2 moves),
- a **Touch** pair needs either rope moved.

`LevelValidator.EstimateMoves` collects the ropes that must move — the top rope of every Over pair, both ropes of every Hook pair, then one rope for each Touch pair not already covered — and reports the set size. One move of a rope clears all of its pairs, so the estimate does not double count. It ignores whether a clearing move fits inside the reach; that stays a play-test item.

## 5. Validation (`LevelValidator`)

Errors (block saving):

- level number ≥ 1, time limit > 0
- pins inside the grid, unique pin ids, one pin per cell
- ropes reference two different existing pins, unique rope ids, `colorIndex` inside the palette
- pin-to-pin length ≤ max tension length
- every rope has a 25-point path that starts on `pinA` and ends on `pinB`
- no rope is free at the start
- every rope can eventually merge: repeatedly remove ropes that have a pin holding only them; anything left is stuck behind shared pins
- tutorial pin exists and its target is an empty cell

Warnings: tense ropes at the start, unused pins, grid size not matching the map, extra stages.

Metrics: pins, locked pins, shared pins, ropes, contact pairs, hooks, estimated moves, free cells.

## 6. Difficulty profiles

Profiles are measured from the shipped levels when the prompt is built (`LevelDataStats`), so they follow the level set instead of hand-tuned numbers. Levels are grouped by range:

| Profile | Levels | Ropes | Pins | Contact pairs | Hooks | Est. moves | Locked pins | Time limit |
|---|---|---|---|---|---|---|---|---|
| Easy | 1–10 | 2–3 | 4–6 | 1–3 | 0–2 | 1–3 | none | 300 s |
| Medium | 11–20 | 3–4 | 6–8 | 2–6 | 0–3 | 2–4 | 5 of 10 levels, max 1 | 57–207 s (median 75) |
| Hard | 21–30 | 4–5 | 8–10 | 2–7 | 0–2 | 4–5 | none | 50–99 s (median 72) |

All shipped levels use map 1, no shared pins, and pin-to-pin lengths of 1.0–3.2 cells.

## 7. AI generation flow

1. **Prompt** (`LevelAiGenerator.BuildManualPrompt`). The AI Level Generator window takes map, difficulty, optional time limit (0 = profile median) and examples (auto picks two levels of the difficulty, or one chosen level, or none). The prompt holds the rules above, the measured profile, the output contract and the examples. Each example is a one-line summary (moves, which rope is over which, hooked pairs) plus the level in the output format.
2. **Answer.** The model writes pins and ropes only — it cannot author physics paths:

   ```json
   {"difficulty":"Medium","timeLimitSeconds":75,"mapId":1,
    "pins":[{"id":0,"x":0,"y":3,"locked":false}],
    "ropes":[{"id":0,"pinA":0,"pinB":1,"colorIndex":0,"layer":0,"bend":0.0}],
    "crossings":[{"a":0,"b":1,"onTop":[1]}]}
   ```

   `bend` is the sideways bulge in cells (+ left of pinA→pinB, 0 = natural slack). `crossings[].onTop` lists the top rope at each crossing in order along rope `a`; two different ids make a hook. Pairs without an entry use `layer`.
3. **Path generation** (`RopePathBuilder`, shared with the Level Creator). Each rope becomes a 25-point arc between its pins. A rope whose pins are closer than its slack allows bulges sideways (parabola sag from the arc length of a ~2.4 cell rope). Heights start at the shipped profile (0.2 at the pins, rising near the pins, 0.1 on the board); at every crossing the top rope gets a smooth bump 0.2 above the lower rope, and a lower rope that was above is pressed back down. The Level Creator uses the same builder for new ropes and for "to front / to back".
4. **Validation.** The level opens in the Level Creator, which runs the validator and shows hooks and estimated moves.
5. **Save.** Saving writes `level_NNN.json` into `Resources/Levels`, where `LevelRepository` loads it.

## 8. Open items

- Generated paths are single arcs. The shipped levels have S-curves and loops, so re-building a shipped level from its own summary gives a valid level for 25 of 30, with most contacts kept but some hooks lost. Hooks in generated levels need a deliberate `bend`.
- The move estimate does not check reach or tension of the clearing moves, and does not search move order. A physics replay in edit mode would close that gap.
- The difficulty buckets are fixed level ranges. Once there are more shipped levels the ranges should come from the data as well.
- Time limits are taken from the profile, not derived from the estimated moves.
