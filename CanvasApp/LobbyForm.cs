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
    public partial class LobbyForm : Form
    {
        private CreateRoom createRoom;
        private RequirePassword requirePassword;

        public LobbyForm()
        {
            InitializeComponent();
            btnLogout.Click += btnLogout_Click;

        }

        private void guna2Panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void btnCreateShow_Click(object sender, EventArgs e)
        {
            if (createRoom == null || createRoom.IsDisposed)
            {
                createRoom = new CreateRoom();
                createRoom.Size = new Size(400, 300);

                createRoom.Location = new Point(
                    (this.Width - createRoom.Width) / 2,
                    (this.Height - createRoom.Height) / 2
                );

                createRoom.OnRoomCreated += CreateRoom_OnRoomCreated;
                
                createRoom.OnCancel += () =>
                {
                    createRoom.Visible = false;
                };

                this.Controls.Add(createRoom);
            }

            createRoom.Visible = true;
            createRoom.BringToFront();
        }

        private void CreateRoom_OnRoomCreated(string roomName, string password, string template, string maxPlayersText)
        {
            int maxPlayers = int.Parse(maxPlayersText.Split(' ')[0]);

            RoomCard roomCard = new RoomCard();
            roomCard.RoomName = roomName;
            roomCard.Password = password;
            roomCard.MaxPlayers = maxPlayers;
            roomCard.CurrentPlayers = 0;

            roomCard.OnJoinClick += (s, e) =>
            {
                RoomCard rc = s as RoomCard;

                if (string.IsNullOrEmpty(rc.Password))
                {
                    rc.JoinSuccess();
                }
                else
                {
                    ShowRequirePassword(rc);
                }
            };



            flowLayoutPanel1.Controls.Add(roomCard);

            createRoom.Visible = false;
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            LoginForm login = new LoginForm();
            login.Show();
            this.Hide();
        }

        private void ShowRequirePassword(RoomCard roomCard)
        {
            if (requirePassword == null || requirePassword.IsDisposed)
            {
                requirePassword = new RequirePassword();
                requirePassword.Size = new Size(300, 180);

                requirePassword.Location = new Point(
                    (this.ClientSize.Width - requirePassword.Width) / 2,
                    (this.ClientSize.Height - requirePassword.Height) / 2
                );

                this.Controls.Add(requirePassword);
            }

            requirePassword.Visible = true;
            requirePassword.BringToFront();

            requirePassword.OnSubmit = (inputPass) =>
            {
                if (inputPass == roomCard.Password)
                {
                    roomCard.JoinSuccess();
                    requirePassword.Visible = false;
                }
            };

            requirePassword.OnCancel = () =>
            {
                requirePassword.Visible = false;
            };
        }

    }

}
