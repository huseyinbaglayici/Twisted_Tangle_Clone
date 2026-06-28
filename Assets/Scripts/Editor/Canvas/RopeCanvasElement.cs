using System;
using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using TwistedTangle.Editor.Geometry;
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
        public RopeData PreviewRope; // in-progress rope being authored (null if none)
        public int SelectedRopeId = -1;
        public bool ShowCrossings; // highlight crossing points (flip tool)
        public bool ShowSubGrid;   // show sub-grid routing dots (rope tool)

        // --- callbacks to the window ---
        public Action<int, int, Vector2, int> CellClicked; // cellX, cellY, localPos, mouseButton
        public Action<int, int> CellDragged; // cellX, cellY (pointer held + moved)
        public Action Released;

        private bool _pointerDown;
        private Vector2Int _lastDragCell = new(-1, -1);
        private Vector2Int _hoveredPeg = new(-1, -1);
        private Vector2Int _hoveredSubCell = new(-1, -1);

        public RopeCanvasElement()
        {
            focusable = false;
            pickingMode = PickingMode.Position;
            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _hoveredPeg = new Vector2Int(-1, -1);
                _hoveredSubCell = new Vector2Int(-1, -1);
                MarkDirtyRepaint();
            });
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

        // Returns full sub-grid coordinates (sx, sy) where sx = cellX*SubDiv + localSub, etc.
        private bool TrySubCell(Vector2 local, out int sx, out int sy)
        {
            int d = CrossingSolver.SubDiv;
            float gameX = local.x / CellSize;
            float gameY = GridHeight - local.y / CellSize;
            sx = Mathf.FloorToInt(gameX * d);
            sy = Mathf.FloorToInt(gameY * d);
            return sx >= 0 && sx < GridWidth * d && sy >= 0 && sy < GridHeight * d;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (GridWidth <= 0 || GridHeight <= 0) return;
            if (!TrySubCell(evt.localPosition, out int sx, out int sy)) return;

            _pointerDown = true;
            _lastDragCell = new Vector2Int(sx, sy);
            this.CapturePointer(evt.pointerId);
            CellClicked?.Invoke(sx, sy, (Vector2)evt.localPosition, evt.button);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            bool dirty = false;

            // Coarse hover — pin highlight
            if (TryCell(evt.localPosition, out int hx, out int hy))
            {
                var hc = new Vector2Int(hx, hy);
                if (hc != _hoveredPeg) { _hoveredPeg = hc; dirty = true; }
            }

            // Sub-grid hover — dot highlight when rope tool is active
            if (ShowSubGrid && TrySubCell(evt.localPosition, out int shx, out int shy))
            {
                var hs = new Vector2Int(shx, shy);
                if (hs != _hoveredSubCell) { _hoveredSubCell = hs; dirty = true; }
            }
            else if (_hoveredSubCell.x != -1) { _hoveredSubCell = new Vector2Int(-1, -1); dirty = true; }

            if (dirty) MarkDirtyRepaint();

            if (!_pointerDown) return;
            if (!TrySubCell(evt.localPosition, out int sx, out int sy)) return;

            var subCell = new Vector2Int(sx, sy);
            if (subCell == _lastDragCell) return;
            _lastDragCell = subCell;
            CellDragged?.Invoke(sx, sy);
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

        private float RopeWidth => Mathf.Max(6f, CellSize * 0.17f);
        private float GapPx => RopeWidth * 2.6f;
        private const int SplineSamples = 16; // subdivisions per segment — raise for smoother curves

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
            if (ShowSubGrid) DrawSubGridDots(p); // drawn last so dots are always visible over ropes
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
            var endpointColors = BuildEndpointColors();

            foreach (var entity in Level.GridEntities)
            {
                Vector2 c = ToPx(CrossingSolver.Center(entity.Coordinates));
                // Endpoint pins (pin A / pin B) take their rope's color; other entities use their type color.
                Color fill = endpointColors.TryGetValue(CrossingSolver.PinToSub(entity.Coordinates), out var ropeColor)
                    ? ropeColor
                    : PegColorResolver?.Invoke(entity.TypeId) ?? new Color(0.8f, 0.8f, 0.8f);

                p.fillColor = fill;
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();

                // Outer border
                p.lineWidth = 3.5f;
                p.strokeColor = new Color(0.06f, 0.06f, 0.06f, 0.88f);
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();

                // Hover glow
                if (entity.Coordinates == _hoveredPeg)
                {
                    p.lineWidth = 3f;
                    p.strokeColor = new Color(1f, 1f, 1f, 0.4f);
                    p.BeginPath();
                    p.Arc(c, r + 5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Stroke();
                }
            }
        }

        private void DrawRopes(Painter2D p)
        {
            if (Level == null || Level.Ropes.Count == 0) return;

            var gaps = BuildGapMap();
            var noGaps = new Dictionary<(int, int), List<float>>();

            var sorted = new List<(RopeData rope, int idx)>();
            for (int i = 0; i < Level.Ropes.Count; i++)
                sorted.Add((Level.Ropes[i], i));
            sorted.Sort((a, b) => a.rope.Layer.CompareTo(b.rope.Layer));

            foreach (var entry in sorted)
                if (entry.rope.RopeId == SelectedRopeId && entry.rope.Path.Count >= 2)
                    StrokeRope(p, entry.rope, entry.idx, noGaps, new Color(1f, 1f, 1f, 0.4f), RopeWidth + 10f);

            foreach (var entry in sorted)
            {
                if (entry.rope.Path.Count < 2) continue;
                StrokeRope(p, entry.rope, entry.idx, noGaps,
                    new Color(0.06f, 0.06f, 0.06f, 0.6f), RopeWidth + 7f);
                StrokeRope(p, entry.rope, entry.idx, gaps, entry.rope.Tint, RopeWidth);
                DrawEndpoints(p, entry.rope);
                DrawExitGrommets(p, entry.rope);
            }
        }

        /// <summary>Color each rope endpoint (pin A / pin B) gets, including the in-progress rope.</summary>
        private Dictionary<Vector2Int, Color> BuildEndpointColors()
        {
            var map = new Dictionary<Vector2Int, Color>();
            if (Level != null)
                foreach (var rope in Level.Ropes)
                {
                    if (rope.Path == null || rope.Path.Count < 1) continue;
                    map[rope.Path[0].PegCoord] = rope.Tint;
                    map[rope.Path[^1].PegCoord] = rope.Tint;
                }

            if (PreviewRope is { Path: { Count: >= 1 } })
            {
                map[PreviewRope.Path[0].PegCoord] = PreviewRope.Tint;
                map[PreviewRope.Path[^1].PegCoord] = PreviewRope.Tint;
            }

            return map;
        }

        /// <summary>For each segment of each rope, the parametric t-positions where it goes UNDER.</summary>
        private Dictionary<(int rope, int seg), List<float>> BuildGapMap()
        {
            var gaps = new Dictionary<(int, int), List<float>>();
            var crossings = CrossingSolver.FindCrossings(Level.Ropes);
            // Same over/under the solver sees: auto-alternated braids + manual flip exceptions.
            var aOver = CrossingSolver.ResolveOverUnder(Level.Ropes, crossings, Level.CrossingOverrides);

            for (int i = 0; i < crossings.Count; i++)
            {
                var c = crossings[i];
                if (aOver[i]) AddGap(gaps, c.RopeIndexB, c.SegB, c.TB); // B goes under
                else AddGap(gaps, c.RopeIndexA, c.SegA, c.TA); // A goes under
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

            int segCount = rope.Path.Count - 1;
            for (int seg = 0; seg < segCount; seg++)
            {
                Vector2 p1 = ToPx(CrossingSolver.SubCenter(rope.Path[seg].PegCoord));
                Vector2 p2 = ToPx(CrossingSolver.SubCenter(rope.Path[seg + 1].PegCoord));
                // Neighbour points for Catmull-Rom tangent — mirror at ends so the curve
                // starts/ends tangent to the segment direction (no surprise kinks at pins).
                Vector2 p0 = seg > 0
                    ? ToPx(CrossingSolver.SubCenter(rope.Path[seg - 1].PegCoord))
                    : p1 * 2f - p2;
                Vector2 p3 = seg < segCount - 1
                    ? ToPx(CrossingSolver.SubCenter(rope.Path[seg + 2].PegCoord))
                    : p2 * 2f - p1;

                float segLen = Vector2.Distance(p1, p2);
                if (segLen < 0.001f) continue;

                if (!gaps.TryGetValue((ropeIndex, seg), out var ts) || ts.Count == 0)
                {
                    DrawCatmullSegment(p, p0, p1, p2, p3, 0f, 1f);
                    continue;
                }

                ts.Sort();
                float halfT = Mathf.Min(0.45f, GapPx * 0.5f / segLen);
                float cursor = 0f;
                foreach (float t in ts)
                {
                    float gapStart = Mathf.Clamp01(t - halfT);
                    float gapEnd = Mathf.Clamp01(t + halfT);
                    if (gapStart > cursor) DrawCatmullSegment(p, p0, p1, p2, p3, cursor, gapStart);
                    cursor = Mathf.Max(cursor, gapEnd);
                }

                if (cursor < 1f) DrawCatmullSegment(p, p0, p1, p2, p3, cursor, 1f);
            }
        }

        // Draws a sub-range [t0, t1] of one Catmull-Rom segment as a fine polyline.
        private static void DrawCatmullSegment(Painter2D p,
            Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t0, float t1)
        {
            float step = (t1 - t0) / SplineSamples;
            p.BeginPath();
            p.MoveTo(CatmullRom(p0, p1, p2, p3, t0));
            for (int i = 1; i <= SplineSamples; i++)
                p.LineTo(CatmullRom(p0, p1, p2, p3, Mathf.Min(t0 + i * step, t1)));
            p.Stroke();
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }


        private void DrawSubGridDots(Painter2D p)
        {
            if (Level == null) return;
            int d = CrossingSolver.SubDiv;
            int mid = d / 2;
            var pinCells = new HashSet<Vector2Int>(Level.GridEntities.Select(e => e.Coordinates));

            // Collect waypoints already placed in the preview rope for click feedback.
            var placedWaypoints = new HashSet<Vector2Int>();
            if (PreviewRope != null)
                foreach (var wp in PreviewRope.Path)
                    if (wp.IsBendPoint) placedWaypoints.Add(wp.PegCoord);

            for (int cx = 0; cx < GridWidth; cx++)
            {
                for (int cy = 0; cy < GridHeight; cy++)
                {
                    bool hasPin = pinCells.Contains(new Vector2Int(cx, cy));
                    for (int dx = 0; dx < d; dx++)
                    {
                        for (int dy = 0; dy < d; dy++)
                        {
                            // Skip center only for pin cells — pin circle is already the visual indicator.
                            if (dx == mid && dy == mid && hasPin) continue;
                            var sub = new Vector2Int(cx * d + dx, cy * d + dy);
                            Vector2 pos = ToPx(CrossingSolver.SubCenter(sub));

                            bool isHovered = sub == _hoveredSubCell;
                            bool isPlaced  = placedWaypoints.Contains(sub);

                            float radius = isHovered || isPlaced ? 4.5f : 2.5f;
                            // Dark halo so the dot reads on any rope color underneath.
                            p.fillColor = new Color(0f, 0f, 0f, 0.55f);
                            p.BeginPath();
                            p.Arc(pos, radius + 2f, Angle.Degrees(0f), Angle.Degrees(360f));
                            p.Fill();
                            p.fillColor = isPlaced  ? new Color(1f, 1f, 1f, 0.90f) :
                                          isHovered ? new Color(1f, 1f, 1f, 0.70f) :
                                                      new Color(1f, 1f, 1f, 0.40f);
                            p.BeginPath();
                            p.Arc(pos, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                            p.Fill();
                        }
                    }
                }
            }
        }

        private void DrawExitGrommets(Painter2D p, RopeData rope)
        {
            if (rope.Path.Count < 2) return;
            float pegR = CellSize * 0.30f;
            Color color = rope.Tint;

            Vector2 aC = ToPx(CrossingSolver.SubCenter(rope.Path[0].PegCoord));
            Vector2 aDir = (ToPx(CrossingSolver.SubCenter(rope.Path[1].PegCoord)) - aC).normalized;
            DrawSocketArc(p, aC, aDir, pegR, color);

            Vector2 bC = ToPx(CrossingSolver.SubCenter(rope.Path[^1].PegCoord));
            Vector2 bDir = (ToPx(CrossingSolver.SubCenter(rope.Path[^2].PegCoord)) - bC).normalized;
            DrawSocketArc(p, bC, bDir, pegR, color);
        }

        private void DrawSocketArc(Painter2D p, Vector2 pinCenter, Vector2 exitDir, float pegR, Color color)
        {
            // Small socket dot INSIDE the pin, offset toward rope — "hole where rope exits" illusion.
            // Stays within pin boundary so it never needs to merge with the rope.
            Vector2 sockCenter = pinCenter + exitDir * (pegR * 0.44f);
            float holeR  = pegR * 0.13f;
            float frameR = holeR + 2.5f;

            // Rope-colored frame ring
            p.fillColor = color;
            p.BeginPath();
            p.Arc(sockCenter, frameR, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();

            // Dark hole center
            p.fillColor = new Color(0.05f, 0.05f, 0.05f, 1f);
            p.BeginPath();
            p.Arc(sockCenter, holeR, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }

        private void DrawEndpoints(Painter2D p, RopeData rope)
        {
            if (rope.Path.Count < 1) return;
            Color color = rope.Tint;
            float r = RopeWidth * 1.1f;

            DrawDot(p, ToPx(CrossingSolver.SubCenter(rope.Path[0].PegCoord)), r, color);
            DrawDot(p, ToPx(CrossingSolver.SubCenter(rope.Path[^1].PegCoord)), r, color);

            // Inner grip ring on rope endpoints
            float pegR = CellSize * 0.30f;
            p.lineWidth = 1.5f;
            p.strokeColor = new Color(0f, 0f, 0f, 0.82f);
            p.BeginPath();
            p.Arc(ToPx(CrossingSolver.SubCenter(rope.Path[0].PegCoord)), pegR * 0.38f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            p.BeginPath();
            p.Arc(ToPx(CrossingSolver.SubCenter(rope.Path[^1].PegCoord)), pegR * 0.38f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();

            // Hollow ring at each bend point.
            float br = RopeWidth * 0.45f;
            for (int i = 1; i < rope.Path.Count - 1; i++)
            {
                if (!rope.Path[i].IsBendPoint) continue;
                Vector2 c = ToPx(CrossingSolver.SubCenter(rope.Path[i].PegCoord));
                p.lineWidth = 2f;
                p.strokeColor = new Color(color.r, color.g, color.b, 0.85f);
                p.BeginPath();
                p.Arc(c, br, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }
        }


        private void DrawPreview(Painter2D p)
        {
            if (PreviewRope == null || PreviewRope.Path.Count == 0) return;
            Color color = PreviewRope.Tint;
            color.a = 0.6f;

            p.lineWidth = RopeWidth;
            p.strokeColor = color;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            if (PreviewRope.Path.Count >= 2)
            {
                int segCount = PreviewRope.Path.Count - 1;
                for (int seg = 0; seg < segCount; seg++)
                {
                    Vector2 p1 = ToPx(CrossingSolver.Center(PreviewRope.Path[seg].PegCoord));
                    Vector2 p2 = ToPx(CrossingSolver.Center(PreviewRope.Path[seg + 1].PegCoord));
                    Vector2 p0 = seg > 0
                        ? ToPx(CrossingSolver.Center(PreviewRope.Path[seg - 1].PegCoord))
                        : p1 * 2f - p2;
                    Vector2 p3 = seg < segCount - 1
                        ? ToPx(CrossingSolver.Center(PreviewRope.Path[seg + 2].PegCoord))
                        : p2 * 2f - p1;
                    DrawCatmullSegment(p, p0, p1, p2, p3, 0f, 1f);
                }
            }

            foreach (var wp in PreviewRope.Path)
            {
                Vector2 c = ToPx(CrossingSolver.Center(wp.PegCoord));
                if (wp.IsBendPoint)
                {
                    p.lineWidth = 2f;
                    p.strokeColor = new Color(1f, 0.9f, 0.2f, 0.9f);
                    p.BeginPath();
                    p.Arc(c, RopeWidth * 0.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Stroke();
                }
                else
                {
                    DrawDot(p, c, RopeWidth * 0.7f, new Color(1f, 1f, 1f, 0.9f));
                }
            }
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