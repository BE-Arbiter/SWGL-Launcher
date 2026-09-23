using System.Diagnostics;
using System.Net;
using Markdig;

namespace SWGLLauncher
{
    /// <summary>
    /// Fenetre des notes de version : le Markdown du canal, converti en HTML et affiche
    /// dans un navigateur integre, au style du launcher. Non modale : le launcher reste
    /// utilisable pendant la lecture.
    /// </summary>
    internal sealed class PatchNotesForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        private static readonly Color PageBackColor = Color.FromArgb(18, 18, 22);

        // HTML brut desactive : les notes viennent du serveur, aucun script ne doit passer.
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

        private readonly Color _accent;
        private readonly Rectangle _titleBounds;
        private readonly Font _titleFont = new("Segoe UI Semibold", 11f);
        private readonly WebBrowser _browser;

        public PatchNotesForm(Color accent, float scale)
        {
            _accent = accent;

            int Scaled(int amount) => (int)Math.Round(amount * scale);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = PageBackColor;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Scaled(640), Scaled(560));
            Text = "Patch notes";

            // Le titre est dessine sur la fenetre : le faire glisser la deplace.
            _titleBounds = new Rectangle(
                Scaled(24), Scaled(18), ClientSize.Width - Scaled(48), Scaled(26));

            int bottom = ClientSize.Height - Scaled(58);

            _browser = new WebBrowser
            {
                Location = new Point(Scaled(24), Scaled(54)),
                Size = new Size(ClientSize.Width - Scaled(48), bottom - Scaled(54)),
                AllowWebBrowserDrop = false,
                IsWebBrowserContextMenuEnabled = false,
                ScriptErrorsSuppressed = true,
                WebBrowserShortcutsEnabled = false,
            };

            // Un lien s'ouvre dans le navigateur par defaut, jamais dans la fenetre.
            _browser.Navigating += Browser_Navigating;
            _browser.NewWindow += (_, e) => e.Cancel = true;
            _browser.PreviewKeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            var close = new FlatButton
            {
                Text = "Close",
                AccentColor = accent,
                Primary = true,
                Size = new Size(Scaled(84), Scaled(28)),
                Location = new Point(ClientSize.Width - Scaled(24) - Scaled(84), bottom + Scaled(16)),
            };

            close.Click += (_, _) => Close();

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                }
            };

            Controls.AddRange([_browser, close]);
        }

        /// <summary>Affiche des notes de version ; appelable a nouveau pour les remplacer.</summary>
        public void ShowNotes(string title, string markdown)
        {
            Text = title;
            Invalidate(_titleBounds);
            _browser.DocumentText = BuildPage(title, Markdown.ToHtml(markdown, Pipeline));
        }

        private string BuildPage(string title, string body)
        {
            string accent = ColorTranslator.ToHtml(_accent);
            string back = ColorTranslator.ToHtml(PageBackColor);

            // Le controle WebBrowser est le moteur d'Internet Explorer : sans la balise
            // X-UA-Compatible, il rendrait la page en mode IE7.
            return $$"""
                <!DOCTYPE html>
                <html>
                <head>
                <meta charset="utf-8">
                <meta http-equiv="X-UA-Compatible" content="IE=edge">
                <title>{{WebUtility.HtmlEncode(title)}}</title>
                <style>
                  html, body { margin: 0; padding: 0; background: {{back}}; }
                  html { scrollbar-base-color: #2c2c33; scrollbar-face-color: #3a3a42;
                         scrollbar-track-color: {{back}}; scrollbar-arrow-color: #9aa0a6; }
                  body { color: #d2d7dc; font: 10pt "Segoe UI", sans-serif; line-height: 1.5;
                         padding: 0 12px 12px 0; }
                  h1, h2, h3, h4 { color: {{accent}}; font-weight: 600; margin: 1.2em 0 .4em; }
                  h1 { font-size: 1.5em; } h2 { font-size: 1.3em; } h3 { font-size: 1.1em; }
                  body > :first-child { margin-top: 0; }
                  a { color: {{accent}}; }
                  code { background: #26262c; padding: 1px 4px; font-family: Consolas, monospace; }
                  pre { background: #26262c; padding: 8px 10px; overflow: auto; }
                  pre code { padding: 0; }
                  blockquote { margin: 0; padding-left: 12px; border-left: 3px solid {{accent}}; color: #aab0b6; }
                  hr { border: 0; border-top: 1px solid #3a3a42; }
                  table { border-collapse: collapse; }
                  th, td { border: 1px solid #3a3a42; padding: 4px 8px; }
                  img { max-width: 100%; }
                </style>
                </head>
                <body>
                {{body}}
                </body>
                </html>
                """;
        }

        private void Browser_Navigating(object? sender, WebBrowserNavigatingEventArgs e)
        {
            // La page elle-meme se charge sur about:blank ; tout le reste est un lien.
            if (e.Url is null || e.Url.Scheme == "about")
            {
                return;
            }

            e.Cancel = true;

            if (e.Url.Scheme is "http" or "https")
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Url.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // Pas de navigateur par defaut : le lien reste simplement sans effet.
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            TextRenderer.DrawText(
                e.Graphics, Text, _titleFont, _titleBounds, _accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Filet d'accent autour de la fenetre, puisqu'elle n'a pas de bordure systeme.
            using var pen = new Pen(Color.FromArgb(120, _accent));
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && m.Result == HTCLIENT)
            {
                m.Result = HTCAPTION;
            }
        }
    }
}
