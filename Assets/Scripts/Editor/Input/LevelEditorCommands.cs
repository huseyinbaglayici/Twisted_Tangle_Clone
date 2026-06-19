using System.Collections.Generic;
using UnityEngine;

namespace TwistedTangle.Editor.Input
{
    /// <summary>
    /// The catalog of Level Creator actions that can be bound to a shortcut. This is pure metadata —
    /// it knows nothing about how an action runs. <see cref="LevelCreator"/> maps these ids to its own
    /// methods, and <see cref="KeyBindingWindow"/> renders one row per entry. Add an action here and it
    /// shows up in the bindings window automatically; wire its id to a method in the creator to run it.
    /// </summary>
    public static class LevelEditorCommands
    {
        // Tools
        public const string ToolPeg = "tool.peg";
        public const string ToolRope = "tool.rope";
        public const string ToolErase = "tool.erase";
        public const string ToolFlip = "tool.flip";

        // Level IO
        public const string Save = "level.save";
        public const string Load = "level.load";
        public const string Delete = "level.delete";
        public const string GenerateGrid = "level.generate";

        // Rope authoring (operate on the in-progress or selected rope)
        public const string FinishRope = "rope.finish";
        public const string CancelRope = "rope.cancel";
        public const string RopeToFront = "rope.front";
        public const string RopeToBack = "rope.back";
        public const string RopeDelete = "rope.delete";

        // Validation
        public const string Validate = "validate";

        /// <summary>All bindable actions, in display order (grouped by <see cref="EditorCommand.Category"/>).</summary>
        public static readonly IReadOnlyList<EditorCommand> All = new List<EditorCommand>
        {
            new(ToolPeg,      "Tools",      "Peg tool",               new KeyCombo(KeyCode.B)),
            new(ToolRope,     "Tools",      "Rope tool",              new KeyCombo(KeyCode.R)),
            new(ToolErase,    "Tools",      "Erase Peg tool",         new KeyCombo(KeyCode.E)),
            new(ToolFlip,     "Tools",      "Flip Crossing tool",     new KeyCombo(KeyCode.F)),

            new(Save,         "Level",      "Save level",             new KeyCombo(KeyCode.S, ctrl: true)),
            new(Load,         "Level",      "Load level",             new KeyCombo(KeyCode.L, ctrl: true)),
            new(Delete,       "Level",      "Delete level",           KeyCombo.None),
            new(GenerateGrid, "Level",      "Generate grid",          new KeyCombo(KeyCode.G, ctrl: true)),

            new(FinishRope,   "Rope",       "Finish rope",            new KeyCombo(KeyCode.Return)),
            new(CancelRope,   "Rope",       "Cancel rope",            new KeyCombo(KeyCode.Escape)),
            new(RopeToFront,  "Rope",       "Selected rope to front", new KeyCombo(KeyCode.PageUp)),
            new(RopeToBack,   "Rope",       "Selected rope to back",  new KeyCombo(KeyCode.PageDown)),
            new(RopeDelete,   "Rope",       "Delete selected rope",   new KeyCombo(KeyCode.Delete, shift: true)),

            new(Validate,     "Validation", "Validate level",         new KeyCombo(KeyCode.T, ctrl: true)),
        };

        public static EditorCommand Find(string id)
        {
            foreach (var c in All)
                if (c.Id == id) return c;
            return null;
        }
    }
}
