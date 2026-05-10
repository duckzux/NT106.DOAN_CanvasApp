using System;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class RoomCard : UserControl
    {
        private int currentPlayers = 0;
        private int maxPlayers = 8;
        private string _password;

        /// <summary>ID của room ở server. Cần để ROOM_JOIN.</summary>
        public string RoomId { get; set; }

        public string RoomName
        {
            get => lblRoomName.Text;
            set => lblRoomName.Text = value;
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                lblPassword.Text = string.IsNullOrEmpty(value) ? "Public" : "🔒 Có mật khẩu";
            }
        }

        public bool HasPassword => !string.IsNullOrEmpty(_password);

        public int MaxPlayers
        {
            get => maxPlayers;
            set
            {
                maxPlayers = value;
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
                if (currentPlayers >= maxPlayers) return;
                OnJoinClick?.Invoke(this, e);
            };
        }

        public void JoinSuccess() => CurrentPlayers++;

        private void UpdatePlayerLabel()
        {
            lblPlayers.Text = $"{currentPlayers}/{maxPlayers} online";
            btnJoin.Enabled = currentPlayers < maxPlayers;
        }

        // Designer event stubs
        private void guna2Panel1_Paint(object sender, PaintEventArgs e) { }
        private void guna2Panel1_Paint_1(object sender, PaintEventArgs e) { }
        private void label1_Click(object sender, EventArgs e) { }
        private void lblRoomName_Click(object sender, EventArgs e) { }
        private void btnJoin_Click(object sender, EventArgs e) { }
    }
}
