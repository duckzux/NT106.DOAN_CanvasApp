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
    public partial class RoomCard : UserControl
    {
        private int currentPlayers = 0;
        private int maxPlayers = 8;
        private string password;
        public string RoomName
        {
            get => lblRoomName.Text;
            set => lblRoomName.Text = value;
        }

        public string Password
        {
            get => password;
            set => password = value;
        }

        public string MaxText
        {
            get => lblPassword.Text;
            set => lblPassword.Text = value;
        }

        public void JoinSuccess()
        {
            CurrentPlayers++;
        }

        public int MaxPlayers
        {
            get => maxPlayers;
            set
            {
                maxPlayers = value;
                lblPassword.Text = $"Max: {maxPlayers}";
                UpdatePlayerLabel();
            }
        }

        public int CurrentPlayers
        {
            get => currentPlayers;
            set
            {
                currentPlayers = value;
                UpdatePlayerLabel();
            }
        }

        public event EventHandler OnJoinClick;

        public RoomCard()
        {
            InitializeComponent();

            btnJoin.Click += (s, e) =>
            {
                if (currentPlayers >= maxPlayers)
                    return;

                OnJoinClick?.Invoke(this, e);
            };
        }

        private void UpdatePlayerLabel()
        {
            lblPlayers.Text = $"{currentPlayers}/{maxPlayers} online";
            btnJoin.Enabled = currentPlayers < maxPlayers;
        }

        private void guna2Panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void guna2Panel1_Paint_1(object sender, PaintEventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void lblRoomName_Click(object sender, EventArgs e)
        {

        }

        private void btnJoin_Click(object sender, EventArgs e)
        {

        }
    }
}
