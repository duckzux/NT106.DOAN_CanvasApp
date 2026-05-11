using System;
using System.Drawing;
using System.Windows.Forms;
using CanvasApp.Common;
using CanvasMessage = CanvasApp.Common.Message;

namespace CanvasApp.Client
{
    public partial class LobbyForm : Form
    {
        private CreateRoom createRoom;
        private RequirePassword requirePassword;
        private RoomCard _pendingJoinCard; // RoomCard đang chờ kết quả join từ server

        public LobbyForm()
        {
            InitializeComponent();
            btnLogout.Click += btnLogout_Click;

            // Đăng ký nhận message từ server
            CanvasClient.Instance.OnMessageReceived += OnServerMessage;
            CanvasClient.Instance.OnDisconnected += OnDisconnected;

            this.Load += async (s, e) =>
            {
                lblTitle.Text = $"Xin chào, {Session.CurrentUser?.Username}";
                await CanvasClient.Instance.RequestRoomListAsync();
            };
        }

        // ── Server message handler ──────────────────────────────────────
        private void OnServerMessage(CanvasMessage msg)
        {
            // Tất cả update UI phải Invoke về UI thread
            if (this.IsDisposed) return;

            this.BeginInvoke((Action)(() =>
            {
                switch (msg.Type)
                {
                    case MessageType.ROOM_LIST_RESULT:
                        var list = msg.GetData<RoomListResult>();
                        RefreshRoomList(list);
                        break;

                    case MessageType.ROOM_CREATE_RESULT:
                        var newRoom = msg.GetData<Room>();
                        AddRoomCard(newRoom);
                        if (createRoom != null) createRoom.Visible = false;
                        break;

                    case MessageType.ROOM_JOIN_RESULT:
                        var joinRes = msg.GetData<JoinRoomResult>();
                        HandleJoinResult(joinRes);
                        break;

                    case MessageType.ROOM_UPDATE:
                        // Refresh để cập nhật currentUsers
                        _ = CanvasClient.Instance.RequestRoomListAsync();
                        break;
                }
            }));
        }

        private void OnDisconnected()
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((Action)(() =>
            {
                MessageBox.Show("Mất kết nối với server!", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Session.Clear();
                new LoginForm().Show();
                this.Close();
            }));
        }

        private void RefreshRoomList(RoomListResult result)
        {
            flowLayoutPanel1.Controls.Clear();
            foreach (var r in result.Rooms)
                AddRoomCard(r);
        }

        private void AddRoomCard(Room room)
        {
            var card = new RoomCard
            {
                RoomId = room.Id,
                RoomName = room.Name,
                Password = room.HasPassword ? "***" : null, // chỉ marker, server giữ password thật
                MaxPlayers = room.MaxUsers,
                CurrentPlayers = room.CurrentUsers
            };

            card.OnJoinClick += (s, e) =>
            {
                var rc = s as RoomCard;
                if (rc.HasPassword)
                    ShowRequirePassword(rc);
                else
                    JoinRoom(rc, "");
            };

            flowLayoutPanel1.Controls.Add(card);
        }

        // ── Join logic ──────────────────────────────────────────────────
        private async void JoinRoom(RoomCard card, string password)
        {
            _pendingJoinCard = card;
            await CanvasClient.Instance.JoinRoomAsync(card.RoomId, password);
        }

        private void HandleJoinResult(JoinRoomResult res)
        {
            if (!res.Success)
            {
                MessageBox.Show(res.Message, "Không vào được phòng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Mở CanvasForm với roomId + danh sách members hiện tại
            var canvas = new CanvasForm();
            canvas.SetRoom(res.Room, res.CanvasState, res.Members);
            canvas.Show();
            this.Hide();

            canvas.FormClosed += (s, e) =>
            {
                this.Show();
                _ = CanvasClient.Instance.RequestRoomListAsync();
            };
        }

        // ── UI handlers ─────────────────────────────────────────────────
        private void guna2Panel1_Paint(object sender, PaintEventArgs e) { }

        private void btnCreateShow_Click(object sender, EventArgs e)
        {
            if (createRoom == null || createRoom.IsDisposed)
            {
                createRoom = new CreateRoom();
                createRoom.Size = new Size(560, 419);
                createRoom.Location = new Point(
                    (this.Width - createRoom.Width) / 2,
                    (this.Height - createRoom.Height) / 2);

                createRoom.OnRoomCreated += async (name, pwd, template, maxText) =>
                {
                    int max = 4;
                    if (!string.IsNullOrEmpty(maxText))
                        int.TryParse(maxText.Split(' ')[0], out max);

                    await CanvasClient.Instance.CreateRoomAsync(new CreateRoomRequest
                    {
                        Name = name,
                        Password = pwd,
                        Template = template,
                        MaxUsers = max
                    });
                };

                createRoom.OnCancel += () => createRoom.Visible = false;
                this.Controls.Add(createRoom);
            }
            createRoom.Visible = true;
            createRoom.BringToFront();
        }

        private async void btnLogout_Click(object sender, EventArgs e)
        {
            CanvasClient.Instance.OnMessageReceived -= OnServerMessage;
            CanvasClient.Instance.OnDisconnected -= OnDisconnected;
            CanvasClient.Instance.Disconnect();
            Session.Clear();

            var login = new LoginForm();
            login.Show();
            this.Hide();
        }

        private void ShowRequirePassword(RoomCard roomCard)
        {
            if (requirePassword == null || requirePassword.IsDisposed)
            {
                requirePassword = new RequirePassword();
                requirePassword.Size = new Size(466, 239);
                requirePassword.Location = new Point(
                    (this.ClientSize.Width - requirePassword.Width) / 2,
                    (this.ClientSize.Height - requirePassword.Height) / 2);
                this.Controls.Add(requirePassword);
            }

            requirePassword.Visible = true;
            requirePassword.BringToFront();

            requirePassword.OnSubmit = (inputPass) =>
            {
                requirePassword.Visible = false;
                JoinRoom(roomCard, inputPass);
            };
            requirePassword.OnCancel = () => requirePassword.Visible = false;
        }
    }
}
