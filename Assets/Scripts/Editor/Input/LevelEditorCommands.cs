using System.Collections.Generic;
using UnityEngine;

namespace Editor.Input
{
    /// <summary>
    /// Catalog of Level Creator shortcuts. Pure metadata — LevelCreator maps ids to callbacks.
    /// </summary>
    public static class LevelEditorCommands
    {
        // Tools
        public const string ToolPin = "tool.pin";
        public const string ToolRope = "tool.rope";
        public const string ToolLock = "tool.lock";
        public const string ToolErase = "tool.erase";

        // Level IO
        public const string Save = "level.save";
        public const string Load = "level.load";
        public const string Delete = "level.delete";
        public const string NewLevel = "level.generate";

        // Rope authoring (operate on the in-progress or selected rope)
        public const string CancelRope = "rope.cancel";
        public const string RopeToFront = "rope.front";
        public const string RopeToBack = "rope.back";
        public const string RopeDelete = "rope.delete";

        // Validation
        public const string Validate = "validate";

        /// <summary>All bindable actions, in display order (grouped by <see cref="EditorCommand.Category"/>).</summary>
        public static readonly IReadOnlyList<EditorCommand> All = new List<EditorCommand>
        {
            new(ToolPin, "Tools", "Pin tool", new KeyCombo(KeyCode.P)),
            new(ToolRope, "Tools", "Rope tool", new KeyCombo(KeyCode.R)),
            new(ToolLock, "Tools", "Lock tool", new KeyCombo(KeyCode.K)),
            new(ToolErase, "Tools", "Erase tool", new KeyCombo(KeyCode.E)),

            new(Save, "Level", "Save level", new KeyCombo(KeyCode.S, ctrl: true)),
            new(Load, "Level", "Load level", new KeyCombo(KeyCode.L, ctrl: true)),
            new(Delete, "Level", "Delete level", KeyCombo.None),
            new(NewLevel, "Level", "New level", new KeyCombo(KeyCode.G, ctrl: true)),

            new(CancelRope, "Rope", "Cancel rope", new KeyCombo(KeyCode.Escape)),
            new(RopeToFront, "Rope", "Selected rope to front", new KeyCombo(KeyCode.PageUp)),
            new(RopeToBack, "Rope", "Selected rope to back", new KeyCombo(KeyCode.PageDown)),
            new(RopeDelete, "Rope", "Delete selected rope", new KeyCombo(KeyCode.Delete, shift: true)),

            new(Validate, "Validation", "Validate level", new KeyCombo(KeyCode.T, ctrl: true)),
        };

        public static EditorCommand Find(string id)
        {
            foreach (var c in All)
                if (c.Id == id)
                    return c;
            return null;
        }
    }
}
