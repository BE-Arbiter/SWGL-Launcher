namespace SWGLLauncher
{
    /// <summary>
    /// Habillage sombre des menus deroulants, au ton de l'accent du launcher.
    /// </summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer(Color accent)
            : base(new DarkMenuColorTable(accent))
        {
            Accent = accent;
        }

        public Color Accent { get; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.FromArgb(16, 16, 16) : Accent;
            base.OnRenderItemText(e);
        }

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            private static readonly Color Background = Color.FromArgb(22, 22, 26);
            private readonly Color _accent;

            public DarkMenuColorTable(Color accent)
            {
                _accent = accent;
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground => Background;
            public override Color ImageMarginGradientBegin => Background;
            public override Color ImageMarginGradientMiddle => Background;
            public override Color ImageMarginGradientEnd => Background;
            public override Color MenuBorder => _accent;
            public override Color MenuItemBorder => _accent;
            public override Color MenuItemSelected => _accent;
            public override Color MenuItemSelectedGradientBegin => _accent;
            public override Color MenuItemSelectedGradientEnd => _accent;
            public override Color MenuItemPressedGradientBegin => Background;
            public override Color MenuItemPressedGradientMiddle => Background;
            public override Color MenuItemPressedGradientEnd => Background;
            public override Color SeparatorDark => Color.FromArgb(70, 255, 255, 255);
            public override Color SeparatorLight => Color.FromArgb(20, 255, 255, 255);
        }
    }
}
