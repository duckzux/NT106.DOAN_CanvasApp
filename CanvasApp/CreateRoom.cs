using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CanvasApp
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

        private void btnCreate_Click(object sender, EventArgs e)
        {
            string roomName = txtRoomName.Text;
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
