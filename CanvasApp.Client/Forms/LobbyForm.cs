using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Lobby uses short-lived TCP queries to the LoadBalancer (no persistent socket while in
    /// the lobby). Every request — ROOM_LIST, ROOM_CREATE, ROOM_JOIN, RESOLVE_INVITE_CODE —
    /// opens its own connection so the LB peeks that specific message and routes by room
    /// affinity. The persistent <see cref="CanvasClient"/> is only opened AFTER ROOM_JOIN
    /// succeeds, directly to the Canvas Server the LB chose for this room.
    /// </summary>
    public partial class LobbyForm : Form
    {
        private CreateRoom createRoom;
        private RequirePassword requirePassword;

        public LobbyForm()
        {
            InitializeComponent();
            btnLogout.Click += btnLogout_Click;

            this.Load += async (s, e) =>
            {
                AddJoinByCodeButton();
                await RefreshRoomListAsync();
            };

            this.Shown += async (s, e) =>
            {
                // After returning from CanvasForm, lobby is shown again — refresh the list.
                if (this.Visible) await RefreshRoomListAsync();
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
                    var inviteCode = txtCode.Text.Trim().ToUpper();
                    await JoinRoomByCodeAsync(inviteCode, txtPwd.Text);
                }
            }
        }

        // ── LB queries (each one fresh short-lived TCP) ─────────────────

        private async Task RefreshRoomListAsync()
        {
            var list = await LobbyClient.GetRoomListAsync();
            if (list == null)
            {
                MessageBox.Show("Không kết nối được Load Balancer.", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RenderRoomList(list);
        }

        private void RenderRoomList(RoomListResult result)
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
                Password = room.HasPassword ? "***" : null,
                MaxPlayers = room.MaxUsers,
                CurrentPlayers = room.CurrentUsers
            };

            card.OnJoinClick += (s, e) =>
            {
                var rc = s as RoomCard;
                if (rc.HasPassword)
                    ShowRequirePassword(rc);
                else
                    _ = JoinRoomAsync(rc.RoomId, "");
            };

            flowLayoutPanel1.Controls.Add(card);
        }

        // ── Join logic ──────────────────────────────────────────────────

        private async Task JoinRoomAsync(string roomId, string password)
        {
            // 1) Ask LB to route the join. LB looks up routing table → forwards to the correct
            //    Canvas Server. Reply carries ServerHost/ServerPort so we know where to direct-connect.
            var res = await LobbyClient.JoinRoomAsync(roomId, password);
            await HandleJoinResultAsync(res, roomId, password);
        }

        private async Task JoinRoomByCodeAsync(string inviteCode, string password)
        {
            // Two-step so LB has the resolved roomId when routing the ROOM_JOIN_BY_CODE:
            //   1) RESOLVE_INVITE_CODE → roomId  (any canvas can answer)
            //   2) ROOM_JOIN_BY_CODE with that roomId → LB looks up routing table → correct canvas
            var resolved = await LobbyClient.ResolveInviteCodeAsync(inviteCode);
            if (resolved == null || !resolved.Success)
            {
                MessageBox.Show(resolved?.Message ?? "Không kết nối được server.",
                    "Mã mời không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var res = await LobbyClient.JoinRoomByCodeAsync(inviteCode, password, resolved.RoomId);

            // Special-case: server asks for password (only known after the resolve)
            if (res != null && !res.Success && res.RequiresPassword)
            {
                ShowRequirePasswordForCode(inviteCode);
                return;
            }

            await HandleJoinResultAsync(res, resolved.RoomId, password);
        }

        private async Task HandleJoinResultAsync(JoinRoomResult res, string roomIdForRejoin, string passwordForRejoin)
        {
            if (res == null)
            {
                MessageBox.Show("Load Balancer không phản hồi.", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!res.Success)
            {
                MessageBox.Show(res.Message, "Không vào được phòng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string canvasHost = res.ServerHost ?? Session.LB_HOST;
            int    canvasPort = res.ServerPort > 0 ? res.ServerPort : 9002;

            // Persist for reconnect attempts inside CanvasForm
            Session.CanvasHost = canvasHost;
            Session.CanvasPort = canvasPort;

            // 2) Open the PERSISTENT TCP to the actual Canvas Server the LB pointed us to.
            //    From this moment on, the user's draw/chat traffic flows over this socket.
            bool connected = await CanvasClient.Instance.ConnectToServerAsync(canvasHost, canvasPort);
            if (!connected)
            {
                MessageBox.Show("Không kết nối được Canvas Server!", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3) Open CanvasForm (it subscribes to CanvasClient events in its ctor) and prime
            //    it with the snapshot + members from the LB-routed join reply.
            var canvas = new CanvasForm();
            canvas.SetRoom(res, passwordForRejoin);
            canvas.Show();
            this.Hide();

            // 4) Re-send ROOM_JOIN on the direct connection so the Canvas Server registers
            //    this socket as belonging to the room (the LB-side socket has been closed,
            //    so the server's view of the client from step 1 is already gone). The
            //    second join also triggers CHAT_HISTORY + ROOM_UPDATE on the persistent
            //    connection, which is what CanvasForm wants to react to.
            if (!string.IsNullOrEmpty(roomIdForRejoin))
                await CanvasClient.Instance.JoinRoomAsync(roomIdForRejoin, passwordForRejoin);

            canvas.FormClosed += async (s, e) =>
            {
                this.Show();
                await RefreshRoomListAsync();
            };
        }

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
                await JoinRoomByCodeAsync(inviteCode, inputPass);
            };
            requirePassword.OnCancel = () => requirePassword.Visible = false;
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

                    var newRoom = await LobbyClient.CreateRoomAsync(new CreateRoomRequest
                    {
                        Name = name,
                        Password = pwd,
                        Template = template,
                        MaxUsers = max
                    });

                    createRoom.Visible = false;

                    if (newRoom == null)
                    {
                        MessageBox.Show("Tạo phòng thất bại.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // The LB has already claimed this room's routing on the create response,
                    // so the upcoming join lands on the same server that owns the state.
                    await JoinRoomAsync(newRoom.Id, pwd ?? "");
                };

                createRoom.OnCancel += () => createRoom.Visible = false;
                this.Controls.Add(createRoom);
            }
            createRoom.Visible = true;
            createRoom.BringToFront();
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
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

            requirePassword.OnSubmit = async (inputPass) =>
            {
                requirePassword.Visible = false;
                await JoinRoomAsync(roomCard.RoomId, inputPass);
            };
            requirePassword.OnCancel = () => requirePassword.Visible = false;
        }
    }
}
