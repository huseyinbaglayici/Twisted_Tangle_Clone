using System;
using DG.Tweening;
using Runtime.Entities;
using Runtime.Level;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Runtime.Gameplay
{
    public class PinInputController : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private RopeCollisionTracker collisions;
        [SerializeField] private Camera cam;

        [Header("Pick / drag")]
        [SerializeField] private float pickRadius = 0.45f;
        [SerializeField] private float dragThresholdPixels = 10f;
        [SerializeField] private float dragSurfaceHeight = 0.033f;
        [SerializeField] private Vector3 liftOffset = new(0f, 0.75f, 0f);
        [SerializeField] private float followSpeed = 30f;
        [SerializeField] private float dropSnapRadius = 0.5f;

        [Header("Drag cancel (rope tension)")]
        [SerializeField] private float cancelTension = 0.55f;
        [SerializeField] private float cancelTensionDelay = 0.25f;
        [SerializeField] private float cancelHigherTension = 0.75f;
        [SerializeField] private float cancelHigherTensionDelay = 0.1f;

        [Header("Over-stretched move revert")]
        [SerializeField] private float revertCheckDelay = 0.2f;

        [Header("Placement tweens")]
        [SerializeField] private float placeSpeed = 6f;
        [SerializeField] private Ease placeEase = Ease.OutSine;
        [SerializeField] private float hopSecondsPerUnit = 0.2f;
        [SerializeField] private Ease hopEase = Ease.InOutQuad;

        private BoardModel.Pin _pressed, _dragged;
        private Vector2 _pressScreen;
        private Vector3 _dragTarget;
        private float _dragTime;
        private GridCellEntity _dropCandidate, _originCell;

        public event Action<PinEntity> PinPicked;
        public event Action<PinEntity> PinDropped;
        public event Action<PinEntity> PinMoveRejected;

        public bool Enabled { get; set; } = true;
        public bool IsHoldingPin => _dragged != null || (_pressed != null && Board.CanControl(_pressed));

        private BoardModel Board => level.Board;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        public void ResetState()
        {
            _pressed = null;
            _dragged = null;
            SetDropCandidate(null);
            SetOriginCell(null);
        }

        private void Update()
        {
            if (!Enabled)
            {
                if (_dragged != null) AbortDrag();
                _pressed = null;
                return;
            }

            FollowDragTarget();

            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                _pressScreen = Input.mousePosition;
                _pressed = PickPin();
            }
            else if (Input.GetMouseButton(0))
            {
                if (_pressed != null && _dragged == null &&
                    Vector2.Distance(_pressScreen, Input.mousePosition) > dragThresholdPixels && Board.CanControl(_pressed))
                    BeginDrag(_pressed);

                if (_dragged != null && TrySurfacePoint(out var hit))
                {
                    _dragTarget = ClampToPins(_dragged, hit + liftOffset);
                    SetDropCandidate(ClosestDropCell(_dragged, new Vector2(_dragTarget.x, _dragTarget.z)));
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                if (_dragged != null) Drop();
                _pressed = null;
            }
        }

        private void FixedUpdate()
        {
            if (_dragged == null) return;

            // The pin can get frozen mid-drag (its merge chain started); the merge animation owns it then.
            if (!Board.CanControl(_dragged))
            {
                ReleaseDragged();
                return;
            }

            _dragTime += Time.fixedDeltaTime;
            if (IsDragOverStretched())
            {
                var pin = _dragged;
                AbortDrag();
                Reject(pin);
            }
        }

        // Per rendered frame, not per physics step, so the held pin moves smoothly at any frame rate.
        private void FollowDragTarget()
        {
            if (_dragged == null || !Board.CanControl(_dragged)) return;
            var view = level.PinViews[_dragged.Id].transform;
            view.position = Vector3.Lerp(view.position, _dragTarget, Mathf.Clamp01(Time.deltaTime * followSpeed));
        }

        private void BeginDrag(BoardModel.Pin pin)
        {
            _dragged = pin;
            _dragTime = 0f;
            var view = level.PinViews[pin.Id].transform;
            view.DOKill();
            _dragTarget = view.position + liftOffset;
            level.SetPinPlaced(pin.Id, false);
            SetOriginCell(level.Cells.TryGetValue(pin.Cell, out var origin) ? origin : null);
            PinPicked?.Invoke(level.PinViews[pin.Id]);
        }

        private void Drop()
        {
            var pin = _dragged;
            var cell = _dropCandidate != null ? _dropCandidate.Cell : (Vector2Int?)null;
            ReleaseDragged();
            if (cell.HasValue && cell.Value != pin.Cell) MoveTo(pin, cell.Value);
            else PlaceBack(pin);
        }

        private void AbortDrag()
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
            SetOriginCell(null);
        }

        private bool IsDragOverStretched()
        {
            float max = 0f;
            foreach (var rope in _dragged.Ropes)
            {
                if (rope.Merged || !level.RopeViews.TryGetValue(rope.Id, out var view)) continue;
                max = Mathf.Max(max, view.TensionRatio);
                foreach (var other in level.RopeViews.Values)
                    if (other != view && collisions.IsCollidingWith(view.Obi, other.Obi))
                        max = Mathf.Max(max, other.TensionRatio);
            }

            return (_dragTime >= cancelTensionDelay && max >= cancelTension) ||
                   (_dragTime >= cancelHigherTensionDelay && max >= cancelHigherTension);
        }

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

        private void SetOriginCell(GridCellEntity cell)
        {
            if (_originCell == cell) return;
            if (_originCell != null) _originCell.SetOrigin(false);
            _originCell = cell;
            if (_originCell != null) _originCell.SetOrigin(true);
        }

        private void Reject(BoardModel.Pin pin)
        {
            if (level.PinViews.TryGetValue(pin.Id, out var view)) PinMoveRejected?.Invoke(view);
        }

        private void MoveTo(BoardModel.Pin pin, Vector2Int cell)
        {
            var from = pin.Cell;
            Board.CommitMove(pin, cell);
            Fly(pin, level.Grid.CellToWorld(cell), placeSpeed, placeEase, byDistance: false);
            var view = level.PinViews[pin.Id];
            PinDropped?.Invoke(view);
            // Targeting the view means a new grab (DOKill) cancels the over-stretch check.
            DOVirtual.DelayedCall(revertCheckDelay, () => RevertIfOverStretched(pin, from, cell))
                .SetTarget(view.transform);
        }

        private void RevertIfOverStretched(BoardModel.Pin pin, Vector2Int from, Vector2Int to)
        {
            if (pin.Cell != to || !Board.CanControl(pin) || pin == _dragged || !IsAnyRopeTooTense(pin)) return;
            if (!Board.IsCellFree(from)) return;
            Board.CommitMove(pin, from);
            Fly(pin, level.Grid.CellToWorld(from), hopSecondsPerUnit, hopEase, byDistance: true);
            Reject(pin);
        }

        private bool IsAnyRopeTooTense(BoardModel.Pin pin)
        {
            foreach (var rope in pin.Ropes)
                if (!rope.Merged && level.RopeViews.TryGetValue(rope.Id, out var v) && v.TensionRatio >= cancelTension)
                    return true;
            return false;
        }

        private void PlaceBack(BoardModel.Pin pin)
        {
            Fly(pin, level.Grid.CellToWorld(pin.Cell), placeSpeed, placeEase, byDistance: false);
        }

        // byDistance: duration = distance * value (revert hop); otherwise value is a speed.
        private void Fly(BoardModel.Pin pin, Vector3 target, float value, Ease ease, bool byDistance)
        {
            var view = level.PinViews[pin.Id].transform;
            float dist = Vector3.Distance(view.position, target);
            float duration = byDistance ? dist * value : dist / Mathf.Max(0.01f, value);
            level.SetPinPlaced(pin.Id, false);
            view.DOKill();
            // A pin sent back from its slot hops over the ropes at drag height instead of ploughing
            // through them; a released drag is already lifted and just slides in.
            Tween tween = byDistance
                ? view.DOJump(target, liftOffset.y, 1, duration)
                : view.DOMove(target, duration);
            tween.SetEase(ease).SetTarget(view).OnComplete(() => level.SetPinPlaced(pin.Id, true));
        }

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
