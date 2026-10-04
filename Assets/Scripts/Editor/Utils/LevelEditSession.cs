using Runtime.Level;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
    /// <summary>
    /// In-memory holder for the level being edited. A ScriptableObject only so Unity's Undo can record
    /// the nested LevelJson; never saved as an asset (the level itself lives in its JSON file).
    /// </summary>
    public class LevelEditSession : ScriptableObject
    {
        public LevelJson level;

        public static LevelEditSession Create(LevelJson level)
        {
            var session = CreateInstance<LevelEditSession>();
            session.hideFlags = HideFlags.DontSave;
            session.level = level;
            return session;
        }
    }
}
