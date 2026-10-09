using System.Collections.Generic;
using DG.Tweening;
using Runtime.Entities;
using Runtime.Level;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Runtime.Gameplay
{
    // Pin controls:
    //  - Drag: the pin is lifted (dragOffset) and eased towards the finger every fixed step, clamped to
    //    maxPinDistance from every pin it is roped to. Released over a free slot -> slides in, otherwise slides back.
    //    The drag is cancelled when a rope it pulls (or one touching it) gets too tense.
    //  - Tap-move: tap a pin (reachable slots light up) -> tap a slot -> the pin flies there.
    public class PinInputController : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private RopeCollisionTracker collisions;
        [SerializeField] private Camera cam;

        [Header("Pick / drag")]
        [SerializeField] private float pickRadius = 0.45f;
        [SerializeField] private float dragThresholdPixels = 10f;
        [SerializeField] private float dragSurfaceHeight = 0.033f;
        [SerializeField] private Vector3 dragOffset = new(0f, 0.75f, 0f);
        [SerializeField] private float moveTime = 30f;               // lerp factor per second
        [SerializeField] private float dropSnapRadius = 0.5f;

        [Header("Drag cancel (rope tension)")]
        [SerializeField] private float cancelTension = 0.55f;
        [SerializeField] private float cancelTensionDelay = 0.25f;
        [SerializeField] private float cancelHigherTension = 0.75f;
        [SerializeField] private float cancelHigherTensionDelay = 0.1f;

        [Header("Placement tweens")]
        [SerializeField] private float placeSpeed = 6f;              // slot transfer (units / second)
        [SerializeField] private Ease placeEase = Ease.OutSine;
        [SerializeField] private float tapMoveSecondsPerUnit = 0.2f;
        [SerializeField] private Ease tapMoveEase = Ease.InOutQuad;

        private readonly MoveHistory _history = new();
        private readonly List<GridCellEntity> _highlighted = new();
        private BoardModel.Pin _pressed, _selected, _dragged;
        private Vector2 _pressScreen;
        private Vector3 _dragTarget;
        private float _dragTime;
        private GridCellEntity _dropCandidate;

        public bool Enabled { get; set; } = true;
        public bool IsDragging => _dragged != null;

        private BoardModel Board => level.Board;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        public void ResetState()
        {
            _history.Clear();
            _pressed = null;
            _dragged = null;
            SetDropCandidate(null);
            Select(null);
        }

        public bool Undo() => _history.TryUndo(Board, this);

        private void Update()
        {
            if (!Enabled)
            {
                if (_dragged != null) CancelDrag();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                _pressScreen = Input.mousePosition;
                _pressed = PickPin();
            }
            else if (Input.GetMouseButton(0))
            {
                if (_pressed != null && _dragged == null &&
                    Vector2.Distance(_pressScreen, Input.mousePosition) > dragThresholdPixels && CanControl(_pressed))
                    BeginDrag(_pressed);

                if (_dragged != null && TrySurfacePoint(out var hit))
                {
                    _dragTarget = ClampToPins(_dragged, hit + dragOffset);
                    SetDropCandidate(ClosestDropCell(_dragged, new Vector2(_dragTarget.x, _dragTarget.z)));
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                if (_dragged != null) Drop();
                else Tap(_pressed);
                _pressed = null;
            }
        }

        private void FixedUpdate()
        {
            if (_dragged == null) return;

            // The rope can free itself and merge mid-air; the merge animation owns the pin then.
            if (!CanControl(_dragged))
            {
                ReleaseDragged();
                return;
            }

            _dragTime += Time.fixedDeltaTime;
            if (ShouldCancelDrag())
            {
                CancelDrag();
                return;
            }

            var view = level.PinViews[_dragged.Id].transform;
            view.position = Vector3.Lerp(view.position, _dragTarget, Mathf.Clamp01(Time.fixedDeltaTime * moveTime));
        }

        private static bool CanControl(BoardModel.Pin p) => p != null && !p.Removed && !p.Locked && p.InputEnabled;

        // ---------- drag ----------
        private void BeginDrag(BoardModel.Pin pin)
        {
            Select(null);
            _dragged = pin;
            _dragTime = 0f;
            var view = level.PinViews[pin.Id].transform;
            view.DOKill();
            _dragTarget = view.position + dragOffset;
            level.SetPinPlaced(pin.Id, false);
        }

        private void Drop()
        {
            var pin = _dragged;
            var cell = _dropCandidate != null ? _dropCandidate.Cell : (Vector2Int?)null;
            ReleaseDragged();
            if (cell.HasValue && cell.Value != pin.Cell) MoveTo(pin, cell.Value, record: true, placeSpeed, placeEase, byDistance: false);
            else PlaceBack(pin);
        }

        private void CancelDrag()
        {
            var pin = _dragged;
            ReleaseDragged();
            PlaceBack(pin);
        }

        private void ReleaseDragged()
        {
            _dragged = null;
            _pressed = null;
            SetDropCandidate(null);
        }

        // A rope pulled by the dragged pin (or a rope it is touching) is too tense.
        private bool ShouldCancelDrag()
        {
            float max = 0f;
            foreach (var rope in _dragged.Ropes)
            {
                if (rope.Merged || !level.RopeViews.TryGetValue(rope.Id, out var view)) continue;
                max = Mathf.Max(max, view.TensionPercentage);
                foreach (var other in level.RopeViews.Values)
                    if (other != view && collisions.IsCollidingWith(view.Obi, other.Obi))
                        max = Mathf.Max(max, other.TensionPercentage);
            }

            return (_dragTime >= cancelTensionDelay && max >= cancelTension) ||
                   (_dragTime >= cancelHigherTensionDelay && max >= cancelHigherTension);
        }

        // Keeps the pin within maxPinDistance of every pin it is roped to.
        private Vector3 ClampToPins(BoardModel.Pin pin, Vector3 target)
        {
            foreach (var rope in pin.Ropes)
            {
                if (rope.Merged || !level.PinViews.TryGetValue(rope.Other(pin).Id, out var other)) continue;
                var anchor = other.transform.position;
                var d = target - anchor;
                if (d.magnitude > BoardModel.MaxPinDistance) target = anchor + d.normalized * BoardModel.MaxPinDistance;
            }
            return target;
        }

        private GridCellEntity ClosestDropCell(BoardModel.Pin pin, Vector2 plane)
        {
            GridCellEntity best = null;
            float bestD = dropSnapRadius * dropSnapRadius;
            foreach (var kv in level.Cells)
            {
                if (kv.Key != pin.Cell && !Board.CanMove(pin, kv.Key)) continue;
                float d = (level.Grid.CellToPlane(kv.Key) - plane).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = kv.Value;
                }
            }
            return best;
        }

        private void SetDropCandidate(GridCellEntity cell)
        {
            if (_dropCandidate == cell) return;
            if (_dropCandidate != null) _dropCandidate.SetHighlight(false);
            _dropCandidate = cell;
            if (_dropCandidate != null) _dropCandidate.SetHighlight(true);
        }

        // ---------- tap-move ----------
        private void Tap(BoardModel.Pin tappedPin)
        {
            if (tappedPin != null)
            {
                Select(_selected == tappedPin || !CanControl(tappedPin) ? null : tappedPin);
                return;
            }

            if (_selected == null || !TrySurfacePoint(out var p)) return;
            var cell = level.Grid.WorldToCell(p);
            if (CanControl(_selected) && Board.CanMove(_selected, cell) && !IsAnyRopeTooTense(_selected))
                MoveTo(_selected, cell, record: true, tapMoveSecondsPerUnit, tapMoveEase, byDistance: true);
            Select(null);
        }

        private bool IsAnyRopeTooTense(BoardModel.Pin pin)
        {
            foreach (var rope in pin.Ropes)
                if (!rope.Merged && level.RopeViews.TryGetValue(rope.Id, out var v) && v.TensionPercentage >= cancelTension)
                    return true;
            return false;
        }

        private void Select(BoardModel.Pin pin)
        {
            if (_selected != null && level.PinViews.TryGetValue(_selected.Id, out var old)) old.SetSelected(false);
            foreach (var c in _highlighted) c.SetHighlight(false);
            _highlighted.Clear();

            _selected = pin;
            if (pin == null) return;
            level.PinViews[pin.Id].SetSelected(true);
            foreach (var kv in level.Cells)
                if (Board.CanMove(pin, kv.Key))
                {
                    kv.Value.SetHighlight(true);
                    _highlighted.Add(kv.Value);
                }
        }

        // ---------- shared ----------
        // byDistance: duration = distance * value (tap-move); otherwise value is a speed (slot transfer).
        private void MoveTo(BoardModel.Pin pin, Vector2Int cell, bool record, float value, Ease ease, bool byDistance)
        {
            var from = pin.Cell;
            Board.CommitMove(pin, cell, countMove: record);
            if (record) _history.Push(pin.Id, from, cell);
            Fly(pin, level.Grid.CellToWorld(cell), value, ease, byDistance);
        }

        public void MoveTo(BoardModel.Pin pin, Vector2Int cell, bool record) =>
            MoveTo(pin, cell, record, tapMoveSecondsPerUnit, tapMoveEase, byDistance: true);

        private void PlaceBack(BoardModel.Pin pin)
        {
            Fly(pin, level.Grid.CellToWorld(pin.Cell), placeSpeed, placeEase, byDistance: false);
        }

        private void Fly(BoardModel.Pin pin, Vector3 target, float value, Ease ease, bool byDistance)
        {
            var view = level.PinViews[pin.Id].transform;
            float dist = Vector3.Distance(view.position, target);
            float duration = byDistance ? dist * value : dist / Mathf.Max(0.01f, value);
            level.SetPinPlaced(pin.Id, false);
            view.DOKill();
            view.DOMove(target, duration).SetEase(ease).SetTarget(view)
                .OnComplete(() => level.SetPinPlaced(pin.Id, true));
        }

        // Closest pin under the pointer on the board plane.
        private BoardModel.Pin PickPin()
        {
            if (!TrySurfacePoint(out var p)) return null;
            BoardModel.Pin best = null;
            float bestD = pickRadius * pickRadius;
            foreach (var pin in Board.Pins.Values)
            {
                if (pin.Removed || !level.PinViews.TryGetValue(pin.Id, out var view)) continue;
                var v = view.transform.position;
                float d = (new Vector2(v.x, v.z) - new Vector2(p.x, p.z)).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = pin;
                }
            }
            return best;
        }

        private bool TrySurfacePoint(out Vector3 point)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, level.Grid.Origin + Vector3.up * dragSurfaceHeight);
            if (plane.Raycast(ray, out float t))
            {
                point = ray.GetPoint(t);
                return true;
            }

            point = default;
            return false;
        }
    }
}
