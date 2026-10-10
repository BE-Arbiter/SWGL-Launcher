using System.ComponentModel;

namespace SWGLLauncher
{
    /// <summary>
    /// Infobulle au style du launcher : fond sombre, texte clair, filet d'accent.
    /// </summary>
    internal sealed class DarkToolTip : ToolTip
    {
        private static readonly Color BackgroundColor = Color.FromArgb(18, 18, 22);
        private static readonly Color TextColor = Color.FromArgb(225, 228, 232);

        private readonly Font _font = new("Segoe UI", 9f);

        public DarkToolTip()
        {
            OwnerDraw = true;
            InitialDelay = 300;
            ReshowDelay = 100;
            Popup += OnPopup;
            Draw += OnDraw;
        }

        /// <summary>Couleur du filet qui entoure l'infobulle.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = Color.Gold;

        private static Padding TextPadding(Control? control)
        {
            float scale = (control?.DeviceDpi ?? 96) / 96f;
            return new Padding((int)(10 * scale), (int)(5 * scale), (int)(10 * scale), (int)(6 * scale));
        }

        private void OnPopup(object? sender, PopupEventArgs e)
        {
            string text = GetToolTip(e.AssociatedControl) ?? string.Empty;
            Size measured = TextRenderer.MeasureText(text, _font);
            Padding padding = TextPadding(e.AssociatedControl);
            e.ToolTipSize = new Size(
                measured.Width + padding.Horizontal, measured.Height + padding.Vertical);
        }

        private void OnDraw(object? sender, DrawToolTipEventArgs e)
        {
            using (var back = new SolidBrush(BackgroundColor))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            using (var pen = new Pen(Color.FromArgb(160, AccentColor)))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            TextRenderer.DrawText(
                e.Graphics, e.ToolTipText, _font, e.Bounds, TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _font.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
