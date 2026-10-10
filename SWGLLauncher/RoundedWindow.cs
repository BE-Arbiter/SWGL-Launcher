using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SWGLLauncher
{
    /// <summary>
    /// Coins arrondis des fenetres sans bordure (notes de version, FAQ, boites de dialogue) :
    /// la fenetre est decoupee, et son filet d'accent suit l'arrondi.
    /// </summary>
    internal static class RoundedWindow
    {
        /// <summary>Rayon des coins avant mise a l'echelle, un peu plus petit que celui du launcher.</summary>
        public const int DesignRadius = 10;

        [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        /// <summary>Decoupe la fenetre selon un rectangle arrondi ; a refaire a chaque redimensionnement.</summary>
        public static void Apply(Form form, int radius)
        {
            form.Region?.Dispose();

            IntPtr handle = CreateRoundRectRgn(0, 0, form.Width + 1, form.Height + 1, radius * 2, radius * 2);

            // Region.FromHrgn copie la region : le handle GDI d'origine doit etre libere.
            form.Region = Region.FromHrgn(handle);
            _ = DeleteObject(handle);
        }

        /// <summary>Filet d'accent le long du bord arrondi de la fenetre.</summary>
        public static void DrawBorder(Graphics g, Form form, Color color, int radius)
        {
            float diameter = radius * 2f;
            var bounds = new RectangleF(0.5f, 0.5f, form.ClientSize.Width - 1.5f, form.ClientSize.Height - 1.5f);

            using var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(color, 1.5f);
            g.DrawPath(pen, path);

            g.SmoothingMode = previous;
        }
    }
}
