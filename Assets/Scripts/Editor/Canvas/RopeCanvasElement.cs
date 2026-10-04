using System;
using System.Collections.Generic;
using Runtime.Level;
using TwistedTangle.Editor.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Canvas
{
    /// <summary>
    /// Top-down drawing of one level stage, matching the runtime: every cell is a slot, pins sit on cells,
    /// a rope runs pinA → pinB along its authored path (straight when the path is empty) and ropes
    /// are drawn in layer order (layer = draw order only).
    /// </summary>
    public class RopeCanvasElement : VisualElement
    {
        // --- state pushed by the window ---
        public float CellSize = 44f;
        public StageJson Stage;
        public TutorialJson Tutorial;
        public RopePaletteSO Palette;
        public int SelectedRopeId = -1;
        public int PendingPinId = -1;                 // rope tool: first pin picked, waiting for the second
        public HashSet<int> TangledRopeIds;           // ropes touching another rope (runtime rule)
        public Color GridStrokeColor = EditorColors.GridDefault;
        public Color RopeOutlineColor = EditorColors.RopeOutlineDark;

        // --- callbacks to the window ---
        public Action<int, int, int> CellClicked; // cellX, cellY, mouseButton
        public Action<int, int> CellDragged;      // cellX, cellY (pointer held + moved)
        public Action Released;

        private bool _pointerDown;
        private Vector2Int _lastDragCell = new(-1, -1);
        private Vector2Int _hoveredCell = new(-1, -1);

        private int Width => Stage?.gridWidth ?? 0;
        private int Height => Stage?.gridHeight ?? 0;

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
                _hoveredCell = new Vector2Int(-1, -1);
                MarkDirtyRepaint();
            });
        }

        public void Redraw()
        {
            style.width = Mathf.Max(0, Width) * CellSize;
            style.height = Mathf.Max(0, Height) * CellSize;
            MarkDirtyRepaint();
        }

        #region Input

        private bool TryCell(Vector2 local, out Vector2Int cell)
        {
            int x = Mathf.FloorToInt(local.x / CellSize);
            int y = Height - 1 - Mathf.FloorToInt(local.y / CellSize);
            cell = new Vector2Int(x, y);
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!TryCell(evt.localPosition, out var cell)) return;
            _pointerDown = true;
            _lastDragCell = cell;
            this.CapturePointer(evt.pointerId);
            CellClicked?.Invoke(cell.x, cell.y, evt.button);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            bool inside = TryCell(evt.localPosition, out var cell);
            var hovered = inside ? cell : new Vector2Int(-1, -1);
            if (hovered != _hoveredCell)
            {
                _hoveredCell = hovered;
                MarkDirtyRepaint();
            }

            if (!_pointerDown || !inside || cell == _lastDragCell) return;
            _lastDragCell = cell;
            CellDragged?.Invoke(cell.x, cell.y);
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

        private float RopeWidth => Mathf.Max(5f, CellSize * 0.14f);
        private float PinRadius => CellSize * 0.30f;
        private const int SplineSamples = 6; // per path segment (13-point authored paths are already dense)

        // Cell space (integer = cell center) → canvas pixels (y up in cell space, down in pixels).
        private Vector2 ToPx(Vector2 cellSpace) =>
            new((cellSpace.x + 0.5f) * CellSize, (Height - (cellSpace.y + 0.5f)) * CellSize);

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (Stage == null || Width <= 0 || Height <= 0) return;
            var p = mgc.painter2D;

            DrawSlots(p);
            DrawRopes(p);
            DrawPins(p);
            DrawPendingRope(p);
            DrawTutorial(p);
        }

        private void DrawSlots(Painter2D p)
        {
            float r = CellSize * 0.19f; // slot scale 0.38 of a 1-unit cell
            p.fillColor = GridStrokeColor;
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                p.BeginPath();
                p.Arc(ToPx(new Vector2(x, y)), r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }

            if (_hoveredCell.x < 0) return;
            p.lineWidth = 2f;
            p.strokeColor = EditorColors.SelectionGlow;
            p.BeginPath();
            p.Arc(ToPx(_hoveredCell), CellSize * 0.42f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        private Color RopeColor(RopeJson rope) => Palette != null ? Palette.Get(rope.colorIndex) : Color.white;

        private PinJson FindPin(int id)
        {
            foreach (var pin in Stage.pins)
                if (pin.id == id)
                    return pin;
            return null;
        }

        // Same shape the runtime builds: authored path (ends snapped onto the pins) or a straight line.
        private List<Vector2> RopePoints(RopeJson rope)
        {
            var a = FindPin(rope.pinA);
            var b = FindPin(rope.pinB);
            if (a == null || b == null) return null;

            var pts = new List<Vector2>();
            if (rope.path != null && rope.path.Count >= 2)
            {
                foreach (var pt in rope.path) pts.Add(new Vector2(pt.x, pt.y));
                pts[0] = a.Cell;
                pts[^1] = b.Cell;
            }
            else
            {
                pts.Add(a.Cell);
                pts.Add(b.Cell);
            }

            for (int i = 0; i < pts.Count; i++) pts[i] = ToPx(pts[i]);
            return pts;
        }

        private void DrawRopes(Painter2D p)
        {
            var sorted = new List<RopeJson>(Stage.ropes);
            sorted.Sort((x, y) => x.layer != y.layer ? x.layer.CompareTo(y.layer) : x.id.CompareTo(y.id));

            foreach (var rope in sorted)
            {
                var pts = RopePoints(rope);
                if (pts == null) continue;

                if (rope.id == SelectedRopeId)
                    StrokeSpline(p, pts, EditorColors.SelectionGlow, RopeWidth + 10f);

                // Painter's algorithm: outline + fill per rope in layer order => higher layer reads as "on top".
                StrokeSpline(p, pts, RopeOutlineColor, RopeWidth + 5f);
                StrokeSpline(p, pts, RopeColor(rope), RopeWidth);

                if (TangledRopeIds != null && TangledRopeIds.Contains(rope.id))
                    DrawDot(p, pts[pts.Count / 2], RopeWidth * 0.35f, new Color(0f, 0f, 0f, 0.55f));
            }
        }

        private void DrawPins(Painter2D p)
        {
            float r = PinRadius;
            foreach (var pin in Stage.pins)
            {
                Vector2 c = ToPx(pin.Cell);
                Color fill = EditorColors.PinDefault;
                foreach (var rope in Stage.ropes)
                    if (rope.pinA == pin.id || rope.pinB == pin.id)
                    {
                        fill = RopeColor(rope); // runtime: pin takes its first rope's color
                        break;
                    }

                DrawDot(p, c, r, fill);
                p.lineWidth = 3f;
                p.strokeColor = EditorColors.PegShadow;
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();

                if (pin.locked) DrawLock(p, c, r);

                if (pin.id == PendingPinId || pin.Cell == _hoveredCell)
                {
                    p.lineWidth = 3f;
                    p.strokeColor = pin.id == PendingPinId ? Color.white : EditorColors.SelectionGlow;
                    p.BeginPath();
                    p.Arc(c, r + 5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Stroke();
                }
            }
        }

        private static void DrawLock(Painter2D p, Vector2 c, float r)
        {
            float s = r * 0.45f;
            p.fillColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
            p.BeginPath();
            p.MoveTo(c + new Vector2(-s, -s * 0.2f));
            p.LineTo(c + new Vector2(s, -s * 0.2f));
            p.LineTo(c + new Vector2(s, s));
            p.LineTo(c + new Vector2(-s, s));
            p.ClosePath();
            p.Fill();
            p.lineWidth = 2.5f;
            p.strokeColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
            p.BeginPath();
            p.Arc(c + new Vector2(0f, -s * 0.2f), s * 0.6f, Angle.Degrees(180f), Angle.Degrees(360f));
            p.Stroke();
        }

        private void DrawPendingRope(Painter2D p)
        {
            if (PendingPinId < 0) return;
            var pin = FindPin(PendingPinId);
            if (pin == null || _hoveredCell.x < 0) return;

            p.lineWidth = RopeWidth * 0.6f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.6f);
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(ToPx(pin.Cell));
            p.LineTo(ToPx(_hoveredCell));
            p.Stroke();
        }

        private void DrawTutorial(Painter2D p)
        {
            if (Tutorial == null || Tutorial.pinId < 0) return;
            var pin = FindPin(Tutorial.pinId);
            if (pin == null) return;

            Vector2 from = ToPx(pin.Cell), to = ToPx(Tutorial.TargetCell);
            var color = new Color(1f, 0.85f, 0.2f, 0.95f);
            p.lineWidth = 3f;
            p.strokeColor = color;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(from);
            p.LineTo(to);
            p.Stroke();

            Vector2 dir = (to - from).normalized, side = new(-dir.y, dir.x);
            float head = CellSize * 0.2f;
            p.BeginPath();
            p.MoveTo(to - dir * head + side * head * 0.6f);
            p.LineTo(to);
            p.LineTo(to - dir * head - side * head * 0.6f);
            p.Stroke();

            p.BeginPath();
            p.Arc(to, PinRadius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        private static void StrokeSpline(Painter2D p, List<Vector2> pts, Color color, float width)
        {
            p.lineWidth = width;
            p.strokeColor = color;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(pts[0]);
            int n = pts.Count;
            for (int seg = 0; seg < n - 1; seg++)
            {
                Vector2 p1 = pts[seg], p2 = pts[seg + 1];
                Vector2 p0 = seg > 0 ? pts[seg - 1] : p1 * 2f - p2;
                Vector2 p3 = seg < n - 2 ? pts[seg + 2] : p2 * 2f - p1;
                for (int i = 1; i <= SplineSamples; i++)
                    p.LineTo(CatmullRom(p0, p1, p2, p3, i / (float)SplineSamples));
            }
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
