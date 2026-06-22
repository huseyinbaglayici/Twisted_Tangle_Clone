using System;
using System.Collections.Generic;
using System.Linq;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEditor;
using UnityEngine;

namespace TwistedTangle.Editor.Input
{
    /// <summary>
    /// The catalog of Level Creator actions that can be bound to a shortcut. The built-in actions are
    /// fixed; the placement actions are discovered dynamically — one tool per <see cref="EntityBaseTypeSO"/>
    /// (listed by its name, e.g. "Pin") with its <see cref="EntityDefinitionSO"/> sub-types grouped under
    /// it — so creating a new entity type in the Level Creator makes matching, bindable shortcuts appear in
    /// the <see cref="KeyBindingWindow"/> automatically. This is pure metadata: <see cref="LevelCreator"/>
    /// maps these ids to its own methods. Call <see cref="Refresh"/> after entity assets change to rebuild
    /// (and announce) the dynamic list. <see cref="BaseGroups"/> exposes the base→sub-type hierarchy for the
    /// bindings window; <see cref="All"/> stays flat for shortcut lookup and conflict detection.
    /// </summary>
    public static class LevelEditorCommands
    {
        // Tools
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

        /// <summary>Prefix for the dynamic, per-base-type tool commands (id = prefix + BaseId).</summary>
        public const string BasePrefix = "base.";

        /// <summary>Prefix for the dynamic, per-entity-sub-type placement commands (id = prefix + TypeId).</summary>
        public const string EntityPrefix = "entity.";

        /// <summary>A base-type command together with its sub-type commands, for the nested bindings view.</summary>
        public sealed class BaseGroup
        {
            public EditorCommand Base { get; }
            public IReadOnlyList<EditorCommand> SubTypes { get; }

            public BaseGroup(EditorCommand baseCommand, IReadOnlyList<EditorCommand> subTypes)
            {
                Base = baseCommand;
                SubTypes = subTypes;
            }
        }

        /// <summary>The fixed, code-defined actions, in display order (grouped by <see cref="EditorCommand.Category"/>).</summary>
        private static readonly IReadOnlyList<EditorCommand> _builtin = new List<EditorCommand>
        {
            new(ToolRope, "Tools", "Rope tool", new KeyCombo(KeyCode.R)),
            new(ToolErase, "Tools", "Erase Entity tool", new KeyCombo(KeyCode.E)),
            new(ToolFlip, "Tools", "Flip Crossing tool", new KeyCombo(KeyCode.F)),

            new(Save, "Level", "Save level", new KeyCombo(KeyCode.S, ctrl: true)),
            new(Load, "Level", "Load level", new KeyCombo(KeyCode.L, ctrl: true)),
            new(Delete, "Level", "Delete level", KeyCombo.None),
            new(GenerateGrid, "Level", "Generate grid", new KeyCombo(KeyCode.G, ctrl: true)),

            new(FinishRope, "Rope", "Finish rope", new KeyCombo(KeyCode.Return)),
            new(CancelRope, "Rope", "Cancel rope", new KeyCombo(KeyCode.Escape)),
            new(RopeToFront, "Rope", "Selected rope to front", new KeyCombo(KeyCode.PageUp)),
            new(RopeToBack, "Rope", "Selected rope to back", new KeyCombo(KeyCode.PageDown)),
            new(RopeDelete, "Rope", "Delete selected rope", new KeyCombo(KeyCode.Delete, shift: true)),

            new(Validate, "Validation", "Validate level", new KeyCombo(KeyCode.T, ctrl: true)),
        };

        private static List<EditorCommand> _dynamic = new();
        private static List<BaseGroup> _baseGroups = new();
        private static List<EditorCommand> _ungroupedSubs = new();
        private static List<EditorCommand> _all;

        /// <summary>Raised when the dynamic command set changes, so open windows can refresh.</summary>
        public static event Action Changed;

        /// <summary>The fixed, code-defined actions (Tools, Level, Rope, Validation).</summary>
        public static IReadOnlyList<EditorCommand> Builtin => _builtin;

        /// <summary>All bindable actions, flat: the built-ins followed by the base + sub-type commands.</summary>
        public static IReadOnlyList<EditorCommand> All
        {
            get
            {
                if (_all == null) Rebuild();
                return _all;
            }
        }

        /// <summary>Each base type with its sub-types, for the nested "Base Types" view in the bindings window.</summary>
        public static IReadOnlyList<BaseGroup> BaseGroups
        {
            get
            {
                if (_all == null) Rebuild();
                return _baseGroups;
            }
        }

