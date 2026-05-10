namespace CanvasApp.Client
{
    partial class RoomCard
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

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.guna2Panel1 = new Guna.UI2.WinForms.Guna2Panel();
            this.guna2CirclePictureBox1 = new Guna.UI2.WinForms.Guna2CirclePictureBox();
            this.guna2CirclePictureBox2 = new Guna.UI2.WinForms.Guna2CirclePictureBox();
            this.lblRoomName = new System.Windows.Forms.Label();
            this.lblPassword = new System.Windows.Forms.Label();
            this.lblPlayers = new System.Windows.Forms.Label();
            this.btnJoin = new Guna.UI2.WinForms.Guna2Button();
            this.guna2Panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.guna2CirclePictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.guna2CirclePictureBox2)).BeginInit();
            this.SuspendLayout();
            // 
            // guna2Panel1
            // 
            this.guna2Panel1.BorderRadius = 30;
            this.guna2Panel1.Controls.Add(this.guna2CirclePictureBox1);
            this.guna2Panel1.Controls.Add(this.guna2CirclePictureBox2);
            this.guna2Panel1.Controls.Add(this.lblRoomName);
            this.guna2Panel1.Controls.Add(this.lblPassword);
            this.guna2Panel1.Controls.Add(this.lblPlayers);
            this.guna2Panel1.Controls.Add(this.btnJoin);
            this.guna2Panel1.FillColor = System.Drawing.Color.White;
            this.guna2Panel1.Location = new System.Drawing.Point(3, 3);
            this.guna2Panel1.Name = "guna2Panel1";
            this.guna2Panel1.Size = new System.Drawing.Size(508, 188);
            this.guna2Panel1.TabIndex = 0;
            // 
            // guna2CirclePictureBox1
            // 
            this.guna2CirclePictureBox1.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.guna2CirclePictureBox1.Image = global::CanvasApp.Client.Properties.Resources.Screenshot_2026_04_21_011733; // FIX
            this.guna2CirclePictureBox1.ImageRotate = 0F;
            this.guna2CirclePictureBox1.Location = new System.Drawing.Point(453, 13);
            this.guna2CirclePictureBox1.Name = "guna2CirclePictureBox1";
            this.guna2CirclePictureBox1.ShadowDecoration.Mode = Guna.UI2.WinForms.Enums.ShadowMode.Circle;
            this.guna2CirclePictureBox1.Size = new System.Drawing.Size(37, 27);
            this.guna2CirclePictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.guna2CirclePictureBox1.TabIndex = 4;
            this.guna2CirclePictureBox1.TabStop = false;
            // 
            // guna2CirclePictureBox2
            // 
            this.guna2CirclePictureBox2.Image = global::CanvasApp.Client.Properties.Resources.Screenshot_2026_04_21_021638; // FIX
            this.guna2CirclePictureBox2.ImageRotate = 0F;
            this.guna2CirclePictureBox2.Location = new System.Drawing.Point(42, 85);
            this.guna2CirclePictureBox2.Name = "guna2CirclePictureBox2";
            this.guna2CirclePictureBox2.ShadowDecoration.Mode = Guna.UI2.WinForms.Enums.ShadowMode.Circle;
            this.guna2CirclePictureBox2.Size = new System.Drawing.Size(30, 27);
            this.guna2CirclePictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.guna2CirclePictureBox2.TabIndex = 6;
            this.guna2CirclePictureBox2.TabStop = false;
            // 
            // lblRoomName
            // 
            this.lblRoomName.AutoSize = true;
            this.lblRoomName.Font = new System.Drawing.Font("Montserrat", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblRoomName.ForeColor = System.Drawing.Color.Black;
            this.lblRoomName.Location = new System.Drawing.Point(41, 21);
            this.lblRoomName.Name = "lblRoomName";
            this.lblRoomName.Size = new System.Drawing.Size(73, 31);
            this.lblRoomName.TabIndex = 0;
            this.lblRoomName.Text = "label1";
            this.lblRoomName.Click += new System.EventHandler(this.lblRoomName_Click);
            // 
            // lblPassword
            // 
            this.lblPassword.AutoSize = true;
            this.lblPassword.Font = new System.Drawing.Font("Montserrat", 10.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblPassword.ForeColor = System.Drawing.Color.Black;
            this.lblPassword.Location = new System.Drawing.Point(41, 55);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(66, 28);
            this.lblPassword.TabIndex = 1;
            this.lblPassword.Text = "label2";
            // 
            // lblPlayers
            // 
            this.lblPlayers.AutoSize = true;
            this.lblPlayers.Font = new System.Drawing.Font("Montserrat", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblPlayers.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.lblPlayers.Location = new System.Drawing.Point(73, 91);
            this.lblPlayers.Name = "lblPlayers";
            this.lblPlayers.Size = new System.Drawing.Size(57, 24);
            this.lblPlayers.TabIndex = 3;
            this.lblPlayers.Text = "label3";
            // 
            // btnJoin
            // 
            this.btnJoin.BorderRadius = 20;
            this.btnJoin.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnJoin.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnJoin.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnJoin.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnJoin.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(86)))), ((int)(((byte)(207)))));
            this.btnJoin.Font = new System.Drawing.Font("Montserrat SemiBold", 7.8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnJoin.ForeColor = System.Drawing.Color.White;
            this.btnJoin.Location = new System.Drawing.Point(29, 118);
            this.btnJoin.Name = "btnJoin";
            this.btnJoin.Size = new System.Drawing.Size(185, 49);
            this.btnJoin.TabIndex = 2;
            this.btnJoin.Text = "Tham gia";
            this.btnJoin.Click += new System.EventHandler(this.btnJoin_Click);
            // 
            // RoomCard
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Transparent;
            this.Controls.Add(this.guna2Panel1);
            this.Name = "RoomCard";
            this.Size = new System.Drawing.Size(526, 194);
            this.guna2Panel1.ResumeLayout(false);
            this.guna2Panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.guna2CirclePictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.guna2CirclePictureBox2)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private Guna.UI2.WinForms.Guna2Panel guna2Panel1;
        private Guna.UI2.WinForms.Guna2CirclePictureBox guna2CirclePictureBox1;
        private Guna.UI2.WinForms.Guna2CirclePictureBox guna2CirclePictureBox2;
        private System.Windows.Forms.Label lblRoomName;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.Label lblPlayers;
        private Guna.UI2.WinForms.Guna2Button btnJoin;
    }
}
