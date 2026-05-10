namespace CanvasApp.Client
{
    partial class LobbyForm
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
            this.panel1 = new System.Windows.Forms.Panel();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.btnLogout = new Guna.UI2.WinForms.Guna2Button();
            this.label1 = new System.Windows.Forms.Label();
            this.btnCreateShow = new Guna.UI2.WinForms.Guna2Button();
            this.guna2Panel1 = new Guna.UI2.WinForms.Guna2Panel();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.rtxtMessage = new System.Windows.Forms.RichTextBox();
            this.txtMessage = new Guna.UI2.WinForms.Guna2TextBox();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.guna2Panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.White;
            this.panel1.Controls.Add(this.btnLogout);
            this.panel1.Controls.Add(this.pictureBox1);
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1301, 100);
            this.panel1.TabIndex = 0;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.Image = global::CanvasApp.Client.Properties.Resources.Purplever; // FIX
            this.pictureBox1.Location = new System.Drawing.Point(3, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(137, 100);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // btnLogout
            // 
            this.btnLogout.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnLogout.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnLogout.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnLogout.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnLogout.FillColor = System.Drawing.Color.Transparent;
            this.btnLogout.Font = new System.Drawing.Font("Segoe UI Semibold", 10.2F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnLogout.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.btnLogout.Location = new System.Drawing.Point(1130, 29);
            this.btnLogout.Name = "btnLogout";
            this.btnLogout.Size = new System.Drawing.Size(137, 53);
            this.btnLogout.TabIndex = 2;
            this.btnLogout.Text = "Đăng xuất";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Montserrat", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.Color.Black;
            this.label1.Location = new System.Drawing.Point(29, 130);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(250, 31);
            this.label1.TabIndex = 2;
            this.label1.Text = "Bảng trắng trực tuyến";
            // 
            // btnCreateShow
            // 
            this.btnCreateShow.BorderRadius = 20;
            this.btnCreateShow.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnCreateShow.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnCreateShow.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnCreateShow.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnCreateShow.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.btnCreateShow.Font = new System.Drawing.Font("Montserrat", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnCreateShow.ForeColor = System.Drawing.Color.White;
            this.btnCreateShow.Location = new System.Drawing.Point(306, 121);
            this.btnCreateShow.Name = "btnCreateShow";
            this.btnCreateShow.Size = new System.Drawing.Size(263, 49);
            this.btnCreateShow.TabIndex = 3;
            this.btnCreateShow.Text = "+ Tạo bảng trắng mới";
            this.btnCreateShow.Click += new System.EventHandler(this.btnCreateShow_Click);
            // 
            // guna2Panel1
            // 
            this.guna2Panel1.BackColor = System.Drawing.Color.Transparent;
            this.guna2Panel1.BorderRadius = 30;
            this.guna2Panel1.Controls.Add(this.txtMessage);
            this.guna2Panel1.Controls.Add(this.rtxtMessage);
            this.guna2Panel1.Controls.Add(this.lblTitle);
            this.guna2Panel1.FillColor = System.Drawing.Color.White;
            this.guna2Panel1.Font = new System.Drawing.Font("Montserrat", 10.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.guna2Panel1.Location = new System.Drawing.Point(627, 121);
            this.guna2Panel1.Name = "guna2Panel1";
            this.guna2Panel1.Size = new System.Drawing.Size(640, 740);
            this.guna2Panel1.TabIndex = 5;
            this.guna2Panel1.Paint += new System.Windows.Forms.PaintEventHandler(this.guna2Panel1_Paint);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoScroll = true;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(35, 205);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(534, 656);
            this.flowLayoutPanel1.TabIndex = 4;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Montserrat", 10.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTitle.ForeColor = System.Drawing.Color.Black;
            this.lblTitle.Location = new System.Drawing.Point(27, 24);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(218, 28);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "Kênh thảo luận chung";
            // 
            // rtxtMessage
            // 
            this.rtxtMessage.BackColor = System.Drawing.Color.WhiteSmoke;
            this.rtxtMessage.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtxtMessage.Location = new System.Drawing.Point(32, 67);
            this.rtxtMessage.Name = "rtxtMessage";
            this.rtxtMessage.ReadOnly = true;
            this.rtxtMessage.Size = new System.Drawing.Size(575, 587);
            this.rtxtMessage.TabIndex = 1;
            this.rtxtMessage.Text = "";
            // 
            // txtMessage
            // 
            this.txtMessage.BorderRadius = 10;
            this.txtMessage.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtMessage.DefaultText = "";
            this.txtMessage.DisabledState.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(208)))), ((int)(((byte)(208)))));
            this.txtMessage.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(226)))), ((int)(((byte)(226)))));
            this.txtMessage.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(138)))), ((int)(((byte)(138)))), ((int)(((byte)(138)))));
            this.txtMessage.DisabledState.PlaceholderForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(138)))), ((int)(((byte)(138)))), ((int)(((byte)(138)))));
            this.txtMessage.FocusedState.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(94)))), ((int)(((byte)(148)))), ((int)(((byte)(255)))));
            this.txtMessage.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.txtMessage.HoverState.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(94)))), ((int)(((byte)(148)))), ((int)(((byte)(255)))));
            this.txtMessage.Location = new System.Drawing.Point(32, 661);
            this.txtMessage.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtMessage.Name = "txtMessage";
            this.txtMessage.PlaceholderText = "";
            this.txtMessage.SelectedText = "";
            this.txtMessage.Size = new System.Drawing.Size(575, 54);
            this.txtMessage.TabIndex = 2;
            // 
            // LobbyForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(249)))), ((int)(((byte)(250)))));
            this.ClientSize = new System.Drawing.Size(1300, 900);
            this.Controls.Add(this.guna2Panel1);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.btnCreateShow);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.panel1);
            this.ForeColor = System.Drawing.SystemColors.WindowFrame;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "LobbyForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "LobbyForm";
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.guna2Panel1.ResumeLayout(false);
            this.guna2Panel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private Guna.UI2.WinForms.Guna2Button btnLogout;
        private System.Windows.Forms.Label label1;
        private Guna.UI2.WinForms.Guna2Button btnCreateShow;
        private Guna.UI2.WinForms.Guna2Panel guna2Panel1;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.Label lblTitle;
        private Guna.UI2.WinForms.Guna2TextBox txtMessage;
        private System.Windows.Forms.RichTextBox rtxtMessage;
    }
}
