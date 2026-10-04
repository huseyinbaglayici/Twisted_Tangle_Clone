using System.Collections.Generic;
using Reflex.Attributes;
using Runtime.Boostrap.Pool;
using Runtime.Entities;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Builds a level stage from pooled views: grid slots -> pins -> ropes.
    public class LevelBuilder : MonoBehaviour
    {
        [Inject] private PoolRegistry _pools;

        [SerializeField] private Transform boardRoot;   // board center
        [SerializeField] private RopePaletteSO palette;

        public BoardModel Board { get; } = new();
        public readonly Dictionary<int, PinEntity> PinViews = new();
        public readonly Dictionary<int, RopeEntity> RopeViews = new();
        public readonly Dictionary<Vector2Int, GridCellEntity> Cells = new();

        public BoardGrid Grid { get; private set; }

        private Vector3 Origin => boardRoot != null ? boardRoot.position : transform.position;

        public void Build(LevelJson level, int stageIndex = 0)
        {
            Clear();
            var stage = level.stages[stageIndex];
            Grid = new BoardGrid(stage.gridWidth, stage.gridHeight, 1f, Origin);
            Board.Load(stage, level.moveLimit, Grid);

            // Pooled objects stay under their pool holders (root scene): parenting them into this scene would
            // destroy them with the scene instead of returning them to the pool.
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

            var ropePool = _pools.Get<RopeEntity>();
            foreach (var rope in Board.Ropes)
            {
                var view = ropePool.Dequeue();
                view.Setup(rope, RopeColor(rope.ColorIndex));
                RopeViews[rope.Id] = view;
            }
        }

        // Returns every view to its pool.
        public void Clear()
        {
            _pools?.DespawnAll();
            PinViews.Clear();
            RopeViews.Clear();
            Cells.Clear();
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

        private void LateUpdate()
        {
            foreach (var v in RopeViews.Values) v.Refresh();
        }

        private void OnDestroy() => Clear();
    }
}
