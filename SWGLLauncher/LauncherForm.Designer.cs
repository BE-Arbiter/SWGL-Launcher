namespace SWGLLauncher
{
    partial class LauncherForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnMusic = new TitleBarButton();
            btnMinimize = new TitleBarButton();
            btnClose = new TitleBarButton();
            btnOptions = new FlatButton();
            btnUpdate = new FlatButton();
            btnStart = new FlatButton();
            btnLog = new TitleBarButton();
            pnlLog = new Panel();
            txtLog = new TextBox();
            pnlLog.SuspendLayout();
            SuspendLayout();
            //
            // btnLog
            //
            btnLog.Glyph = TitleBarGlyph.LogShow;
            btnLog.Name = "btnLog";
            btnLog.Size = new Size(34, 34);
            btnLog.TabIndex = 6;
            btnLog.Click += BtnLog_Click;
            //
            // pnlLog
            //
            pnlLog.BackColor = Color.FromArgb(16, 16, 20);
            pnlLog.Controls.Add(txtLog);
            pnlLog.Name = "pnlLog";
            pnlLog.Padding = new Padding(10, 8, 4, 8);
            pnlLog.Size = new Size(420, 386);
            pnlLog.TabIndex = 7;
            pnlLog.Visible = false;
            pnlLog.Paint += PnlLog_Paint;
            //
            // txtLog
            //
            txtLog.BackColor = Color.FromArgb(16, 16, 20);
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Dock = DockStyle.Fill;
            txtLog.Font = new Font("Consolas", 8.5F);
            txtLog.ForeColor = Color.FromArgb(205, 210, 215);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.TabStop = false;
            //
            // btnMusic
            //
            btnMusic.Glyph = TitleBarGlyph.SoundOn;
            btnMusic.Name = "btnMusic";
            btnMusic.Size = new Size(34, 34);
            btnMusic.TabIndex = 3;
            btnMusic.Click += BtnMusic_Click;
            //
            // btnMinimize
            //
            btnMinimize.Glyph = TitleBarGlyph.Minimize;
            btnMinimize.Name = "btnMinimize";
            btnMinimize.Size = new Size(34, 34);
            btnMinimize.TabIndex = 4;
            btnMinimize.Click += BtnMinimize_Click;
            //
            // btnClose
            //
            btnClose.Glyph = TitleBarGlyph.Close;
            btnClose.Name = "btnClose";
            btnClose.Size = new Size(34, 34);
            btnClose.TabIndex = 5;
            btnClose.Click += BtnClose_Click;
            //
            // btnOptions
            //
            btnOptions.Name = "btnOptions";
            btnOptions.ShowDropDown = true;
            btnOptions.Size = new Size(140, 28);
            btnOptions.TabIndex = 0;
            btnOptions.Text = "Options";
            btnOptions.Click += BtnOptions_Click;
            btnOptions.DropDownClick += BtnOptions_Click;
            //
            // btnUpdate
            //
            btnUpdate.Name = "btnUpdate";
            btnUpdate.ShowDropDown = true;
            btnUpdate.Size = new Size(170, 28);
            btnUpdate.TabIndex = 1;
            btnUpdate.Text = "Update";
            btnUpdate.Click += BtnUpdate_Click;
            btnUpdate.DropDownClick += BtnUpdateDropDown_Click;
            //
            // btnStart
            //
            btnStart.Name = "btnStart";
            btnStart.Primary = true;
            btnStart.Size = new Size(130, 28);
            btnStart.TabIndex = 2;
            btnStart.Text = "Start";
            btnStart.Click += BtnStart_Click;
            //
            // LauncherForm
            //
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            BackgroundImageLayout = ImageLayout.Stretch;
            ClientSize = new Size(960, 540);
            Controls.Add(pnlLog);
            Controls.Add(btnLog);
            Controls.Add(btnOptions);
            Controls.Add(btnUpdate);
            Controls.Add(btnStart);
            Controls.Add(btnMusic);
            Controls.Add(btnMinimize);
            Controls.Add(btnClose);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = true;
            Name = "LauncherForm";
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "SWGL Launcher";
            pnlLog.ResumeLayout(false);
            pnlLog.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TitleBarButton btnMusic;
        private TitleBarButton btnMinimize;
        private TitleBarButton btnClose;
        private FlatButton btnOptions;
        private FlatButton btnUpdate;
        private FlatButton btnStart;
        private TitleBarButton btnLog;
        private Panel pnlLog;
        private TextBox txtLog;
    }
}
