using System;
using System.Collections.Generic;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using TwistedTangle.Runtime.Geometry;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor.Canvas
{
    /// <summary>
    /// The drawing surface of the level editor. Renders the grid, pegs, and ropes with Painter2D
    /// (UI Toolkit's vector API). Rope crossings use the knot-diagram trick: the rope that goes
    /// "under" is broken with a small gap at the crossing, which makes the over/under order obvious
    /// and stays correct regardless of how many ropes pile onto the same spot.
    ///
    /// The window owns all state; it pushes data into the public fields and calls
    /// <see cref="Redraw"/>. The element only translates pointer input into cell coordinates and
    /// forwards it through the callbacks.
    /// </summary>
    public class RopeCanvasElement : VisualElement
    {
        // --- state pushed by the window ---
        public float CellSize = 44f;
        public int GridWidth;
        public int GridHeight;
        public LevelDataSO Level;
        public Func<string, Color> PegColorResolver;
        public RopeData PreviewRope;          // in-progress rope being authored (null if none)
        public int SelectedRopeId = -1;
        public bool ShowCrossings;            // highlight crossing points (flip tool)

        // --- callbacks to the window ---
        public Action<int, int, Vector2, int> CellClicked; // cellX, cellY, localPos, mouseButton
        public Action<int, int> CellDragged;               // cellX, cellY (pointer held + moved)
        public Action Released;

        private bool _pointerDown;
        private Vector2Int _lastDragCell = new(-1, -1);

        public RopeCanvasElement()
        {
            focusable = false;
            pickingMode = PickingMode.Position;
            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        public void Redraw()
        {
            style.width = Mathf.Max(0, GridWidth) * CellSize;
            style.height = Mathf.Max(0, GridHeight) * CellSize;
            MarkDirtyRepaint();
        }

        #region Input

        private bool TryCell(Vector2 local, out int cellX, out int cellY)
        {
            cellX = Mathf.FloorToInt(local.x / CellSize);
            int rowFromTop = Mathf.FloorToInt(local.y / CellSize);
            cellY = GridHeight - 1 - rowFromTop;
            return cellX >= 0 && cellX < GridWidth && cellY >= 0 && cellY < GridHeight;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (GridWidth <= 0 || GridHeight <= 0) return;
            if (!TryCell(evt.localPosition, out int x, out int y)) return;

            _pointerDown = true;
            _lastDragCell = new Vector2Int(x, y);
            this.CapturePointer(evt.pointerId);
            CellClicked?.Invoke(x, y, (Vector2)evt.localPosition, evt.button);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_pointerDown) return;
            if (!TryCell(evt.localPosition, out int x, out int y)) return;

            var cell = new Vector2Int(x, y);
            if (cell == _lastDragCell) return;
            _lastDragCell = cell;
            CellDragged?.Invoke(x, y);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_pointerDown) return;
            _pointerDown = false;
            _lastDragCell = new Vector2Int(-1, -1);
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            Released?.Invoke();
        }

        #endregion

        #region Rendering

        private float RopeWidth => Mathf.Max(4f, CellSize * 0.16f);
        private float GapPx => RopeWidth * 1.9f;

        private Vector2 ToPx(Vector2 centerSpace) =>
            new(centerSpace.x * CellSize, (GridHeight - centerSpace.y) * CellSize);

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (GridWidth <= 0 || GridHeight <= 0) return;
            var p = mgc.painter2D;

            DrawGrid(p);
            DrawPegs(p);
            DrawRopes(p);
            DrawPreview(p);
            if (ShowCrossings) DrawCrossingMarkers(p);
        }

        private void DrawGrid(Painter2D p)
        {
            p.lineWidth = 1f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.08f);
            float w = GridWidth * CellSize;
            float h = GridHeight * CellSize;

            for (int x = 0; x <= GridWidth; x++)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x * CellSize, 0));
                p.LineTo(new Vector2(x * CellSize, h));
                p.Stroke();
            }

            for (int y = 0; y <= GridHeight; y++)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(0, y * CellSize));
                p.LineTo(new Vector2(w, y * CellSize));
                p.Stroke();
            }
        }

        private void DrawPegs(Painter2D p)
        {
            if (Level == null) return;
            float r = CellSize * 0.30f;

            foreach (var peg in Level.Pegs)
            {
                Vector2 c = ToPx(CrossingSolver.Center(peg.Coordinates));
                Color fill = PegColorResolver?.Invoke(peg.TypeId) ?? new Color(0.8f, 0.8f, 0.8f);

                p.fillColor = fill;
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();

                p.lineWidth = 2f;
                p.strokeColor = new Color(0f, 0f, 0f, 0.6f);
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }
        }

        private void DrawRopes(Painter2D p)
        {
            if (Level == null || Level.Ropes.Count == 0) return;

            var gaps = BuildGapMap();

            // Selected rope first as a soft halo so it reads as selected without changing draw order.
            for (int i = 0; i < Level.Ropes.Count; i++)
            {
                var rope = Level.Ropes[i];
                if (rope.RopeId == SelectedRopeId && rope.Path.Count >= 2)
                    StrokeRope(p, rope, i, gaps, new Color(1f, 1f, 1f, 0.35f), RopeWidth + 8f);
            }

            for (int i = 0; i < Level.Ropes.Count; i++)
            {
                var rope = Level.Ropes[i];
                if (rope.Path.Count < 2) continue;
                StrokeRope(p, rope, i, gaps, EntityColorResolved(rope), RopeWidth);
                DrawEndpoints(p, rope);
            }
        }

        private static Color EntityColorResolved(RopeData rope) =>
            Runtime.Data.Enums.EntityColors.Resolve(rope.Color);

        /// <summary>For each segment of each rope, the parametric t-positions where it goes UNDER.</summary>
        private Dictionary<(int rope, int seg), List<float>> BuildGapMap()
        {
            var gaps = new Dictionary<(int, int), List<float>>();
            var overrides = new HashSet<CrossingOverride>(Level.CrossingOverrides);
            var crossings = CrossingSolver.FindCrossings(Level.Ropes);

            foreach (var c in crossings)
            {
                var ropeA = Level.Ropes[c.RopeIndexA];
                var ropeB = Level.Ropes[c.RopeIndexB];
                bool overridden = overrides.Contains(
                    CrossingOverride.Create(c.RopeIdA, c.SegA, c.RopeIdB, c.SegB));
                bool aOver = CrossingSolver.IsAOver(ropeA, ropeB, overridden);

                if (aOver) AddGap(gaps, c.RopeIndexB, c.SegB, c.TB);
                else AddGap(gaps, c.RopeIndexA, c.SegA, c.TA);
            }

            return gaps;
        }

        private static void AddGap(Dictionary<(int, int), List<float>> gaps, int rope, int seg, float t)
        {
            var key = (rope, seg);
            if (!gaps.TryGetValue(key, out var list)) gaps[key] = list = new List<float>();
            list.Add(t);
        }

        private void StrokeRope(Painter2D p, RopeData rope, int ropeIndex,
            Dictionary<(int, int), List<float>> gaps, Color color, float width)
        {
            p.lineWidth = width;
            p.strokeColor = color;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            for (int seg = 0; seg < rope.Path.Count - 1; seg++)
            {
                Vector2 a = ToPx(CrossingSolver.Center(rope.Path[seg].PegCoord));
                Vector2 b = ToPx(CrossingSolver.Center(rope.Path[seg + 1].PegCoord));
                float segLen = Vector2.Distance(a, b);
                if (segLen < 0.001f) continue;

                if (!gaps.TryGetValue((ropeIndex, seg), out var ts) || ts.Count == 0)
                {
                    DrawPiece(p, a, b, 0f, 1f);
                    continue;
                }

                ts.Sort();
                float halfT = Mathf.Min(0.45f, GapPx * 0.5f / segLen);
                float cursor = 0f;
                foreach (float t in ts)
                {
                    float gapStart = Mathf.Clamp01(t - halfT);
                    float gapEnd = Mathf.Clamp01(t + halfT);
                    if (gapStart > cursor) DrawPiece(p, a, b, cursor, gapStart);
                    cursor = Mathf.Max(cursor, gapEnd);
                }
                if (cursor < 1f) DrawPiece(p, a, b, cursor, 1f);
            }
        }

        private static void DrawPiece(Painter2D p, Vector2 a, Vector2 b, float t0, float t1)
        {
            p.BeginPath();
            p.MoveTo(Vector2.LerpUnclamped(a, b, t0));
            p.LineTo(Vector2.LerpUnclamped(a, b, t1));
            p.Stroke();
        }

        private void DrawEndpoints(Painter2D p, RopeData rope)
        {
            if (rope.Path.Count < 1) return;
            Color color = EntityColorResolved(rope);
            float r = RopeWidth * 0.9f;

            DrawDot(p, ToPx(CrossingSolver.Center(rope.Path[0].PegCoord)), r, color);
            DrawDot(p, ToPx(CrossingSolver.Center(rope.Path[^1].PegCoord)), r, color);
        }

        private void DrawPreview(Painter2D p)
        {
            if (PreviewRope == null || PreviewRope.Path.Count == 0) return;
            Color color = EntityColorResolved(PreviewRope);
            color.a = 0.6f;

            p.lineWidth = RopeWidth;
            p.strokeColor = color;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            if (PreviewRope.Path.Count >= 2)
            {
                p.BeginPath();
                p.MoveTo(ToPx(CrossingSolver.Center(PreviewRope.Path[0].PegCoord)));
                for (int i = 1; i < PreviewRope.Path.Count; i++)
                    p.LineTo(ToPx(CrossingSolver.Center(PreviewRope.Path[i].PegCoord)));
                p.Stroke();
            }

            foreach (var wp in PreviewRope.Path)
                DrawDot(p, ToPx(CrossingSolver.Center(wp.PegCoord)), RopeWidth * 0.7f,
                    new Color(1f, 1f, 1f, 0.9f));
        }

        private void DrawCrossingMarkers(Painter2D p)
        {
            if (Level == null) return;
            foreach (var c in CrossingSolver.FindCrossings(Level.Ropes))
            {
                Vector2 px = ToPx(c.Point);
                DrawDot(p, px, 5f, new Color(1f, 1f, 1f, 0.95f));
                p.lineWidth = 1.5f;
                p.strokeColor = new Color(0f, 0f, 0f, 0.8f);
                p.BeginPath();
                p.Arc(px, 5f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }
        }

        private static void DrawDot(Painter2D p, Vector2 c, float r, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }

        #endregion
    }
}