        /// <summary>Sub-types with no base type ("Ungrouped"), shown in their own foldout.</summary>
        public static IReadOnlyList<EditorCommand> UngroupedSubTypes
        {
            get
            {
                if (_all == null) Rebuild();
                return _ungroupedSubs;
            }
        }

        /// <summary>The command id for the tool that places a given base type (its first sub-type).</summary>
        public static string BaseCommandId(string baseId) => BasePrefix + baseId;

        /// <summary>The command id that places a given entity sub-type.</summary>
        public static string EntityCommandId(string typeId) => EntityPrefix + typeId;

        /// <summary>
        /// Shared ordering for a base's sub-types: untagged variants first (so a plain "Standard" beats a
        /// "Nailed"/"locked" one), then alphabetically. Used for the palette, the default selection, and
        /// the bindings dropdown so all three agree.
        /// </summary>
        public static int CompareSubTypes(EntityDefinitionSO a, EntityDefinitionSO b)
        {
            bool aTagged = a.Tags.Length > 0;
            bool bTagged = b.Tags.Length > 0;
            if (aTagged != bTagged) return aTagged ? 1 : -1;
            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Rescans entity assets, rebuilds the dynamic commands, and announces any change.</summary>
        public static void Refresh()
        {
            if (Rebuild()) Changed?.Invoke();
        }

        /// <summary>(Re)builds the cached command lists. Returns true if the dynamic command set changed.</summary>
        private static bool Rebuild()
        {
            Scan(out var flat, out var groups, out var ungrouped);
            bool changed = _all == null || !SameCommands(_dynamic, flat);
            _dynamic = flat;
            _baseGroups = groups;
            _ungroupedSubs = ungrouped;
            _all = _builtin.Concat(_dynamic).ToList();
            return changed;
        }

        /// <summary>
        /// Scans the project for base + sub-type assets and produces: the flat command list (bases then
        /// subs, for <see cref="All"/>), the base→sub-type groups, and the ungrouped sub-types.
        /// </summary>
        private static void Scan(out List<EditorCommand> flat, out List<BaseGroup> groups,
            out List<EditorCommand> ungrouped)
        {
            var baseTypes = LoadAll<EntityBaseTypeSO>();
            baseTypes.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            // Bucket sub-types under their base id (or the ungrouped bucket), each bucket sorted consistently.
            var subsByBase = new Dictionary<string, List<EntityDefinitionSO>>();
            var ungroupedDefs = new List<EntityDefinitionSO>();
            foreach (var def in LoadAll<EntityDefinitionSO>())
            {
                if (def.BaseType != null)
                {
                    if (!subsByBase.TryGetValue(def.BaseType.BaseId, out var bucket))
                        subsByBase[def.BaseType.BaseId] = bucket = new List<EntityDefinitionSO>();
                    bucket.Add(def);
                }
                else
                {
                    ungroupedDefs.Add(def);
                }
            }

            foreach (var bucket in subsByBase.Values) bucket.Sort(CompareSubTypes);
            ungroupedDefs.Sort(CompareSubTypes);

            flat = new List<EditorCommand>();
            groups = new List<BaseGroup>();
            foreach (var b in baseTypes)
            {
                var baseCmd = new EditorCommand(BaseCommandId(b.BaseId), "Base Types", b.DisplayName, KeyCombo.None);
                var subCmds = new List<EditorCommand>();
                if (subsByBase.TryGetValue(b.BaseId, out var defs))
                    foreach (var def in defs)
                        subCmds.Add(SubCommand(def));

                groups.Add(new BaseGroup(baseCmd, subCmds));
                flat.Add(baseCmd);
                flat.AddRange(subCmds);
            }

            ungrouped = new List<EditorCommand>();
            foreach (var def in ungroupedDefs)
            {
                var cmd = SubCommand(def);
                ungrouped.Add(cmd);
                flat.Add(cmd);
            }
        }

        // A sub-type row is shown nested under its base, so its label is just the sub-type's own name.
        private static EditorCommand SubCommand(EntityDefinitionSO def) =>
            new(EntityCommandId(def.TypeId), "Place Entities", def.DisplayName, KeyCombo.None);

        private static List<T> LoadAll<T>() where T : ScriptableObject
        {
            var list = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) list.Add(asset);
            }
            return list;
        }

        private static bool SameCommands(List<EditorCommand> a, List<EditorCommand> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Id != b[i].Id || a[i].DisplayName != b[i].DisplayName)
                    return false;
            return true;
        }

        public static EditorCommand Find(string id)
        {
            foreach (var c in All)
                if (c.Id == id)
                    return c;
            return null;
        }
    }
}
