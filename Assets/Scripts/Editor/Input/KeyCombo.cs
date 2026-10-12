using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Editor.Input
{
    [Serializable]
    public struct KeyCombo : IEquatable<KeyCombo>
    {
        public KeyCode Key;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;

        public KeyCombo(KeyCode key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }

        public bool IsEmpty => Key == KeyCode.None;

        public static KeyCombo None => new KeyCombo(KeyCode.None);

        public static KeyCombo FromEvent(KeyDownEvent e)
        {
            var key = e.keyCode;
            if (key == KeyCode.None || IsModifierKey(key)) return None;
            // commandKey covers macOS Cmd; we fold it into Ctrl so bindings read the same on both OSes.
            return new KeyCombo(key, e.ctrlKey || e.commandKey, e.altKey, e.shiftKey);
        }

        public bool Matches(KeyDownEvent e)
        {
            if (IsEmpty) return false;
            return e.keyCode == Key
                   && (e.ctrlKey || e.commandKey) == Ctrl
                   && e.altKey == Alt
                   && e.shiftKey == Shift;
        }

        private static bool IsModifierKey(KeyCode k) =>
            k is KeyCode.LeftControl or KeyCode.RightControl
              or KeyCode.LeftAlt or KeyCode.RightAlt
              or KeyCode.LeftShift or KeyCode.RightShift
              or KeyCode.LeftCommand or KeyCode.RightCommand
              or KeyCode.LeftWindows or KeyCode.RightWindows
              or KeyCode.AltGr;

        public override string ToString()
        {
            if (IsEmpty) return "—";
            var s = string.Empty;
            if (Ctrl) s += "Ctrl+";
            if (Alt) s += "Alt+";
            if (Shift) s += "Shift+";
            return s + KeyLabel(Key);
        }

        private static string KeyLabel(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Delete: return "Del";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Space: return "Space";
                case KeyCode.Tab: return "Tab";
                case KeyCode.UpArrow: return "↑";
                case KeyCode.DownArrow: return "↓";
                case KeyCode.LeftArrow: return "←";
                case KeyCode.RightArrow: return "→";
                case KeyCode.PageUp: return "PageUp";
                case KeyCode.PageDown: return "PageDown";
                case KeyCode.Home: return "Home";
                case KeyCode.End: return "End";
            }

            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9)
                return ((char)('0' + (k - KeyCode.Alpha0))).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9)
                return "Num" + (char)('0' + (k - KeyCode.Keypad0));

            return k.ToString();
        }

        public bool Equals(KeyCombo other) =>
            Key == other.Key && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift;

        public override bool Equals(object obj) => obj is KeyCombo o && Equals(o);

        public override int GetHashCode() => HashCode.Combine((int)Key, Ctrl, Alt, Shift);
    }
}
