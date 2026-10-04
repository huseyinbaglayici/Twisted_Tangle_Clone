using Editor.Input;
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
            root.AddToClassList(Css.Root);

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(LevelEditorPaths.Uss);
            if (uss != null) root.styleSheets.Add(uss);

            root.style.backgroundColor = EditorColors.WindowBg;

            var scroll = new ScrollView();
            scroll.AddToClassList(Css.RightScroll);
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft  = 8;
            scroll.style.paddingRight = 8;
            scroll.style.paddingTop   = 8;

            var title = new Label("Twisted Tangle — Key Bindings");
            title.AddToClassList(Css.Title);
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
            }, Css.BtnDanger));
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
                    AddHeader(cmd.Category);
                }
                _rowsHost.Add(BuildRow(cmd));
            }
        }

        private void AddHeader(string text)
        {
            var header = new Label(text);
            header.AddToClassList(Css.SectionHeader);
            header.style.marginTop = 8;
            _rowsHost.Add(header);
        }

        private VisualElement BuildRow(EditorCommand cmd)
        {
            var row = MakeRow();
            bool listening = _listeningId == cmd.Id;
            var combo = KeyBindingStore.Get(cmd.Id);

            // The name flexes (and ellipsises long text) so the fixed-width buttons always stay on one
            // line — otherwise "Default" wraps below on narrow windows / long command names.
            var name = new Label(cmd.DisplayName) { tooltip = cmd.DisplayName };
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
            bindBtn.AddToClassList(Css.Tool);
            bindBtn.style.minWidth = 110;
            bindBtn.style.flexShrink = 0;
            if (listening) bindBtn.AddToClassList(Css.ToolActive);
            row.Add(bindBtn);

            var clear = new Button(() => { KeyBindingStore.Set(cmd.Id, KeyCombo.None); Rebuild(); }) { text = "Clear" };
            clear.AddToClassList(Css.Tool);
            clear.style.flexShrink = 0;
            clear.SetEnabled(!combo.IsEmpty);
            row.Add(clear);

            if (KeyBindingStore.IsOverridden(cmd.Id))
            {
                var reset = new Button(() => { KeyBindingStore.Reset(cmd.Id); Rebuild(); }) { text = "Default" };
                reset.AddToClassList(Css.Tool);
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
                warn.AddToClassList(Css.ValidationWarn);
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
            r.AddToClassList(Css.Row);
            r.AddToClassList(Css.RowWrap);
            return r;
        }

        private static Button MakeButton(string text, System.Action onClick, string ussClass)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList(Css.Btn);
            if (!string.IsNullOrEmpty(ussClass)) b.AddToClassList(ussClass);
            return b;
        }
    }
}
