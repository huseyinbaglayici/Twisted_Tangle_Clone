using TwistedTangle.Editor.Input;
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
        private const string UssPath = "Assets/Scripts/Editor/LevelCreator.uss";

        private string _listeningId; // command currently capturing a key press, or null
        private VisualElement _rowsHost;

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

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null) root.styleSheets.Add(uss);

            var scroll = new ScrollView();
            scroll.AddToClassList("tt-main-scroll");

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

            Rebuild();
        }

        private void Rebuild()
        {
            _rowsHost.Clear();

            string lastCategory = null;
            foreach (var cmd in LevelEditorCommands.All)
            {
                if (cmd.Category != lastCategory)
                {
                    lastCategory = cmd.Category;
                    var header = new Label(cmd.Category);
                    header.AddToClassList("tt-section__header");
                    header.style.marginTop = 8;
                    _rowsHost.Add(header);
                }
                _rowsHost.Add(BuildRow(cmd));
            }
        }

        private VisualElement BuildRow(EditorCommand cmd)
        {
            var row = MakeRow();
            bool listening = _listeningId == cmd.Id;
            var combo = KeyBindingStore.Get(cmd.Id);

            var name = new Label(cmd.DisplayName);
            name.style.minWidth = 170;
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
            bindBtn.style.minWidth = 150;
            if (listening) bindBtn.AddToClassList("tt-tool--active");
            row.Add(bindBtn);

            var clear = new Button(() => { KeyBindingStore.Set(cmd.Id, KeyCombo.None); Rebuild(); }) { text = "Clear" };
            clear.AddToClassList("tt-tool");
            clear.SetEnabled(!combo.IsEmpty);
            row.Add(clear);

            if (KeyBindingStore.IsOverridden(cmd.Id))
            {
                var reset = new Button(() => { KeyBindingStore.Reset(cmd.Id); Rebuild(); }) { text = "Default" };
                reset.AddToClassList("tt-tool");
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
