using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SWGLLauncher
{
    internal enum TitleBarGlyph
    {
        Minimize,
        Close,
        SoundOn,
        SoundOff,
    }

    /// <summary>
    /// Bouton plat de la barre de titre personnalisee (fenetre sans bordure).
    /// Le glyphe est dessine en vectoriel : aucune dependance a une police d'icones.
    /// </summary>
    internal sealed class TitleBarButton : Control
    {
        private bool _hovered;

        public TitleBarButton()
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
            TabStop = false;
        }

        [DefaultValue(TitleBarGlyph.Close)]
        public TitleBarGlyph Glyph { get; set; } = TitleBarGlyph.Close;

        /// <summary>Couleur du glyphe au repos.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color GlyphColor { get; set; } = Color.Gold;

        /// <summary>Couleur du glyphe au survol.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverGlyphColor { get; set; } = Color.White;

        /// <summary>Fond affiche au survol (peut etre semi-transparent).</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color HoverBackColor { get; set; } = Color.FromArgb(64, 255, 255, 255);

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
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // La fenetre dessine elle-meme la portion d'image situee sous le bouton :
            // c'est plus fiable que la transparence WinForms au-dessus d'une image de fond.
            if (Parent is LauncherForm launcher)
            {
                launcher.PaintBackgroundSlice(this, e.Graphics);
            }
            else
            {
                base.OnPaintBackground(e);
            }

            if (_hovered && HoverBackColor.A > 0)
            {
                using var brush = new SolidBrush(HoverBackColor);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color color = _hovered ? HoverGlyphColor : GlyphColor;
            float thickness = Math.Max(1.4f, Width / 22f);
            using var pen = new Pen(color, thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };

            // Zone carree centree occupant ~36% du bouton.
            float size = Math.Min(Width, Height) * 0.36f;
            float cx = Width / 2f;
            float cy = Height / 2f;
            float half = size / 2f;

            switch (Glyph)
            {
                case TitleBarGlyph.Minimize:
                    g.DrawLine(pen, cx - half, cy + half * 0.6f, cx + half, cy + half * 0.6f);
                    break;

                case TitleBarGlyph.Close:
                    g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                    g.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
                    break;

                case TitleBarGlyph.SoundOn:
                case TitleBarGlyph.SoundOff:
                    DrawSpeaker(g, pen, color, cx, cy, size * 1.5f, Glyph == TitleBarGlyph.SoundOn);
                    break;
            }
        }

        /// <summary>Haut-parleur, avec ondes (son actif) ou croix (son coupe).</summary>
        private static void DrawSpeaker(
            Graphics g, Pen pen, Color color, float cx, float cy, float size, bool soundOn)
        {
            float left = cx - (size / 2f);
            float bodyTop = cy - (size * 0.18f);
            float bodyBottom = cy + (size * 0.18f);
            float coneX = left + (size * 0.42f);

            PointF[] speaker =
            [
                new(left, bodyTop),
                new(left + (size * 0.2f), bodyTop),
                new(coneX, cy - (size * 0.42f)),
                new(coneX, cy + (size * 0.42f)),
                new(left + (size * 0.2f), bodyBottom),
                new(left, bodyBottom),
            ];

            using var brush = new SolidBrush(color);
            g.FillPolygon(brush, speaker);

            if (soundOn)
            {
                for (int wave = 1; wave <= 2; wave++)
                {
                    float radius = size * 0.2f * wave;
                    g.DrawArc(
                        pen,
                        coneX - radius + (size * 0.06f),
                        cy - radius,
                        radius * 2,
                        radius * 2,
                        -55,
                        110);
                }

                return;
            }

            float crossX = coneX + (size * 0.28f);
            float arm = size * 0.16f;
            g.DrawLine(pen, crossX - arm, cy - arm, crossX + arm, cy + arm);
            g.DrawLine(pen, crossX + arm, cy - arm, crossX - arm, cy + arm);
        }
    }
}
