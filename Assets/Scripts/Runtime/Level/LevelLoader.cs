using TwistedTangle.Runtime.Data.Enums;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using TwistedTangle.Runtime.Data.ValueObjects;
using TwistedTangle.Runtime.Geometry;
using UnityEngine;

namespace TwistedTangle.Runtime.Level
{
    /// <summary>
    /// Instantiates a <see cref="LevelDataSO"/> in the scene exactly as authored in the editor:
    /// pegs at their grid cells (using the prefab from each peg's <see cref="PegDefinitionSO"/>) and
    /// ropes following their waypoint paths. Rope over/under order is reproduced via a per-layer depth
    /// offset so higher-layer ropes draw in front. This is render/topology only — no gameplay/solving
    /// logic, which is out of scope.
    /// </summary>
    public class LevelLoader : MonoBehaviour
    {
        [Header("Level")]
        [SerializeField] private LevelDataSO level;
        [SerializeField] private bool loadOnStart = true;

        [Header("Layout")]
        [Tooltip("World distance between adjacent grid cells.")]
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private float ropeWidth = 0.08f;
        [Tooltip("World height the ropes sit above the board; scaled per rope layer for over/under.")]
        [SerializeField] private float ropeBaseHeight = 0.1f;
        [SerializeField] private float layerHeightStep = 0.02f;

        private PegRegistry _registry;
        private Transform _root;

        private void Start()
        {
            if (loadOnStart && level != null) Load(level);
        }

        public void Load(LevelDataSO data)
        {
            if (data == null) return;
            level = data;
            _registry ??= new PegRegistry();

            RebuildRoot();
            SpawnPegs(data);
            SpawnRopes(data);
        }

        private void RebuildRoot()
        {
            if (_root != null) DestroyImmediate(_root.gameObject);
            _root = new GameObject("Level").transform;
            _root.SetParent(transform, false);
        }

        private void SpawnPegs(LevelDataSO data)
        {
            foreach (var peg in data.Pegs)
            {
                var def = _registry.Get(peg.TypeId);
                Vector3 pos = GridToWorld(peg.Coordinates);

                GameObject go;
                if (def != null && def.Prefab != null)
                {
                    go = Instantiate(def.Prefab, pos, Quaternion.identity, _root);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.SetParent(_root, false);
                    go.transform.position = pos;
                    go.transform.localScale = new Vector3(cellSize * 0.25f, cellSize * 0.25f, cellSize * 0.25f);
                    if (def != null) TintRenderer(go.GetComponent<Renderer>(), def.EditorColor);
                }

                go.name = $"Peg_{peg.TypeId}_{peg.Coordinates.x}_{peg.Coordinates.y}";
            }
        }

        private void SpawnRopes(LevelDataSO data)
        {
            foreach (var rope in data.Ropes)
            {
                if (rope?.Path == null || rope.Path.Count < 2) continue;

                var go = new GameObject($"Rope_{rope.RopeId}");
                go.transform.SetParent(_root, false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.numCornerVertices = 4;
                lr.numCapVertices = 4;
                lr.widthMultiplier = ropeWidth;
                lr.alignment = LineAlignment.View;

                Color color = EntityColors.Resolve(rope.Color);
                lr.material = UnlitMaterial(color);
                lr.startColor = lr.endColor = color;
                // Higher layer => closer to the top of the board => drawn over lower layers.
                lr.sortingOrder = rope.Layer;

                float y = ropeBaseHeight + rope.Layer * layerHeightStep;
                lr.positionCount = rope.Path.Count;
                for (int i = 0; i < rope.Path.Count; i++)
                {
                    Vector3 p = GridToWorld(rope.Path[i].PegCoord);
                    p.y = y;
                    lr.SetPosition(i, p);
                }
            }
        }

        /// <summary>Grid cell -> centered world position on the XZ plane (matches editor cell centers).</summary>
        public Vector3 GridToWorld(Vector2Int coord)
        {
            float x = (coord.x - (level.GridWidth - 1) * 0.5f) * cellSize;
            float z = (coord.y - (level.GridHeight - 1) * 0.5f) * cellSize;
            return _root != null ? _root.TransformPoint(new Vector3(x, 0f, z)) : new Vector3(x, 0f, z);
        }

        private static void TintRenderer(Renderer r, Color color)
        {
            if (r == null) return;
            r.material.color = color;
        }

        /// <summary>Pipeline-agnostic unlit material for rope lines.</summary>
        private static Material UnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { color = color };
            return mat;
        }
    }
}
