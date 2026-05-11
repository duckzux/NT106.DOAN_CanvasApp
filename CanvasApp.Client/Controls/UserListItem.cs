using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanvasApp.Client.Controls
{
    public partial class UserListItem : UserControl
    {
        private Panel pnlAvatar;
        private Label lblName;
        private Label lblRole;
        private string _avatarInitial = "?";
        private Color _avatarColor = Color.Gray;

        public UserListItem()
        {
            InitializeCustomComponent();
            this.Resize += (s, e) => RepositionRoleBadge();
        }

        private void InitializeCustomComponent()
        {
            this.pnlAvatar = new Panel();
            this.lblName = new Label();
            this.lblRole = new Label();
            this.SuspendLayout();

            this.pnlAvatar.Location = new Point(10, 5);
            this.pnlAvatar.Size = new Size(30, 30);
            this.pnlAvatar.Paint += PnlAvatar_Paint;
            this.pnlAvatar.BackColor = Color.Transparent;

            this.lblName.AutoSize = true;
            this.lblName.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            this.lblName.Location = new Point(48, 10);
            this.lblName.Text = "Username";
            this.lblName.ForeColor = Color.Black;
            this.lblName.BackColor = Color.Transparent;

            this.lblRole.AutoSize = true;
            this.lblRole.Font = new Font("Segoe UI", 7.5F, FontStyle.Bold);
            this.lblRole.Location = new Point(160, 11);
            this.lblRole.Text = "MEMBER";
            this.lblRole.Padding = new Padding(3, 2, 3, 2);

            this.Controls.Add(this.lblRole);
            this.Controls.Add(this.lblName);
            this.Controls.Add(this.pnlAvatar);
            this.Size = new Size(220, 40);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.Margin = new Padding(0, 0, 0, 5);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void PnlAvatar_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(_avatarColor))
                e.Graphics.FillEllipse(brush, 0, 0, pnlAvatar.Width - 1, pnlAvatar.Height - 1);

            var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center };
            using (Font f = new Font("Segoe UI", 10F, FontStyle.Bold))
                e.Graphics.DrawString(_avatarInitial, f, Brushes.White, pnlAvatar.ClientRectangle, sf);
        }

        public void SetData(string name, string role, string hexColor)
        {
            lblName.Text = name;
            _avatarInitial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpper();

            try { _avatarColor = ColorTranslator.FromHtml(hexColor); }
            catch { _avatarColor = Color.DarkOrchid; }
            pnlAvatar.Invalidate();

            lblRole.Text = (role ?? "Member").ToUpper();
            if (string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase))
            {
                lblRole.BackColor = Color.FromArgb(255, 234, 234);
                lblRole.ForeColor = Color.FromArgb(220, 53, 69);
            }
            else if (string.Equals(role, "Viewer", StringComparison.OrdinalIgnoreCase))
            {
                lblRole.BackColor = Color.FromArgb(240, 240, 240);
                lblRole.ForeColor = Color.FromArgb(100, 100, 100);
            }
            else
            {
                lblRole.BackColor = Color.FromArgb(230, 240, 255);
                lblRole.ForeColor = Color.FromArgb(13, 110, 253);
            }

            RepositionRoleBadge();
        }

        private void RepositionRoleBadge()
        {
            if (lblRole == null) return;
            lblRole.Left = Math.Max(lblName.Right + 8, this.Width - lblRole.Width - 10);
        }
    }
}
