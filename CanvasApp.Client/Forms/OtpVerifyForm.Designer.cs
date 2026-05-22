namespace CanvasApp.Client
{
    partial class OtpVerifyForm
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
            this.btnResend = new Guna.UI2.WinForms.Guna2Button();
            this.btnCancel = new Guna.UI2.WinForms.Guna2Button();
            this.btnOK = new Guna.UI2.WinForms.Guna2Button();
            this.txtCode = new Guna.UI2.WinForms.Guna2TextBox();
            this.lblEmail = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblHint = new System.Windows.Forms.Label();
            this.pnlCard.SuspendLayout();
            this.SuspendLayout();
            //
            // pnlCard
            //
            this.pnlCard.BorderRadius = 20;
            this.pnlCard.Controls.Add(this.btnResend);
            this.pnlCard.Controls.Add(this.btnCancel);
            this.pnlCard.Controls.Add(this.btnOK);
            this.pnlCard.Controls.Add(this.txtCode);
            this.pnlCard.Controls.Add(this.lblHint);
            this.pnlCard.Controls.Add(this.lblEmail);
            this.pnlCard.Controls.Add(this.lblTitle);
            this.pnlCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlCard.FillColor = System.Drawing.Color.White;
            this.pnlCard.Location = new System.Drawing.Point(0, 0);
            this.pnlCard.Name = "pnlCard";
            this.pnlCard.Size = new System.Drawing.Size(480, 360);
            this.pnlCard.TabIndex = 0;
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 16.2F);
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.lblTitle.Location = new System.Drawing.Point(40, 32);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(280, 32);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Xác thực email";
            //
            // lblEmail
            //
            this.lblEmail.AutoSize = true;
            this.lblEmail.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F);
            this.lblEmail.ForeColor = System.Drawing.Color.FromArgb(75, 85, 99);
            this.lblEmail.Location = new System.Drawing.Point(42, 80);
            this.lblEmail.Name = "lblEmail";
            this.lblEmail.Size = new System.Drawing.Size(200, 19);
            this.lblEmail.TabIndex = 1;
            this.lblEmail.Text = "Mã đã gửi tới:";
            //
            // lblHint
            //
            this.lblHint.AutoSize = true;
            this.lblHint.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F);
            this.lblHint.ForeColor = System.Drawing.Color.FromArgb(107, 114, 128);
            this.lblHint.Location = new System.Drawing.Point(42, 110);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(360, 18);
            this.lblHint.TabIndex = 2;
            this.lblHint.Text = "Nhập mã 6 chữ số. Mã có hiệu lực trong 5 phút.";
            //
            // txtCode
            //
            this.txtCode.BorderRadius = 10;
            this.txtCode.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtCode.DefaultText = "";
            this.txtCode.FocusedState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtCode.Font = new System.Drawing.Font("Consolas", 18F, System.Drawing.FontStyle.Bold);
            this.txtCode.HoverState.BorderColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.txtCode.Location = new System.Drawing.Point(46, 150);
            this.txtCode.MaxLength = 6;
            this.txtCode.Name = "txtCode";
            this.txtCode.PlaceholderText = "______";
            this.txtCode.Size = new System.Drawing.Size(388, 60);
            this.txtCode.TabIndex = 3;
            this.txtCode.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            //
            // btnOK
            //
            this.btnOK.BorderRadius = 20;
            this.btnOK.FillColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.btnOK.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.btnOK.ForeColor = System.Drawing.Color.White;
            this.btnOK.Location = new System.Drawing.Point(46, 234);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(184, 50);
            this.btnOK.TabIndex = 4;
            this.btnOK.Text = "Xác nhận";
            //
            // btnCancel
            //
            this.btnCancel.BorderRadius = 20;
            this.btnCancel.BorderColor = System.Drawing.Color.FromArgb(209, 213, 219);
            this.btnCancel.BorderThickness = 1;
            this.btnCancel.FillColor = System.Drawing.Color.White;
            this.btnCancel.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F);
            this.btnCancel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.btnCancel.Location = new System.Drawing.Point(250, 234);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(184, 50);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Huỷ";
            //
            // btnResend
            //
            this.btnResend.BorderRadius = 16;
            this.btnResend.FillColor = System.Drawing.Color.Transparent;
            this.btnResend.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Underline);
            this.btnResend.ForeColor = System.Drawing.Color.FromArgb(120, 86, 207);
            this.btnResend.Location = new System.Drawing.Point(155, 300);
            this.btnResend.Name = "btnResend";
            this.btnResend.Size = new System.Drawing.Size(170, 36);
            this.btnResend.TabIndex = 6;
            this.btnResend.Text = "Gửi lại mã";
            //
            // OtpVerifyForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(480, 360);
            this.Controls.Add(this.pnlCard);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OtpVerifyForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Xác thực email";
            this.pnlCard.ResumeLayout(false);
            this.pnlCard.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private Guna.UI2.WinForms.Guna2Panel pnlCard;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblEmail;
        private System.Windows.Forms.Label lblHint;
        private Guna.UI2.WinForms.Guna2TextBox txtCode;
        private Guna.UI2.WinForms.Guna2Button btnOK;
        private Guna.UI2.WinForms.Guna2Button btnCancel;
        private Guna.UI2.WinForms.Guna2Button btnResend;
    }
}
