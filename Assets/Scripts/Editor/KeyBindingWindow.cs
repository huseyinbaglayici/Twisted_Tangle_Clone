using System.Collections.Generic;
using TwistedTangle.Editor.Input;
using TwistedTangle.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TwistedTangle.Editor
{
    /// <summary>
    /// Standalone window for assigning keyboard shortcuts to the Level Creator's actions. It only edits
    /// the <see cref="KeyBindingStore"/> — it doesn't need the Level Creator to be open. Click a binding,
    /// press the desired keys, and any open Level Creator picks up the change immediately.
    /// </summary>
    public class KeyBindingWindow : EditorWindow
    {
        private string _listeningId; // command currently capturing a key press, or null
        private VisualElement _rowsHost;
        private readonly HashSet<string> _expanded = new(); // base-type dropdowns the user has opened

        [MenuItem("TwistedTangle/Level Editor Key Bindings")]
        public static void ShowWindow()
        {
            var w = GetWindow<KeyBindingWindow>();
            w.titleContent = new GUIContent("Tangle Key Bindings");
            w.minSize = new Vector2(440, 480);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("tt-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = new Color(0.102f, 0.102f, 0.102f); // #1A1A1A

            var scroll = new ScrollView();
            scroll.AddToClassList("tt-right-scroll");
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft  = 8;
            scroll.style.paddingRight = 8;
            scroll.style.paddingTop   = 8;

            var title = new Label("Twisted Tangle — Key Bindings");
            title.AddToClassList("tt-title");
            scroll.Add(title);

            scroll.Add(new HelpBox(
                "Click a binding, then press the keys you want (Esc cancels recording). " +
                "Shortcuts fire while the Level Creator window is focused.",
                HelpBoxMessageType.Info));

            _rowsHost = new VisualElement();
            scroll.Add(_rowsHost);

            var footer = MakeRow();
            footer.style.marginTop = 8;
            footer.Add(MakeButton("Reset All to Defaults", () =>
            {
                if (EditorUtility.DisplayDialog("Reset key bindings",
                        "Restore every Level Creator shortcut to its default?", "Reset", "Cancel"))
                {
                    KeyBindingStore.ResetAll();
                    _listeningId = null;
                    Rebuild();
                }
            }, "tt-btn--danger"));
            scroll.Add(footer);

            root.Add(scroll);

            // Capture phase so a recorded key is grabbed before any focused field consumes it.
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            root.focusable = true;

            // Pick up entity types created in the Level Creator, live, while this window stays open.
            LevelEditorCommands.Refresh();
            LevelEditorCommands.Changed -= OnCommandsChanged;
            LevelEditorCommands.Changed += OnCommandsChanged;

            Rebuild();
        }

        private void OnDisable() => LevelEditorCommands.Changed -= OnCommandsChanged;

        // Re-scan when the window regains focus, in case entity assets changed while it was in the back.
        private void OnFocus() => LevelEditorCommands.Refresh();

        private void OnCommandsChanged()
        {
            if (_rowsHost != null) Rebuild();
        }

        private void Rebuild()
        {
            _rowsHost.Clear();

            // Base Types: the Rope tool plus every entity base type. Each base type carries a collapsible
            // dropdown of its sub-types so their shortcuts are set right under their parent (not in a flat list).
            AddHeader("Base Types");
            AddRowFor(LevelEditorCommands.ToolRope);
            foreach (var group in LevelEditorCommands.BaseGroups)
            {
                _rowsHost.Add(BuildRow(group.Base));
                if (group.SubTypes.Count > 0)
                    _rowsHost.Add(BuildSubFoldout(group.Base.Id, "Sub-types", group.SubTypes));
            }
            var ungrouped = LevelEditorCommands.UngroupedSubTypes;
            if (ungrouped.Count > 0)
                _rowsHost.Add(BuildSubFoldout("ungrouped", "Ungrouped sub-types", ungrouped));

            // Tools: the remaining built-in editing tools.
            AddHeader("Tools");
            AddRowFor(LevelEditorCommands.ToolErase);
            AddRowFor(LevelEditorCommands.ToolFlip);

            // The rest of the built-ins (Level, Rope authoring, Validation), grouped by category as before.
            // The whole "Tools" category is handled above (Rope under Base Types; Erase/Flip under Tools).
            string lastCategory = null;
            foreach (var cmd in LevelEditorCommands.Builtin)
            {
                if (cmd.Category == "Tools") continue;
                if (cmd.Category != lastCategory)
                {
                    lastCategory = cmd.Category;
                    AddHeader(cmd.Category);
                }
                _rowsHost.Add(BuildRow(cmd));
            }
        }

        private void AddHeader(string text)
        {
            var header = new Label(text);
            header.AddToClassList("tt-section__header");
            header.style.marginTop = 8;
            _rowsHost.Add(header);
        }

        private void AddRowFor(string commandId)
        {
            var cmd = LevelEditorCommands.Find(commandId);
            if (cmd != null) _rowsHost.Add(BuildRow(cmd));
        }

        /// <summary>
        /// A dropdown holding the binding rows for a base type's sub-types. <paramref name="key"/> tracks its
        /// expanded state so it survives the full rebuild that follows each binding edit.
        /// </summary>
        private VisualElement BuildSubFoldout(string key, string title, IReadOnlyList<EditorCommand> subs)
        {
            var foldout = new Foldout { text = title, value = _expanded.Contains(key) };
            foldout.AddToClassList("tt-subgroup"); // indent + accent so it reads as nested under its base
            // The rows hold only buttons/labels (no bool fields), so every ChangeEvent<bool> here is the
            // foldout's own expand/collapse — safe to track directly.
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) _expanded.Add(key);
                else _expanded.Remove(key);
            });
            foreach (var cmd in subs)
                foldout.Add(BuildRow(cmd, sub: true));
            return foldout;
        }

        private VisualElement BuildRow(EditorCommand cmd, bool sub = false)
        {
            var row = MakeRow();
            bool listening = _listeningId == cmd.Id;
            var combo = KeyBindingStore.Get(cmd.Id);

            // The name flexes (and ellipsises long text) so the fixed-width buttons always stay on one
            // line — otherwise "Default" wraps below on narrow windows / long command names.
            var name = new Label(cmd.DisplayName) { tooltip = cmd.DisplayName };
            if (sub) name.AddToClassList("tt-subname"); // smaller + dimmer for nested sub-types
            name.style.flexGrow = 1;
            name.style.flexShrink = 1;
            name.style.minWidth = 60;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(name);

            var bindBtn = new Button(() =>
            {
                _listeningId = listening ? null : cmd.Id;
                Rebuild();
                rootVisualElement.Focus(); // ensure key events reach the window while recording
            })
            {
                text = listening ? "Press keys…" : combo.ToString()
            };
            bindBtn.AddToClassList("tt-tool");
            bindBtn.style.minWidth = 110;
            bindBtn.style.flexShrink = 0;
            if (listening) bindBtn.AddToClassList("tt-tool--active");
            row.Add(bindBtn);

            var clear = new Button(() => { KeyBindingStore.Set(cmd.Id, KeyCombo.None); Rebuild(); }) { text = "Clear" };
            clear.AddToClassList("tt-tool");
            clear.style.flexShrink = 0;
            clear.SetEnabled(!combo.IsEmpty);
            row.Add(clear);

            if (KeyBindingStore.IsOverridden(cmd.Id))
            {
                var reset = new Button(() => { KeyBindingStore.Reset(cmd.Id); Rebuild(); }) { text = "Default" };
                reset.AddToClassList("tt-tool");
                reset.style.flexShrink = 0;
                row.Add(reset);
            }

            // Surface clashes so two actions don't silently share one shortcut.
            var conflict = KeyBindingStore.FindConflict(combo, cmd.Id);
            if (conflict != null)
            {
                var warn = new Label("⚠ also: " + LevelEditorCommands.Find(conflict)?.DisplayName)
                {
                    tooltip = "This shortcut is bound to more than one action."
                };
                warn.AddToClassList("tt-validation__warn");
                warn.style.marginLeft = 4;
                row.Add(warn);
            }

            return row;
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (_listeningId == null) return;

            // Esc cancels recording without changing the existing binding.
            if (e.keyCode == KeyCode.Escape)
            {
                _listeningId = null;
                e.StopPropagation();
                Rebuild();
                return;
            }

            var combo = KeyCombo.FromEvent(e);
            if (combo.IsEmpty) return; // modifier-only so far — wait for the actual key

            KeyBindingStore.Set(_listeningId, combo);
            _listeningId = null;
            e.StopPropagation();
            Rebuild();
        }

        private static VisualElement MakeRow()
        {
            var r = new VisualElement();
            r.AddToClassList("tt-row");
            r.AddToClassList("tt-row--wrap");
            return r;
        }

        private static Button MakeButton(string text, System.Action onClick, string ussClass)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("tt-btn");
            if (!string.IsNullOrEmpty(ussClass)) b.AddToClassList(ussClass);
            return b;
        }
    }
}
