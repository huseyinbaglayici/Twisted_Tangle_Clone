namespace TwistedTangle.Editor.Utils
{
    // USS class name constants shared across editor windows.
    // A mismatch between these and LevelCreator.uss is a silent failure at runtime;
    // centralising the strings here makes USS refactoring safe and grep-friendly.
    internal static class Css
    {
        // ── Structural ────────────────────────────────────────────────────────
        public const string Root          = "tt-root";
        public const string MainScroll    = "tt-main-scroll";
        public const string RightScroll   = "tt-right-scroll";
        public const string Title         = "tt-title";
        public const string Section       = "tt-section";
        public const string SectionHeader = "tt-section__header";
        public const string Hint          = "tt-hint";
        public const string Row           = "tt-row";
        public const string RowWrap       = "tt-row--wrap";
        public const string Subgroup      = "tt-subgroup";
        public const string Subname       = "tt-subname";
        public const string Metric        = "tt-metric";

        // ── Buttons ───────────────────────────────────────────────────────────
        public const string Btn        = "tt-btn";
        public const string BtnPrimary = "tt-btn--primary";
        public const string BtnSave    = "tt-btn--save";
        public const string BtnDanger  = "tt-btn--danger";
        public const string Num        = "tt-num";

        // ── Tool buttons ──────────────────────────────────────────────────────
        public const string Tool       = "tt-tool";
        public const string ToolActive = "tt-tool--active";

        // ── Validation / status ───────────────────────────────────────────────
        public const string ValidationOk    = "tt-validation__ok";
        public const string ValidationError = "tt-validation__error";
        public const string ValidationWarn  = "tt-validation__warn";
        public const string StatusDot       = "tt-status-dot";
        public const string StatusDotOk     = "tt-status-dot--ok";
        public const string StatusDotError  = "tt-status-dot--error";
        public const string StatusDotWarn   = "tt-status-dot--warn";

        // ── Difficulty badge ──────────────────────────────────────────────────
        public const string DifficultyBadge = "tt-difficulty-badge";
        // modifier suffix: $"{DifficultyBadge}--{difficulty}" (dynamic — no const possible)

        // ── Metric chips ──────────────────────────────────────────────────────
        public const string MetricChip     = "tt-metric-chip";
        public const string MetricChipWarn = "tt-metric-chip--warn";
        public const string MetricChipVal  = "tt-metric-chip__val";
        public const string MetricChipLbl  = "tt-metric-chip__lbl";

        // ── Rope list rows ────────────────────────────────────────────────────
        public const string RopeRow                = "tt-rope-row";
        public const string RopeRowSelected        = "tt-rope-row--selected";
        public const string RopeRowLeft            = "tt-rope-row__left";
        public const string RopeRowHandle          = "tt-rope-row__handle";
        public const string RopeRowSwatch          = "tt-rope-row__swatch";
        public const string RopeRowSwatchPaintable = "tt-rope-row__swatch--paintable";
        public const string RopeRowInfo            = "tt-rope-row__info";
        public const string RopeRowName            = "tt-rope-row__name";
        public const string RopeRowMeta            = "tt-rope-row__meta";
        public const string RopeRowBadge           = "tt-rope-row__badge";
        public const string RopeRowActions         = "tt-rope-row__actions";
        public const string RopeRowIconBtn         = "tt-rope-row__icon-btn";
        public const string RopeRowIconBtnDanger   = "tt-rope-row__icon-btn--danger";

        // ── Palette / swatches ────────────────────────────────────────────────
        public const string SwatchGrid              = "tt-swatch-grid";
        public const string Swatch                  = "tt-swatch";
        public const string SwatchSelected          = "tt-swatch--selected";
        public const string SwatchFilterBtn         = "tt-swatch-filter-btn";
        public const string PalettePickerLabel      = "tt-palette-picker__label";
        public const string PaletteSelectorCompact  = "tt-palette-selector--compact";

        // ── LevelCreator layout ───────────────────────────────────────────────
        public const string AppContainer         = "tt-app-container";
        public const string Body                 = "tt-body";
        public const string SetupBar             = "tt-setup-bar";
        public const string Topbar               = "tt-topbar";
        public const string TopbarSep            = "tt-topbar__sep";
        public const string LevelPropsBar        = "tt-level-props-bar";
        public const string LevelPropsBarLabel   = "tt-level-props-bar__label";
        public const string LevelPropsBarField   = "tt-level-props-bar__field";
        public const string CanvasPanel          = "tt-canvas-panel";
        public const string CanvasHost           = "tt-canvas-host";
        public const string Canvas               = "tt-canvas";
        public const string CanvasBottomHalf     = "tt-canvas-bottom__half";
        public const string CanvasBottomHandle   = "tt-canvas-bottom__handle";
        public const string CanvasBottomScroll   = "tt-canvas-bottom__scroll";
        public const string RightPanel           = "tt-right-panel";
        public const string RightDivider         = "tt-right-divider";
        public const string RopeBar              = "tt-rope-bar";
        public const string RopeBarScroll        = "tt-rope-bar__scroll";

        // ── AI Generator window ───────────────────────────────────────────────
        public const string AiGroupHeader    = "tt-ai-group-header";
        public const string AiEntityRow      = "tt-ai-entity-row";
        public const string AiEntityRequired = "tt-ai-entity-required";
    }
}
