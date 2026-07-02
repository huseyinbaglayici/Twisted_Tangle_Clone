using TwistedTangle.Editor.Canvas;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEngine;

namespace TwistedTangle.Editor.Settings
{
    [CreateAssetMenu(
        fileName = "EntityEditorData_New",
        menuName = "TwistedTangle/Entity Editor Data",
        order = 1)]
    public class EntityDefinitionEditorDataSO : ScriptableObject
    {
        [SerializeField] private EntityDefinitionSO definition;
        [SerializeField] private EntityBaseTypeSO baseType;
        [SerializeField] private Color editorColor = new(0.85f, 0.85f, 0.85f);
        [SerializeField] private CanvasMarker canvasMarker = CanvasMarker.None;
        [SerializeField] private int sortOrder = 0;

        public EntityDefinitionSO Definition  => definition;
        public EntityBaseTypeSO   BaseType    => baseType;
        public Color              EditorColor => editorColor;
        public CanvasMarker       CanvasMarker => canvasMarker;
        public int                SortOrder   => sortOrder;
    }
}
