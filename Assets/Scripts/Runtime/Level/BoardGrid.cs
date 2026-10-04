using UnityEngine;

namespace Runtime.Level
{
    // Board grid: spacing 1, XZ plane, centered.
    // map N => (3+N) columns x (5+N) rows. Cell (c,r) => world (c-(W-1)/2, 0, r-(H-1)/2).
    public readonly struct BoardGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float Spacing;
        public readonly Vector3 Origin; // board center in world

        public BoardGrid(int width, int height, float spacing = 1f, Vector3 origin = default)
        {
            Width = width;
            Height = height;
            Spacing = spacing;
            Origin = origin;
        }

        public static BoardGrid FromMapId(int mapId) =>
            new(StageJson.WidthForMap(mapId), StageJson.HeightForMap(mapId));

        public Vector3 CellToWorld(Vector2Int cell) =>
            Origin + new Vector3((cell.x - (Width - 1) * 0.5f) * Spacing, 0f, (cell.y - (Height - 1) * 0.5f) * Spacing);

        public Vector2 CellToPlane(Vector2Int cell)
        {
            var w = CellToWorld(cell);
            return new Vector2(w.x, w.z);
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            var local = (world - Origin) / Spacing;
            return new Vector2Int(Mathf.RoundToInt(local.x + (Width - 1) * 0.5f),
                                  Mathf.RoundToInt(local.z + (Height - 1) * 0.5f));
        }

        public bool Contains(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
    }
}
