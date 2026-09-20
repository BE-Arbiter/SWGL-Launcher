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
            SuspendLayout();
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
            ResumeLayout(false);
        }

        #endregion

        private TitleBarButton btnMusic;
        private TitleBarButton btnMinimize;
        private TitleBarButton btnClose;
        private FlatButton btnOptions;
        private FlatButton btnUpdate;
        private FlatButton btnStart;
    }
}
