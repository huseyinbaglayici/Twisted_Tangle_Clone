using Runtime.Level;
using UnityEngine;

namespace TwistedTangle.Editor.Utils
{
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
