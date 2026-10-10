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

        /// <summary>Double chevron vers la droite : ouvrir le journal.</summary>
        LogShow,

        /// <summary>Double chevron vers la gauche : refermer le journal.</summary>
        LogHide,

        /// <summary>Lien vers la page du mod sur ModDB.</summary>
        ModDb,

        /// <summary>Lien vers la page du mod sur Nexus Mods.</summary>
        Nexus,

        /// <summary>Lien vers le serveur Discord.</summary>
        Discord,

        /// <summary>Point d'interrogation : ouvre la FAQ des erreurs courantes.</summary>
        Help,
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

        /// <summary>Image a afficher a la place d'un glyphe vectoriel (logos des sites).</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image? Icon { get; set; }

        /// <summary>
        /// Logo rond sans bordure : il est decoupe en disque et cerne d'un anneau noir,
        /// pour s'accorder au logo Nexus qui a deja le sien.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IconRound { get; set; }

        /// <summary>Vrai pour les pastilles (logos, aide) : pas de fond gris au survol.</summary>
        /// <summary>
        /// Epaisseur de l'anneau noir des pastilles rondes, en part du diametre : celle du
        /// contour du logo Nexus (45 px pour 468 px visibles).
        /// </summary>
        private const float RingRatio = 0.096f;

        /// <summary>Marge transparente du logo Nexus (22 px sur 512), en part de sa taille visible.</summary>
        private const float NexusMarginRatio = 0.047f;

        private bool IsBadge =>Icon is not null || Glyph == TitleBarGlyph.Help;

        /// <summary>Couleur du disque du bouton d'aide.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeColor { get; set; } = Color.Gold;

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

            if (_hovered && HoverBackColor.A > 0 && !IsBadge)
            {
                using var brush = new SolidBrush(HoverBackColor);
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (Glyph == TitleBarGlyph.Help)
            {
                DrawHelp(g);
                return;
            }

            if (Icon is not null)
            {
                DrawIcon(g);
                return;
            }

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

                case TitleBarGlyph.LogShow:
                case TitleBarGlyph.LogHide:
                    DrawDoubleChevron(g, pen, cx, cy, half, Glyph == TitleBarGlyph.LogShow);
                    break;

                case TitleBarGlyph.ModDb:
                    DrawBadge(g, pen, color, cx, cy, half * 1.7f, "db");
                    break;

                case TitleBarGlyph.Nexus:
                    DrawBadge(g, pen, color, cx, cy, half * 1.7f, "N");
                    break;

                case TitleBarGlyph.Help:
                    DrawBadge(g, pen, color, cx, cy, half * 1.7f, "?");
                    break;

                case TitleBarGlyph.Discord:
                    DrawDiscord(g, color, cx, cy, half * 1.9f);
                    break;
            }
        }

        /// <summary>
        /// Zone carree de la pastille : 80 % du bouton au repos, 88 % au survol.
        /// La marge laissee au repos accueille l'ombre et l'agrandissement.
        /// </summary>
        private RectangleF BadgeBounds()
        {
            float side = Math.Min(Width, Height) * (_hovered ? 0.88f : 0.8f);
            return new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
        }

        /// <summary>Ombre douce sous la pastille, pour la detacher de l'image de fond.</summary>
        private static void DrawBadgeShadow(Graphics g, RectangleF bounds)
        {
            for (int layer = 3; layer >= 1; layer--)
            {
                RectangleF halo = bounds;
                halo.Inflate(layer * bounds.Width * 0.035f, layer * bounds.Width * 0.035f);
                halo.Offset(0, bounds.Height * 0.03f);

                using var brush = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
                g.FillEllipse(brush, halo);
            }
        }

        /// <summary>Logo d'un site, centre ; decoupe en disque et cerne de noir si rond.</summary>
        private void DrawIcon(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            RectangleF bounds = BadgeBounds();
            DrawBadgeShadow(g, bounds);

            if (!IconRound)
            {
                // Le logo Nexus a une marge transparente (~4,5 % par cote) : on l'agrandit
                // d'autant pour que sa partie visible ait la taille des pastilles rondes.
                RectangleF full = bounds;
                full.Inflate(bounds.Width * NexusMarginRatio, bounds.Height * NexusMarginRatio);
                int fullSide = (int)Math.Round(full.Width);
                g.DrawImage(ScaledIcon(fullSide), full.X, full.Y, fullSide, fullSide);
                return;
            }

            // Anneau noir de la meme epaisseur relative que celui du logo Nexus.
            using (var ring = new SolidBrush(Color.Black))
            {
                g.FillEllipse(ring, bounds);
            }

            RectangleF inner = bounds;
            float ringWidth = bounds.Width * RingRatio;
            inner.Inflate(-ringWidth, -ringWidth);

            // Un SetClip n'est pas anticrenele : le disque est rempli avec l'image comme
            // texture, ce qui donne un bord lisse. L'image est d'abord reduite en haute
            // qualite a la taille du disque, une texture se reechantillonnant mal.
            // Leger debord : les bords de l'image ne laissent pas de liseré clair.
            int side = (int)Math.Ceiling(inner.Width * 1.04f);
            Bitmap scaled = ScaledIcon(side);
            float offset = (side - inner.Width) / 2f;

            using var texture = new TextureBrush(scaled, WrapMode.Clamp);
            texture.TranslateTransform(inner.X - offset, inner.Y - offset);
            g.FillEllipse(texture, inner);
        }

        private Bitmap? _scaledIcon;
        private Image? _scaledSource;

        /// <summary>L'image reduite a la taille demandee, gardee tant qu'elle ne change pas.</summary>
        private Bitmap ScaledIcon(int side)
        {
            if (_scaledIcon is not null && _scaledSource == Icon && _scaledIcon.Width == side)
            {
                return _scaledIcon;
            }

            _scaledIcon?.Dispose();
            _scaledIcon = new Bitmap(side, side);
            _scaledSource = Icon;

            using Graphics g = Graphics.FromImage(_scaledIcon);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(
                Icon!, new Rectangle(0, 0, side, side),
                0, 0, Icon!.Width, Icon.Height, GraphicsUnit.Pixel, attributes);

            return _scaledIcon;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scaledIcon?.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Gros point d'interrogation blanc dans un disque a la couleur du mod, cerne de noir.</summary>
        private void DrawHelp(Graphics g)
        {
            RectangleF bounds = BadgeBounds();
            DrawBadgeShadow(g, bounds);

            using (var ring = new SolidBrush(Color.Black))
            {
                g.FillEllipse(ring, bounds);
            }

            RectangleF disc = bounds;
            float ringWidth = bounds.Width * RingRatio;
            disc.Inflate(-ringWidth, -ringWidth);

            using (var fill = new SolidBrush(_hovered ? Lighten(BadgeColor) : BadgeColor))
            {
                g.FillEllipse(fill, disc);
            }

            // Le "?" est converti en trace puis centre sur sa boite reelle : un DrawString
            // le placerait trop bas, a cause de l'interligne de la police.
            using var mark = new GraphicsPath();
            using (var family = new FontFamily("Segoe UI"))
            {
                mark.AddString(
                    "?", family, (int)FontStyle.Bold, disc.Height * 0.72f,
                    PointF.Empty, StringFormat.GenericTypographic);
            }

            RectangleF glyph = mark.GetBounds();
            using var center = new Matrix();
            center.Translate(
                disc.X + ((disc.Width - glyph.Width) / 2f) - glyph.X,
                disc.Y + ((disc.Height - glyph.Height) / 2f) - glyph.Y);
            mark.Transform(center);

            using (var outline = new Pen(Color.Black, Math.Max(1f, disc.Width * 0.06f))
            {
                LineJoin = LineJoin.Round,
            })
            {
                g.DrawPath(outline, mark);
            }

            g.FillPath(Brushes.White, mark);
        }

        private static Color Lighten(Color color) => Color.FromArgb(
            color.A,
            color.R + ((255 - color.R) / 4),
            color.G + ((255 - color.G) / 4),
            color.B + ((255 - color.B) / 4));

        /// <summary>Pastille circulaire contenant un court texte (ModDB, Nexus, aide).</summary>
        private static void DrawBadge(
            Graphics g, Pen pen, Color color, float cx, float cy, float radius, string text)
        {
            g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);

            using var font = new Font("Segoe UI", radius, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, brush, new PointF(cx, cy + (radius * 0.04f)), format);
        }

        /// <summary>Silhouette stylisee du logo Discord : bulle arrondie et deux yeux.</summary>
        private static void DrawDiscord(Graphics g, Color color, float cx, float cy, float radius)
        {
            float w = radius * 2f;
            float h = radius * 1.5f;

            using var path = new GraphicsPath();
            path.AddArc(cx - (w / 2), cy - (h / 2), w * 0.5f, h * 0.7f, 180, 90);
            path.AddArc(cx, cy - (h / 2), w * 0.5f, h * 0.7f, 270, 90);
            path.AddLine(cx + (w / 2), cy + (h * 0.1f), cx + (w * 0.38f), cy + (h / 2));
            path.AddLine(cx + (w * 0.38f), cy + (h / 2), cx - (w * 0.38f), cy + (h / 2));
            path.AddLine(cx - (w * 0.38f), cy + (h / 2), cx - (w / 2), cy + (h * 0.1f));
            path.CloseFigure();

            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);

            // Les yeux : taches sombres posees sur la forme.
            using var eye = new SolidBrush(Color.FromArgb(230, 14, 14, 18));
            float r = radius * 0.2f;
            g.FillEllipse(eye, cx - (w * 0.22f) - r, cy - (r * 0.6f), r * 2, r * 2.4f);
            g.FillEllipse(eye, cx + (w * 0.22f) - r, cy - (r * 0.6f), r * 2, r * 2.4f);
        }

        /// <summary>Deux chevrons cote a cote, pointant vers la droite ou vers la gauche.</summary>
        private static void DrawDoubleChevron(
            Graphics g, Pen pen, float cx, float cy, float half, bool pointsRight)
        {
            float direction = pointsRight ? 1f : -1f;
            float armX = half * 0.45f;
            float armY = half * 0.9f;

            foreach (float offset in new[] { -half * 0.4f, half * 0.4f })
            {
                float x = cx + offset;

                g.DrawLines(pen,
                [
                    new PointF(x - (direction * armX), cy - armY),
                    new PointF(x + (direction * armX), cy),
                    new PointF(x - (direction * armX), cy + armY),
                ]);
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
