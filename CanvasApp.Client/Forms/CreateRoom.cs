using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class CreateRoom : UserControl
    {
        public event Action<string, string, string, string> OnRoomCreated;
        public event Action OnCancel;
        public CreateRoom()
        {
            InitializeComponent();
            btnCreate.Click += btnCreate_Click;
            btnCancel.Click += btnCancel_Click;
        }

        // Must match the server's MaxRoomNameLength constant — keep in sync if that limit
        // ever changes server-side. Enforcing it here gives the user a clear error instead of
        // the silent reject they'd get from the server.
        private const int MaxRoomNameLength = 100;

        private void btnCreate_Click(object sender, EventArgs e)
        {
            string roomName = (txtRoomName.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                MessageBox.Show("Tên phòng không được để trống.", "Thiếu tên phòng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtRoomName.Focus();
                return;
            }
            if (roomName.Length > MaxRoomNameLength)
            {
                MessageBox.Show($"Tên phòng tối đa {MaxRoomNameLength} ký tự (hiện tại {roomName.Length}).",
                    "Tên phòng quá dài", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtRoomName.Focus();
                txtRoomName.SelectAll();
                return;
            }

            string password = txtPassword.Text;
            string template = cboTemplate.SelectedItem?.ToString() ?? "Default";
            string maxPlayers = cboMaxPeople.SelectedItem?.ToString() ?? "4 người";
            OnRoomCreated?.Invoke(roomName, password, template, maxPlayers);
            this.Visible = false;
            txtRoomName.Clear();
            txtPassword.Clear();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            OnCancel?.Invoke();
        }

        private void label5_Click(object sender, EventArgs e)
        {

        }
    }
}
