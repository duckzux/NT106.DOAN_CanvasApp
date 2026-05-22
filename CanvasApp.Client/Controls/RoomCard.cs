using System;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class RoomCard : UserControl
    {
        private int currentPlayers = 0;
        private int maxPlayers = 8;
        private string _password;
        private int _ownerId;

        /// <summary>ID của room ở server. Cần để ROOM_JOIN.</summary>
        public string RoomId { get; set; }

        /// <summary>
        /// UserId của chủ phòng. Khi set, RoomCard tự động ẩn/hiện nút Xóa và Đổi mật khẩu
        /// dựa vào việc current user (Session.CurrentUser) có phải là chủ phòng hay không.
        /// </summary>
        public int OwnerId
        {
            get => _ownerId;
            set
            {
                _ownerId = value;
                UpdateOwnerControlsVisibility();
            }
        }

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
        // Raised when the owner clicks "Xóa phòng" / "Đổi mật khẩu". LobbyForm wires these
        // up to LobbyClient.DeleteRoomAsync / UpdateRoomPasswordAsync.
        public event EventHandler OnDeleteClick;
        public event EventHandler OnChangePasswordClick;

        public RoomCard()
        {
            InitializeComponent();
            btnJoin.Click += (s, e) =>
            {
                if (currentPlayers >= maxPlayers) return;
                OnJoinClick?.Invoke(this, e);
            };
            btnDelete.Click += (s, e) => OnDeleteClick?.Invoke(this, e);
            btnChangePwd.Click += (s, e) => OnChangePasswordClick?.Invoke(this, e);
        }

        public void JoinSuccess() => CurrentPlayers++;

        private void UpdatePlayerLabel()
        {
            lblPlayers.Text = $"{currentPlayers}/{maxPlayers} online";
            btnJoin.Enabled = currentPlayers < maxPlayers;
        }

        // Toggle owner-only buttons. Called whenever OwnerId is (re)assigned —
        // safe even before the form is shown because the buttons exist after InitializeComponent.
        private void UpdateOwnerControlsVisibility()
        {
            bool isOwner = Session.CurrentUser != null && Session.CurrentUser.Id == _ownerId;
            btnDelete.Visible = isOwner;
            btnChangePwd.Visible = isOwner;
        }

        // Designer event stubs
        private void guna2Panel1_Paint(object sender, PaintEventArgs e) { }
        private void guna2Panel1_Paint_1(object sender, PaintEventArgs e) { }
        private void label1_Click(object sender, EventArgs e) { }
        private void lblRoomName_Click(object sender, EventArgs e) { }
        private void btnJoin_Click(object sender, EventArgs e) { }
    }
}
