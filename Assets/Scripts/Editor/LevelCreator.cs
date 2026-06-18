using UnityEditor;

namespace Editor
{
    public class LevelCreator : EditorWindow
    {
        [MenuItem("TwistedTangle/Level Creation Tool")]
        public static void ShowWindow() => GetWindow<LevelCreator>();
    }
}