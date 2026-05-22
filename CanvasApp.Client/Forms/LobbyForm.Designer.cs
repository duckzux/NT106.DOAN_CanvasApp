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
            this.components = new System.ComponentModel.Container();
            this.pnlTopBar = new Guna.UI2.WinForms.Guna2Panel();
            this.ctrlMinimize = new Guna.UI2.WinForms.Guna2ControlBox();
            this.ctrlClose = new Guna.UI2.WinForms.Guna2ControlBox();
            this.guna2DragControl1 = new Guna.UI2.WinForms.Guna2DragControl(this.components);
            this.panel1 = new System.Windows.Forms.Panel();
            this.lblGreeting = new System.Windows.Forms.Label();
            this.btnLogout = new Guna.UI2.WinForms.Guna2Button();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btnCreateShow = new Guna.UI2.WinForms.Guna2Button();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.pnlTopBar.SuspendLayout();
            this.panel1.SuspendLayout();
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
            this.pnlTopBar.Size = new System.Drawing.Size(1300, 30);
            this.pnlTopBar.TabIndex = 0;
            // 
            // ctrlMinimize
            // 
            this.ctrlMinimize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ctrlMinimize.ControlBoxType = Guna.UI2.WinForms.Enums.ControlBoxType.MinimizeBox;
            this.ctrlMinimize.FillColor = System.Drawing.Color.Transparent;
            this.ctrlMinimize.IconColor = System.Drawing.Color.White;
            this.ctrlMinimize.Location = new System.Drawing.Point(1210, 0);
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
            this.ctrlClose.Location = new System.Drawing.Point(1255, 0);
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
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.White;
            this.panel1.Controls.Add(this.lblGreeting);
            this.panel1.Controls.Add(this.btnLogout);
            this.panel1.Controls.Add(this.pictureBox1);
            this.panel1.Location = new System.Drawing.Point(0, 30);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1301, 100);
            this.panel1.TabIndex = 1;
            // 
            // lblGreeting
            // 
            this.lblGreeting.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblGreeting.AutoSize = true;
            this.lblGreeting.BackColor = System.Drawing.Color.MediumPurple;
            this.lblGreeting.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.lblGreeting.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.lblGreeting.Font = new System.Drawing.Font("Montserrat ExtraBold", 10.2F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblGreeting.ForeColor = System.Drawing.Color.GhostWhite;
            this.lblGreeting.Location = new System.Drawing.Point(930, 43);
            this.lblGreeting.Name = "lblGreeting";
            this.lblGreeting.Size = new System.Drawing.Size(116, 26);
            this.lblGreeting.TabIndex = 3;
            this.lblGreeting.Text = "Xin chào, ...";
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
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.Image = global::CanvasApp.Client.Properties.Resources.Purplever;
            this.pictureBox1.Location = new System.Drawing.Point(3, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(137, 100);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Montserrat", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.Color.Black;
            this.label1.Location = new System.Drawing.Point(29, 160);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(249, 27);
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
            this.btnCreateShow.Location = new System.Drawing.Point(306, 151);
            this.btnCreateShow.Name = "btnCreateShow";
            this.btnCreateShow.Size = new System.Drawing.Size(263, 49);
            this.btnCreateShow.TabIndex = 3;
            this.btnCreateShow.Text = "+ Tạo bảng trắng mới";
            this.btnCreateShow.Click += new System.EventHandler(this.btnCreateShow_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.AutoScroll = true;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(35, 235);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(1232, 656);
            this.flowLayoutPanel1.TabIndex = 4;
            // 
            // LobbyForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(249)))), ((int)(((byte)(250)))));
            this.ClientSize = new System.Drawing.Size(1300, 930);
            this.Controls.Add(this.pnlTopBar);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.btnCreateShow);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.panel1);
            this.ForeColor = System.Drawing.SystemColors.WindowFrame;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "LobbyForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "LobbyForm";
            this.pnlTopBar.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private Guna.UI2.WinForms.Guna2Panel pnlTopBar;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlMinimize;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlClose;
        private Guna.UI2.WinForms.Guna2DragControl guna2DragControl1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private Guna.UI2.WinForms.Guna2Button btnLogout;
        private System.Windows.Forms.Label lblGreeting;
        private System.Windows.Forms.Label label1;
        private Guna.UI2.WinForms.Guna2Button btnCreateShow;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
    }
}
