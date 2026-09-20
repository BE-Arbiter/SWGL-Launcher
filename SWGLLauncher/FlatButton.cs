using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SWGLLauncher
{
    /// <summary>
    /// Bouton plat au style du launcher : fond translucide, bordure et texte dores,
    /// inversion des couleurs au survol. Dessine par-dessus l'image de fond de la fenetre.
    /// </summary>
    internal sealed class FlatButton : Control
    {
        /// <summary>Largeur logique de la zone du chevron, en pixels a 96 ppp.</summary>
        private const int DropDownZoneWidth = 24;

        private bool _hovered;
        private bool _pressed;
        private bool _dropDownHovered;
        private bool _suppressClick;

        public FlatButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color AccentColor { get; set; } = Color.FromArgb(232, 185, 35);

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Color.FromArgb(150, 0, 0, 0);

        /// <summary>Couleur du texte quand le bouton est survole (fond plein).</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverTextColor { get; set; } = Color.FromArgb(16, 16, 16);

        /// <summary>
        /// Affiche un chevron a droite du bouton : la zone du chevron declenche
        /// <see cref="DropDownClick"/> au lieu de l'action principale.
        /// </summary>
        [DefaultValue(false)]
        public bool ShowDropDown { get; set; }

        /// <summary>Bouton mis en avant : fond plein en couleur d'accent.</summary>
        [DefaultValue(false)]
        public bool Primary { get; set; }

        /// <summary>Clic sur la zone du chevron.</summary>
        public event EventHandler? DropDownClick;

        /// <summary>Zone reservee au chevron, a l'echelle du ppp courant.</summary>
        private int DropDownWidth => ShowDropDown
            ? (int)Math.Round(DropDownZoneWidth * (DeviceDpi / 96f))
            : 0;

        private bool IsInDropDownZone(Point location) =>
            ShowDropDown && location.X >= Width - DropDownWidth;

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            _dropDownHovered = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool inZone = IsInDropDownZone(e.Location);
            if (inZone != _dropDownHovered)
            {
                _dropDownHovered = inZone;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;

            // MouseUp precede Click : on peut donc annuler l'action principale
            // quand le clic tombe sur le chevron.
            if (Enabled && e.Button == MouseButtons.Left && IsInDropDownZone(e.Location))
            {
                _suppressClick = true;
                DropDownClick?.Invoke(this, EventArgs.Empty);
            }

            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            base.OnClick(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent is LauncherForm launcher)
            {
                launcher.PaintBackgroundSlice(this, e.Graphics);
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = CreateRoundedPath(bounds, Math.Max(2, Height / 6));

            bool active = Enabled && _hovered;
            Color accent = Enabled ? AccentColor : Color.FromArgb(110, AccentColor);

            // Un bouton "Primary" est plein en permanence ; les autres se remplissent au survol.
            Color fill = active || (Primary && Enabled)
                ? (_pressed ? Darken(accent, 0.8f) : accent)
                : (Primary ? Color.FromArgb(60, accent) : FillColor);

            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(accent, 1f))
            {
                g.DrawPath(pen, path);
            }

            bool filled = active || (Primary && Enabled);
            Color textColor = filled
                ? HoverTextColor
                : (Enabled ? accent : Color.FromArgb(110, AccentColor));

            int dropDownWidth = DropDownWidth;

            var textBounds = new Rectangle(
                0, 0, Width - dropDownWidth, Height);

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                textBounds,
                textColor,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis);

            if (dropDownWidth == 0)
            {
                return;
            }

            // Separateur et chevron de la zone deroulante.
            float separatorX = Width - dropDownWidth;

            using (var separator = new Pen(Color.FromArgb(filled ? 90 : 140, textColor)))
            {
                g.DrawLine(separator, separatorX, Height * 0.22f, separatorX, Height * 0.78f);
            }

            float caretCenterX = separatorX + (dropDownWidth / 2f);
            float caretCenterY = Height / 2f;
            float caretHalf = Math.Max(3f, dropDownWidth * 0.18f);

            using var caret = new Pen(
                _dropDownHovered && !filled ? HoverGlyphBoost(accent) : textColor, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            // Chevron vers le haut : les menus du launcher s'ouvrent au-dessus du bouton,
            // la barre etant en bas de la fenetre.
            g.DrawLines(caret,
            [
                new PointF(caretCenterX - caretHalf, caretCenterY + (caretHalf / 2f)),
                new PointF(caretCenterX, caretCenterY - (caretHalf / 2f)),
                new PointF(caretCenterX + caretHalf, caretCenterY + (caretHalf / 2f)),
            ]);
        }

        private static Color HoverGlyphBoost(Color color) => Color.FromArgb(
            255,
            Math.Min(255, color.R + 40),
            Math.Min(255, color.G + 40),
            Math.Min(255, color.B + 40));

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static Color Darken(Color color, float factor) => Color.FromArgb(
            color.A,
            (int)(color.R * factor),
            (int)(color.G * factor),
            (int)(color.B * factor));
    }
}
