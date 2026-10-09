using System.Collections.Generic;
using Obi;
using Reflex.Attributes;
using Runtime.Boostrap.Pool;
using Runtime.Entities;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Builds a level stage from pooled views: grid slots -> pins -> Obi ropes.
    public class LevelBuilder : MonoBehaviour
    {
        [Inject] private PoolRegistry _pools;

        [SerializeField] private Transform boardRoot;   // board center
        [SerializeField] private RopePaletteSO palette;

        private readonly HashSet<int> _unplacedPins = new();
        private readonly List<Vector3> _particles = new();
        private ObiSolver _solver;

        public BoardModel Board { get; } = new();
        public readonly Dictionary<int, PinEntity> PinViews = new();
        public readonly Dictionary<int, RopeEntity> RopeViews = new();
        public readonly Dictionary<Vector2Int, GridCellEntity> Cells = new();

        public BoardGrid Grid { get; private set; }
        public ObiSolver Solver => _solver != null ? _solver : _solver = FindFirstObjectByType<ObiSolver>();

        private Vector3 Origin => boardRoot != null ? boardRoot.position : transform.position;

        public void Build(LevelJson level, int stageIndex = 0)
        {
            Clear();
            var stage = level.stages[stageIndex];
            Grid = new BoardGrid(stage.gridWidth, stage.gridHeight, 1f, Origin);
            Board.Load(stage, Grid);

            // Pooled objects stay out of this scene (pool holders / solver live in Boostrap): parenting them here
            // would destroy them with the scene instead of returning them to the pool.
            var cellPool = _pools.Get<GridCellEntity>();
            for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
            {
                var c = new Vector2Int(x, y);
                var cell = cellPool.Dequeue();
                cell.Setup(c, Grid.CellToWorld(c));
                Cells[c] = cell;
            }

            var pinPool = _pools.Get<PinEntity>();
            foreach (var pin in Board.Pins.Values)
            {
                var view = pinPool.Dequeue();
                view.Setup(pin, Grid.CellToWorld(pin.Cell), PinColor(pin));
                PinViews[pin.Id] = view;
            }

            // Obi actors must sit under the solver to simulate.
            var ropePool = _pools.Get<RopeEntity>();
            for (int i = 0; i < Board.Ropes.Count; i++)
            {
                var rope = Board.Ropes[i];
                var view = ropePool.Dequeue(Solver.transform);
                ParticlePositions(stage.ropes[i], rope);
                view.Setup(rope, RopeColor(rope.ColorIndex),
                    PinViews[rope.A.Id].RopeConnectPoint, PinViews[rope.B.Id].RopeConnectPoint,
                    _particles, rope.A.Ropes.Count > 1, rope.B.Ropes.Count > 1);
                RopeViews[rope.Id] = view;
            }
        }

        // Authored particle path (cell space + height) -> world. Ends snap onto the pins' connect points.
        private void ParticlePositions(RopeJson json, BoardModel.Rope rope)
        {
            _particles.Clear();
            var origin = Grid.CellToWorld(Vector2Int.zero);
            if (json.path != null && json.path.Count >= 2)
            {
                foreach (var p in json.path)
                    _particles.Add(origin + new Vector3(p.x * Grid.Spacing, p.h, p.y * Grid.Spacing));
            }
            else
            {
                var a = PinViews[rope.A.Id].RopeConnectPoint.position;
                var b = PinViews[rope.B.Id].RopeConnectPoint.position;
                for (int i = 0; i < 25; i++) _particles.Add(Vector3.Lerp(a, b, i / 24f));
            }
        }

        public bool IsPinPlaced(int pinId) => !_unplacedPins.Contains(pinId);

        public void SetPinPlaced(int pinId, bool placed)
        {
            if (placed) _unplacedPins.Remove(pinId);
            else _unplacedPins.Add(pinId);
        }

        // Returns every view to its pool.
        public void Clear()
        {
            _pools?.DespawnAll();
            PinViews.Clear();
            RopeViews.Clear();
            Cells.Clear();
            _unplacedPins.Clear();
        }

        public void ReleasePin(int id)
        {
            if (PinViews.Remove(id, out var v)) _pools.Get<PinEntity>().Enqueue(v);
        }

        public void ReleaseRope(int id)
        {
            if (RopeViews.Remove(id, out var v)) _pools.Get<RopeEntity>().Enqueue(v);
        }

        private Color RopeColor(int colorIndex) => palette != null ? palette.Get(colorIndex) : Color.white;

        private Color PinColor(BoardModel.Pin pin) =>
            pin.Ropes.Count > 0 ? RopeColor(pin.Ropes[0].ColorIndex) : Color.white;

        private void OnDestroy() => Clear();
    }
}
