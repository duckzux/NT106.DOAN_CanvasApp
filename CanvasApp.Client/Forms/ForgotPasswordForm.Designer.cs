namespace CanvasApp.Client
{
    partial class ForgotPasswordForm
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
            this.pnlCard = new Guna.UI2.WinForms.Guna2Panel();
            this.btnReset = new Guna.UI2.WinForms.Guna2Button();
            this.btnSendOtp = new Guna.UI2.WinForms.Guna2Button();
            this.btnCancel = new Guna.UI2.WinForms.Guna2Button();
            this.txtNewPassword2 = new Guna.UI2.WinForms.Guna2TextBox();
            this.txtNewPassword = new Guna.UI2.WinForms.Guna2TextBox();
            this.txtCode = new Guna.UI2.WinForms.Guna2TextBox();
            this.txtEmail = new Guna.UI2.WinForms.Guna2TextBox();
            this.lblEmail = new System.Windows.Forms.Label();
            this.lblCode = new System.Windows.Forms.Label();
            this.lblNewPw = new System.Windows.Forms.Label();
            this.lblNewPw2 = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblHint = new System.Windows.Forms.Label();
            this.pnlCard.SuspendLayout();
            this.SuspendLayout();
            //
            // pnlCard
            //
            this.pnlCard.BorderRadius = 20;
            this.pnlCard.Controls.Add(this.btnCancel);
            this.pnlCard.Controls.Add(this.btnReset);
            this.pnlCard.Controls.Add(this.btnSendOtp);
            this.pnlCard.Controls.Add(this.txtNewPassword2);
            this.pnlCard.Controls.Add(this.txtNewPassword);
            this.pnlCard.Controls.Add(this.txtCode);
            this.pnlCard.Controls.Add(this.txtEmail);
            this.pnlCard.Controls.Add(this.lblNewPw2);
            this.pnlCard.Controls.Add(this.lblNewPw);
            this.pnlCard.Controls.Add(this.lblCode);
            this.pnlCard.Controls.Add(this.lblEmail);
            this.pnlCard.Controls.Add(this.lblHint);
            this.pnlCard.Controls.Add(this.lblTitle);
            this.pnlCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlCard.FillColor = System.Drawing.Color.White;
            this.pnlCard.Location = new System.Drawing.Point(0, 0);
            this.pnlCard.Name = "pnlCard";
            this.pnlCard.Size = new System.Drawing.Size(520, 560);
            this.pnlCard.TabIndex = 0;
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 16.2F);
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.lblTitle.Location = new System.Drawing.Point(40, 28);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(280, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Quên mật khẩu";
            //
            // lblHint
            //
            this.lblHint.AutoSize = true;
            this.lblHint.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F);
            this.lblHint.ForeColor = System.Drawing.Color.FromArgb(107, 114, 128);
            this.lblHint.Location = new System.Drawing.Point(42, 70);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(420, 18);
            this.lblHint.TabIndex = 1;
            this.lblHint.Text = "Nhập email đã đăng ký để nhận mã xác thực đặt lại mật khẩu.";
            //
            // lblEmail
            //
            this.lblEmail.AutoSize = true;
            this.lblEmail.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F);
            this.lblEmail.ForeColor = System.Drawing.Color.FromArgb(75, 85, 99);
            this.lblEmail.Location = new System.Drawing.Point(42, 105);
            this.lblEmail.Name = "lblEmail";
            this.lblEmail.Size = new System.Drawing.Size(40, 19);
            this.lblEmail.TabIndex = 2;
            this.lblEmail.Text = "Email";
            //
            // txtEmail
            //
            this.txtEmail.BorderRadius = 10;
            this.txtEmail.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtEmail.DefaultText = "";
            this.txtEmail.FocusedState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtEmail.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.txtEmail.HoverState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtEmail.Location = new System.Drawing.Point(46, 128);
            this.txtEmail.Name = "txtEmail";
            this.txtEmail.PlaceholderText = "you@example.com";
            this.txtEmail.Size = new System.Drawing.Size(428, 44);
            this.txtEmail.TabIndex = 3;
            //
            // btnSendOtp
            //
            this.btnSendOtp.BorderRadius = 16;
            this.btnSendOtp.FillColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.btnSendOtp.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.btnSendOtp.ForeColor = System.Drawing.Color.White;
            this.btnSendOtp.Location = new System.Drawing.Point(46, 180);
            this.btnSendOtp.Name = "btnSendOtp";
            this.btnSendOtp.Size = new System.Drawing.Size(428, 44);
            this.btnSendOtp.TabIndex = 4;
            this.btnSendOtp.Text = "Gửi mã xác thực";
            //
            // lblCode
            //
            this.lblCode.AutoSize = true;
            this.lblCode.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F);
            this.lblCode.ForeColor = System.Drawing.Color.FromArgb(75, 85, 99);
            this.lblCode.Location = new System.Drawing.Point(42, 240);
            this.lblCode.Name = "lblCode";
            this.lblCode.Size = new System.Drawing.Size(140, 19);
            this.lblCode.TabIndex = 5;
            this.lblCode.Text = "Mã xác thực (6 chữ số)";
            //
            // txtCode
            //
            this.txtCode.BorderRadius = 10;
            this.txtCode.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtCode.DefaultText = "";
            this.txtCode.Enabled = false;
            this.txtCode.FocusedState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtCode.Font = new System.Drawing.Font("Consolas", 14F, System.Drawing.FontStyle.Bold);
            this.txtCode.HoverState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtCode.Location = new System.Drawing.Point(46, 264);
            this.txtCode.MaxLength = 6;
            this.txtCode.Name = "txtCode";
            this.txtCode.PlaceholderText = "______";
            this.txtCode.Size = new System.Drawing.Size(428, 48);
            this.txtCode.TabIndex = 6;
            this.txtCode.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            //
            // lblNewPw
            //
            this.lblNewPw.AutoSize = true;
            this.lblNewPw.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F);
            this.lblNewPw.ForeColor = System.Drawing.Color.FromArgb(75, 85, 99);
            this.lblNewPw.Location = new System.Drawing.Point(42, 322);
            this.lblNewPw.Name = "lblNewPw";
            this.lblNewPw.Size = new System.Drawing.Size(105, 19);
            this.lblNewPw.TabIndex = 7;
            this.lblNewPw.Text = "Mật khẩu mới";
            //
            // txtNewPassword
            //
            this.txtNewPassword.BorderRadius = 10;
            this.txtNewPassword.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtNewPassword.DefaultText = "";
            this.txtNewPassword.Enabled = false;
            this.txtNewPassword.FocusedState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtNewPassword.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.txtNewPassword.HoverState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtNewPassword.Location = new System.Drawing.Point(46, 346);
            this.txtNewPassword.Name = "txtNewPassword";
            this.txtNewPassword.PasswordChar = '●';
            this.txtNewPassword.PlaceholderText = "Mật khẩu mới (>= 6 ký tự)";
            this.txtNewPassword.Size = new System.Drawing.Size(428, 44);
            this.txtNewPassword.TabIndex = 8;
            this.txtNewPassword.UseSystemPasswordChar = true;
            //
            // lblNewPw2
            //
            this.lblNewPw2.AutoSize = true;
            this.lblNewPw2.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F);
            this.lblNewPw2.ForeColor = System.Drawing.Color.FromArgb(75, 85, 99);
            this.lblNewPw2.Location = new System.Drawing.Point(42, 400);
            this.lblNewPw2.Name = "lblNewPw2";
            this.lblNewPw2.Size = new System.Drawing.Size(150, 19);
            this.lblNewPw2.TabIndex = 9;
            this.lblNewPw2.Text = "Nhập lại mật khẩu mới";
            //
            // txtNewPassword2
            //
            this.txtNewPassword2.BorderRadius = 10;
            this.txtNewPassword2.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtNewPassword2.DefaultText = "";
            this.txtNewPassword2.Enabled = false;
            this.txtNewPassword2.FocusedState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtNewPassword2.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.txtNewPassword2.HoverState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtNewPassword2.Location = new System.Drawing.Point(46, 424);
            this.txtNewPassword2.Name = "txtNewPassword2";
            this.txtNewPassword2.PasswordChar = '●';
            this.txtNewPassword2.PlaceholderText = "Nhập lại mật khẩu mới";
            this.txtNewPassword2.Size = new System.Drawing.Size(428, 44);
            this.txtNewPassword2.TabIndex = 10;
            this.txtNewPassword2.UseSystemPasswordChar = true;
            //
            // btnReset
            //
            this.btnReset.BorderRadius = 20;
            this.btnReset.Enabled = false;
            this.btnReset.FillColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.btnReset.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.btnReset.ForeColor = System.Drawing.Color.White;
            this.btnReset.Location = new System.Drawing.Point(46, 484);
            this.btnReset.Name = "btnReset";
            this.btnReset.Size = new System.Drawing.Size(240, 48);
            this.btnReset.TabIndex = 11;
            this.btnReset.Text = "Đặt lại mật khẩu";
            //
            // btnCancel
            //
            this.btnCancel.BorderRadius = 20;
            this.btnCancel.BorderColor = System.Drawing.Color.FromArgb(209, 213, 219);
            this.btnCancel.BorderThickness = 1;
            this.btnCancel.FillColor = System.Drawing.Color.White;
            this.btnCancel.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.btnCancel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.btnCancel.Location = new System.Drawing.Point(306, 484);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(168, 48);
            this.btnCancel.TabIndex = 12;
            this.btnCancel.Text = "Huỷ";
            //
            // ForgotPasswordForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(520, 560);
            this.Controls.Add(this.pnlCard);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ForgotPasswordForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Quên mật khẩu";
            this.pnlCard.ResumeLayout(false);
            this.pnlCard.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private Guna.UI2.WinForms.Guna2Panel pnlCard;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblHint;
        private System.Windows.Forms.Label lblEmail;
        private Guna.UI2.WinForms.Guna2TextBox txtEmail;
        private Guna.UI2.WinForms.Guna2Button btnSendOtp;
        private System.Windows.Forms.Label lblCode;
        private Guna.UI2.WinForms.Guna2TextBox txtCode;
        private System.Windows.Forms.Label lblNewPw;
        private Guna.UI2.WinForms.Guna2TextBox txtNewPassword;
        private System.Windows.Forms.Label lblNewPw2;
        private Guna.UI2.WinForms.Guna2TextBox txtNewPassword2;
        private Guna.UI2.WinForms.Guna2Button btnReset;
        private Guna.UI2.WinForms.Guna2Button btnCancel;
    }
}
