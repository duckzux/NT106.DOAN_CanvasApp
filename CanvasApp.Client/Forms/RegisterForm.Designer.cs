namespace CanvasApp.Client
{
    partial class RegisterForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pnlTopBar = new Guna.UI2.WinForms.Guna2Panel();
            this.ctrlMinimize = new Guna.UI2.WinForms.Guna2ControlBox();
            this.ctrlClose = new Guna.UI2.WinForms.Guna2ControlBox();
            this.guna2DragControl1 = new Guna.UI2.WinForms.Guna2DragControl(this.components);
            this.txtUsername = new CanvasApp.Client.loginTextbox();
            this.label3 = new System.Windows.Forms.Label();
            this.gradientPanel1 = new CanvasApp.Client.GradientPanel();
            this.label2 = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btnReg = new Guna.UI2.WinForms.Guna2Button();
            this.txtPassword = new CanvasApp.Client.loginTextbox();
            this.txtEmail = new CanvasApp.Client.loginTextbox();
            this.pnlTopBar.SuspendLayout();
            this.gradientPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlTopBar
            // 
            this.pnlTopBar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.pnlTopBar.Controls.Add(this.ctrlMinimize);
            this.pnlTopBar.Controls.Add(this.ctrlClose);
            this.pnlTopBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlTopBar.Location = new System.Drawing.Point(0, 0);
            this.pnlTopBar.Name = "pnlTopBar";
            this.pnlTopBar.Size = new System.Drawing.Size(1280, 30);
            this.pnlTopBar.TabIndex = 0;
            // 
            // ctrlMinimize
            // 
            this.ctrlMinimize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ctrlMinimize.ControlBoxType = Guna.UI2.WinForms.Enums.ControlBoxType.MinimizeBox;
            this.ctrlMinimize.FillColor = System.Drawing.Color.Transparent;
            this.ctrlMinimize.IconColor = System.Drawing.Color.White;
            this.ctrlMinimize.Location = new System.Drawing.Point(1190, 0);
            this.ctrlMinimize.Name = "ctrlMinimize";
            this.ctrlMinimize.Size = new System.Drawing.Size(45, 30);
            this.ctrlMinimize.TabIndex = 1;
            // 
            // ctrlClose
            // 
            this.ctrlClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ctrlClose.FillColor = System.Drawing.Color.Transparent;
            this.ctrlClose.HoverState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(17)))), ((int)(((byte)(35)))));
            this.ctrlClose.IconColor = System.Drawing.Color.White;
            this.ctrlClose.Location = new System.Drawing.Point(1235, 0);
            this.ctrlClose.Name = "ctrlClose";
            this.ctrlClose.Size = new System.Drawing.Size(45, 30);
            this.ctrlClose.TabIndex = 0;
            // 
            // guna2DragControl1
            // 
            this.guna2DragControl1.DockIndicatorTransparencyValue = 0.6D;
            this.guna2DragControl1.TargetControl = this.pnlTopBar;
            this.guna2DragControl1.UseTransparentDrag = true;
            // 
            // txtUsername
            // 
            this.txtUsername.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.txtUsername.isPassword = false;
            this.txtUsername.label = "Tài khoản";
            this.txtUsername.Location = new System.Drawing.Point(751, 365);
            this.txtUsername.Name = "txtUsername";
            this.txtUsername.Padding = new System.Windows.Forms.Padding(0, 0, 0, 1);
            this.txtUsername.Size = new System.Drawing.Size(431, 68);
            this.txtUsername.TabIndex = 9;
            this.txtUsername.TextValue = "";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.BackColor = System.Drawing.Color.Transparent;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 16.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.ForeColor = System.Drawing.Color.Black;
            this.label3.Location = new System.Drawing.Point(870, 188);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(128, 32);
            this.label3.TabIndex = 8;
            this.label3.Text = "SIGN UP";
            // 
            // gradientPanel1
            // 
            this.gradientPanel1.BackColor = System.Drawing.Color.White;
            this.gradientPanel1.Controls.Add(this.label2);
            this.gradientPanel1.Controls.Add(this.pictureBox1);
            this.gradientPanel1.Controls.Add(this.label1);
            this.gradientPanel1.gradientBottom = System.Drawing.Color.FromArgb(((int)(((byte)(174)))), ((int)(((byte)(145)))), ((int)(((byte)(255)))));
            this.gradientPanel1.gradientTop = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.gradientPanel1.Location = new System.Drawing.Point(0, 30);
            this.gradientPanel1.Name = "gradientPanel1";
            this.gradientPanel1.Size = new System.Drawing.Size(617, 720);
            this.gradientPanel1.TabIndex = 7;
            // 
            // label2
            // 
            this.label2.BackColor = System.Drawing.Color.Transparent;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.Color.White;
            this.label2.Location = new System.Drawing.Point(74, 461);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(475, 136);
            this.label2.TabIndex = 1;
            this.label2.Text = "Nền tảng bảng trắng thời gian thực cho đội nhóm. Phác thảo ý tưởng, truyền file v" +
    "à cộng tác mọi lúc.";
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.Image = global::CanvasApp.Client.Properties.Resources.whitever;
            this.pictureBox1.Location = new System.Drawing.Point(126, 192);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(336, 252);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 1;
            this.pictureBox1.TabStop = false;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.BackColor = System.Drawing.Color.Transparent;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 19.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(245)))), ((int)(((byte)(255)))));
            this.label1.Location = new System.Drawing.Point(149, 150);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(242, 38);
            this.label1.TabIndex = 0;
            this.label1.Text = "WELCOME TO";
            // 
            // btnReg
            // 
            this.btnReg.BorderRadius = 20;
            this.btnReg.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnReg.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnReg.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnReg.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnReg.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.btnReg.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnReg.ForeColor = System.Drawing.Color.White;
            this.btnReg.Location = new System.Drawing.Point(751, 582);
            this.btnReg.Name = "btnReg";
            this.btnReg.Size = new System.Drawing.Size(431, 54);
            this.btnReg.TabIndex = 11;
            this.btnReg.Text = "Đăng ký";
            this.btnReg.Click += new System.EventHandler(this.btnReg_Click);
            // 
            // txtPassword
            // 
            this.txtPassword.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.txtPassword.isPassword = false;
            this.txtPassword.label = "Mật khẩu";
            this.txtPassword.Location = new System.Drawing.Point(751, 466);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Padding = new System.Windows.Forms.Padding(0, 0, 0, 1);
            this.txtPassword.Size = new System.Drawing.Size(431, 68);
            this.txtPassword.TabIndex = 10;
            this.txtPassword.TextValue = "";
            // 
            // txtEmail
            // 
            this.txtEmail.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.txtEmail.isPassword = false;
            this.txtEmail.label = "Email";
            this.txtEmail.Location = new System.Drawing.Point(751, 273);
            this.txtEmail.Name = "txtEmail";
            this.txtEmail.Padding = new System.Windows.Forms.Padding(0, 0, 0, 1);
            this.txtEmail.Size = new System.Drawing.Size(431, 68);
            this.txtEmail.TabIndex = 14;
            this.txtEmail.TextValue = "";
            // 
            // RegisterForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1280, 750);
            this.Controls.Add(this.pnlTopBar);
            this.Controls.Add(this.txtEmail);
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.gradientPanel1);
            this.Controls.Add(this.btnReg);
            this.Controls.Add(this.txtPassword);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "RegisterForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "RegisterForm";
            this.pnlTopBar.ResumeLayout(false);
            this.gradientPanel1.ResumeLayout(false);
            this.gradientPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Guna.UI2.WinForms.Guna2Panel pnlTopBar;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlMinimize;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlClose;
        private Guna.UI2.WinForms.Guna2DragControl guna2DragControl1;
        private loginTextbox txtUsername;
        private System.Windows.Forms.Label label3;
        private GradientPanel gradientPanel1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label1;
        private Guna.UI2.WinForms.Guna2Button btnReg;
        private loginTextbox txtPassword;
        private loginTextbox txtEmail;
    }
}
