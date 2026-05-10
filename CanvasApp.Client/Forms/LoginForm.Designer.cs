namespace CanvasApp.Client
{
    partial class LoginForm
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
            this.label3 = new System.Windows.Forms.Label();
            this.btnLogin = new Guna.UI2.WinForms.Guna2Button();
            this.label4 = new System.Windows.Forms.Label();
            this.lbReg = new System.Windows.Forms.LinkLabel();
            this.txtPassword = new loginTextbox();          // FIX: bỏ "CanvasApp."
            this.gradientPanel1 = new GradientPanel();      // FIX: bỏ "CanvasApp."
            this.label2 = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.txtUsername = new loginTextbox();          // FIX: bỏ "CanvasApp."
            this.gradientPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.BackColor = System.Drawing.Color.Transparent;
            this.label3.Font = new System.Drawing.Font("Montserrat", 16.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.ForeColor = System.Drawing.Color.Black;
            this.label3.Location = new System.Drawing.Point(743, 149);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(411, 44);
            this.label3.TabIndex = 1;
            this.label3.Text = "SIGN IN TO YOUR ACCOUNT";
            this.label3.Click += new System.EventHandler(this.label3_Click);
            // 
            // btnLogin
            // 
            this.btnLogin.BorderRadius = 20;
            this.btnLogin.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnLogin.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnLogin.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnLogin.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnLogin.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.btnLogin.Font = new System.Drawing.Font("Montserrat", 10.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnLogin.ForeColor = System.Drawing.Color.White;
            this.btnLogin.Location = new System.Drawing.Point(751, 494);
            this.btnLogin.Name = "btnLogin";
            this.btnLogin.Size = new System.Drawing.Size(431, 54);
            this.btnLogin.TabIndex = 4;
            this.btnLogin.Text = "Đăng nhập";
            this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click_1);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.BackColor = System.Drawing.Color.Transparent;
            this.label4.Font = new System.Drawing.Font("Montserrat", 10.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.ForeColor = System.Drawing.Color.Black;
            this.label4.Location = new System.Drawing.Point(826, 655);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(174, 27);
            this.label4.TabIndex = 5;
            this.label4.Text = "Chưa có tài khoản?";
            this.label4.Click += new System.EventHandler(this.label4_Click);
            // 
            // lbReg
            // 
            this.lbReg.AutoSize = true;
            this.lbReg.Font = new System.Drawing.Font("Montserrat", 10.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbReg.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.lbReg.Location = new System.Drawing.Point(1015, 655);
            this.lbReg.Name = "lbReg";
            this.lbReg.Size = new System.Drawing.Size(82, 27);
            this.lbReg.TabIndex = 6;
            this.lbReg.TabStop = true;
            this.lbReg.Text = "Đăng ký";
            this.lbReg.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.lbReg_LinkClicked);
            // 
            // txtPassword
            // 
            this.txtPassword.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.txtPassword.isPassword = false;
            this.txtPassword.label = "Mật khẩu";
            this.txtPassword.Location = new System.Drawing.Point(751, 375);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Padding = new System.Windows.Forms.Padding(0, 0, 0, 1);
            this.txtPassword.Size = new System.Drawing.Size(431, 68);
            this.txtPassword.TabIndex = 3;
            this.txtPassword.TextValue = "";
            // 
            // gradientPanel1
            // 
            this.gradientPanel1.BackColor = System.Drawing.Color.White;
            this.gradientPanel1.Controls.Add(this.label2);
            this.gradientPanel1.Controls.Add(this.pictureBox1);
            this.gradientPanel1.Controls.Add(this.label1);
            this.gradientPanel1.gradientBottom = System.Drawing.Color.FromArgb(((int)(((byte)(174)))), ((int)(((byte)(145)))), ((int)(((byte)(255)))));
            this.gradientPanel1.gradientTop = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.gradientPanel1.Location = new System.Drawing.Point(0, -1);
            this.gradientPanel1.Name = "gradientPanel1";
            this.gradientPanel1.Size = new System.Drawing.Size(617, 750);
            this.gradientPanel1.TabIndex = 0;
            // 
            // label2
            // 
            this.label2.BackColor = System.Drawing.Color.Transparent;
            this.label2.Font = new System.Drawing.Font("Montserrat", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.Color.White;
            this.label2.Location = new System.Drawing.Point(74, 461);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(488, 136);
            this.label2.TabIndex = 1;
            this.label2.Text = "Nền tảng bảng trắng thời gian thực cho đội nhóm. Phác thảo ý tưởng, truyền file v" +
    "à cộng tác mọi lúc.";
            this.label2.Click += new System.EventHandler(this.label2_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.Image = global::CanvasApp.Client.Properties.Resources.whitever; // FIX: CanvasApp → CanvasApp.Client
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
            this.label1.Font = new System.Drawing.Font("Montserrat", 19.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(245)))), ((int)(((byte)(255)))));
            this.label1.Location = new System.Drawing.Point(149, 150);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(260, 52);
            this.label1.TabIndex = 0;
            this.label1.Text = "WELCOME TO";
            // 
            // txtUsername
            // 
            this.txtUsername.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.txtUsername.isPassword = false;
            this.txtUsername.label = "Tài khoản";
            this.txtUsername.Location = new System.Drawing.Point(751, 246);
            this.txtUsername.Name = "txtUsername";
            this.txtUsername.Padding = new System.Windows.Forms.Padding(0, 0, 0, 1);
            this.txtUsername.Size = new System.Drawing.Size(431, 68);
            this.txtUsername.TabIndex = 2;
            this.txtUsername.TextValue = "";
            // 
            // LoginForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1280, 720);
            this.Controls.Add(this.lbReg);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.btnLogin);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.txtUsername);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.gradientPanel1);
            this.ForeColor = System.Drawing.Color.White;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "LoginForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "LoginForm";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.gradientPanel1.ResumeLayout(false);
            this.gradientPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private GradientPanel gradientPanel1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private loginTextbox txtPassword;
        private Guna.UI2.WinForms.Guna2Button btnLogin;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.LinkLabel lbReg;
        private loginTextbox txtUsername;
    }
}
