using System.Collections.Generic;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    public class MoveHistory
    {
        private readonly Stack<(int pin, Vector2Int from, Vector2Int to)> _stack = new();

        public void Push(int pin, Vector2Int from, Vector2Int to) => _stack.Push((pin, from, to));

        public void Clear() => _stack.Clear();

        public void DiscardLast(int pin)
        {
            if (_stack.Count > 0 && _stack.Peek().pin == pin) _stack.Pop();
        }

        public bool TryUndo(BoardModel board, PinInputController input)
        {
            while (_stack.Count > 0)
            {
                var m = _stack.Pop();
                if (!board.Pins.TryGetValue(m.pin, out var pin) || pin.Removed)
                    continue;
                if (!board.CanControl(pin) || !board.IsCellFree(m.from) || !board.IsWithinPinDistance(pin, m.from))
                {
                    _stack.Push(m);
                    return false;
                }
                input.MoveTo(pin, m.from, record: false);
                return true;
            }
            return false;
        }
    }
}
