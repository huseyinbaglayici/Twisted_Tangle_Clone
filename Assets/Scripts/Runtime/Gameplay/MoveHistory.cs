using System.Collections.Generic;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Undo stack of committed pin moves.
    public class MoveHistory
    {
        private readonly Stack<(int pin, Vector2Int from, Vector2Int to)> _stack = new();

        public void Push(int pin, Vector2Int from, Vector2Int to) => _stack.Push((pin, from, to));

        public void Clear() => _stack.Clear();

        public bool TryUndo(BoardModel board, PinInputController input)
        {
            while (_stack.Count > 0)
            {
                var m = _stack.Pop();
                if (!board.Pins.TryGetValue(m.pin, out var pin) || pin.Removed || !pin.InputEnabled)
                    continue; // its rope already merged
                if (!board.IsCellFree(m.from) || !board.IsWithinPinDistance(pin, m.from))
                    return false;
                input.MoveTo(pin, m.from, record: false);
                return true;
            }
            return false;
        }
    }
}
