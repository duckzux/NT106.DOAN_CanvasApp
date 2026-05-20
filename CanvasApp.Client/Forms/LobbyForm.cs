using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CanvasApp.Common;
using CanvasMessage = CanvasApp.Common.Message;

namespace CanvasApp.Client
{
    public partial class LobbyForm : Form
    {
        private CreateRoom createRoom;
        private RequirePassword requirePassword;
        private RoomCard _pendingJoinCard;
        private string _pendingInviteCode;
        private string _pendingPassword = "";

        public LobbyForm()
        {
            InitializeComponent();
            btnLogout.Click += btnLogout_Click;

            CanvasClient.Instance.OnMessageReceived += OnServerMessage;
            CanvasClient.Instance.OnDisconnected += OnDisconnected;

            this.Load += async (s, e) =>
            {
                AddJoinByCodeButton();

                // ✅ Connect to LB to get room list (short-lived connection, will reconnect on join)
                bool connected = await CanvasClient.Instance.ConnectToServerAsync(Session.LB_HOST, Session.LB_PORT);
                if (!connected)
                {
                    MessageBox.Show("Không kết nối được server. Vui lòng kiểm tra kết nối.", "Lỗi mạng",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                await CanvasClient.Instance.RequestRoomListAsync();
            };
        }

        // Adds a "Nhập mã mời" button programmatically next to btnCreateShow.
        private void AddJoinByCodeButton()
        {
            var btn = new Guna.UI2.WinForms.Guna2Button
            {
                Text = "Nhập mã mời",
                Size = new Size(140, btnCreateShow.Height),
                Location = new Point(btnCreateShow.Right + 10, btnCreateShow.Top),
                BorderRadius = btnCreateShow.BorderRadius,
                FillColor = System.Drawing.Color.FromArgb(52, 152, 219),
                ForeColor = System.Drawing.Color.White,
                Font = btnCreateShow.Font
            };
            btn.Click += async (s, e) => await PromptJoinByCode();
            btnCreateShow.Parent.Controls.Add(btn);
        }

        private async Task PromptJoinByCode()
        {
            using (var dlg = new Form
            {
                Text = "Tham gia bằng mã mời",
                Size = new Size(340, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var lblCode = new Label { Text = "Mã mời:", Location = new Point(20, 20), AutoSize = true };
                var txtCode = new TextBox { Location = new Point(100, 17), Size = new Size(200, 25), CharacterCasing = CharacterCasing.Upper };
                var lblPwd  = new Label { Text = "Mật khẩu:", Location = new Point(20, 58), AutoSize = true };
                var txtPwd  = new TextBox { Location = new Point(100, 55), Size = new Size(200, 25), PasswordChar = '●' };
                var lblHint = new Label { Text = "(bỏ trống nếu phòng không có mật khẩu)", Location = new Point(100, 80), AutoSize = true, ForeColor = System.Drawing.Color.Gray, Font = new Font(Font.FontFamily, 7f) };
                var btnJoin = new Button { Text = "Tham gia", Location = new Point(100, 110), Size = new Size(100, 32), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Hủy", Location = new Point(210, 110), Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lblCode, txtCode, lblPwd, txtPwd, lblHint, btnJoin, btnCancel });
                dlg.AcceptButton = btnJoin;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(txtCode.Text))
                {
                    _pendingInviteCode = txtCode.Text.Trim().ToUpper();
                    _pendingPassword = txtPwd.Text;
                    await CanvasClient.Instance.JoinRoomByCodeAsync(_pendingInviteCode, txtPwd.Text);
                }
            }
        }

        // ── Server message handler ──────────────────────────────────────
        private void OnServerMessage(CanvasMessage msg)
        {
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
                        // The creator gets this; others get ROOM_LIST_RESULT pushed from server
                        var newRoom = msg.GetData<Room>();
                        AddRoomCard(newRoom);
                        if (createRoom != null) createRoom.Visible = false;
                        break;

                    case MessageType.ROOM_JOIN_RESULT:
                        var joinRes = msg.GetData<JoinRoomResult>();
                        HandleJoinResult(joinRes);
                        break;

                    case MessageType.CHAT_HISTORY:
                        // Chat history is only relevant inside CanvasForm; ignore here.
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
            _pendingPassword = password;
            await CanvasClient.Instance.JoinRoomAsync(card.RoomId, password);
        }

        private async void HandleJoinResult(JoinRoomResult res)
        {
            if (!res.Success)
            {
                // Server says the room requires a password that wasn't supplied
                // (happens when joining by invite code without entering a password)
                if (res.RequiresPassword && !string.IsNullOrEmpty(_pendingInviteCode))
                {
                    ShowRequirePasswordForCode(_pendingInviteCode);
                    return;
                }

                MessageBox.Show(res.Message, "Không vào được phòng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _pendingInviteCode = null;
                return;
            }

            _pendingInviteCode = null;

            // ✅ LB trả về địa chỉ Canvas Server đúng room
            string canvasHost = res.ServerHost ?? Session.LB_HOST;
            int canvasPort = res.ServerPort > 0 ? res.ServerPort : 9002;

            // ✅ Lưu vào Session để reconnect đúng server nếu mất kết nối
            Session.CanvasHost = canvasHost;
            Session.CanvasPort = canvasPort;

            // Lưu password trước khi reset để gửi ROOM_JOIN lần 2 vào Canvas Server
            string passwordForRejoin = _pendingPassword;
            string roomIdForRejoin = res.Room?.Id;

            // ✅ Unsubscribe LobbyForm events TRƯỚC khi switch server, tránh nhận message của Canvas connection
            // (nếu không unsubscribe, ROOM_JOIN_RESULT lần 2 sẽ trigger HandleJoinResult lần nữa → mở 2 CanvasForm)
            CanvasClient.Instance.OnMessageReceived -= OnServerMessage;
            CanvasClient.Instance.OnDisconnected -= OnDisconnected;

            // ✅ Connect đến Canvas Server (silent disconnect LB nhờ generation counter trong CanvasClient)
            bool connected = await CanvasClient.Instance.ConnectToServerAsync(canvasHost, canvasPort);
            if (!connected)
            {
                // Re-subscribe và reconnect LB để user vẫn dùng được Lobby
                CanvasClient.Instance.OnMessageReceived += OnServerMessage;
                CanvasClient.Instance.OnDisconnected += OnDisconnected;
                await CanvasClient.Instance.ConnectToServerAsync(Session.LB_HOST, Session.LB_PORT);
                MessageBox.Show("Không kết nối được Canvas Server!", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ✅ Tạo CanvasForm (constructor sẽ tự subscribe OnMessageReceived)
            var canvas = new CanvasForm();
            canvas.SetRoom(res, _pendingPassword);
            _pendingPassword = "";
            canvas.Show();
            this.Hide();

            // ✅ Gửi ROOM_JOIN lần 2 trên connection direct để Canvas Server biết client đang trong room
            // (lần 1 đi qua LB; khi disconnect LB, server-side đã remove client khỏi room state)
            if (!string.IsNullOrEmpty(roomIdForRejoin))
                await CanvasClient.Instance.JoinRoomAsync(roomIdForRejoin, passwordForRejoin);

            canvas.FormClosed += async (s, e) =>
            {
                this.Show();
                // ✅ Re-subscribe LobbyForm events
                CanvasClient.Instance.OnMessageReceived += OnServerMessage;
                CanvasClient.Instance.OnDisconnected += OnDisconnected;
                // ✅ Re-connect LB để lấy room list mới (connection trước trỏ vào Canvas Server)
                bool reConnected = await CanvasClient.Instance.ConnectToServerAsync(Session.LB_HOST, Session.LB_PORT);
                if (reConnected)
                    await CanvasClient.Instance.RequestRoomListAsync();
            };
        }

        // Shows a password dialog then retries the invite-code join with the password.
        private void ShowRequirePasswordForCode(string inviteCode)
        {
            if (requirePassword == null || requirePassword.IsDisposed)
            {
                requirePassword = new RequirePassword();
                requirePassword.Size = new Size(466, 239);
                requirePassword.Location = new Point(
                    (this.ClientSize.Width  - requirePassword.Width)  / 2,
                    (this.ClientSize.Height - requirePassword.Height) / 2);
                this.Controls.Add(requirePassword);
            }

            requirePassword.Visible = true;
            requirePassword.BringToFront();

            requirePassword.OnSubmit = async (inputPass) =>
            {
                requirePassword.Visible = false;
                _pendingPassword = inputPass;
                _pendingInviteCode = inviteCode;
                await CanvasClient.Instance.JoinRoomByCodeAsync(inviteCode, inputPass);
            };
            requirePassword.OnCancel = () =>
            {
                requirePassword.Visible = false;
                _pendingInviteCode = null;
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
                    _pendingPassword = pwd ?? "";
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
