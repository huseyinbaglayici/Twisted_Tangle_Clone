using UnityEngine;

namespace Runtime.Level
{
    public readonly struct BoardGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float Spacing;
        public readonly Vector3 Origin;

        public BoardGrid(int width, int height, float spacing = 1f, Vector3 origin = default)
        {
            Width = width;
            Height = height;
            Spacing = spacing;
            Origin = origin;
        }

        public Vector3 CellToWorld(Vector2Int cell) =>
            Origin + new Vector3((cell.x - (Width - 1) * 0.5f) * Spacing, 0f, (cell.y - (Height - 1) * 0.5f) * Spacing);

        public Vector2 CellToPlane(Vector2Int cell)
        {
            var w = CellToWorld(cell);
            return new Vector2(w.x, w.z);
        }

        public bool Contains(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
    }
}
