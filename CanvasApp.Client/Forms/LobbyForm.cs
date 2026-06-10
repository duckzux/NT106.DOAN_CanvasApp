using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
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

        private Timer _autoRefreshTimer;
    
        private readonly System.Threading.SemaphoreSlim _refreshLock = new System.Threading.SemaphoreSlim(1, 1);


        private readonly List<Room> _lastRendered = new List<Room>();

        private readonly HashSet<string> _missingOnceSilent = new HashSet<string>(StringComparer.Ordinal);

        public LobbyForm()
        {
            InitializeComponent();
            btnLogout.Click += btnLogout_Click;

            UpdateGreeting();
            this.SizeChanged += (s, e) => PositionGreetingLabel();

            EnableDoubleBuffering(flowLayoutPanel1);

            this.Load += async (s, e) =>
            {
                AddJoinByCodeButton();
                await RefreshRoomListAsync();
                StartAutoRefresh();
            };

            this.Shown += async (s, e) =>
            {
                if (this.Visible) await RefreshRoomListAsync();
            };

            this.FormClosed += (s, e) => StopAutoRefresh();
        }

        private void StartAutoRefresh()
        {
            if (_autoRefreshTimer != null) return;
            _autoRefreshTimer = new Timer { Interval = 3000 };
            _autoRefreshTimer.Tick += async (s, e) =>
            {
                if (!this.Visible) return;
                await RefreshRoomListAsync(silent: true, tryEnter: true);
            };
            _autoRefreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            _autoRefreshTimer = null;
        }

        private void UpdateGreeting()
        {
            var name = Session.CurrentUser?.Username;
            lblGreeting.Text = string.IsNullOrWhiteSpace(name) ? "Xin chào" : $"Xin chào, {name}";
            PositionGreetingLabel();
        }

        private void PositionGreetingLabel()
        {
            if (lblGreeting == null || btnLogout == null || panel1 == null) return;

            int spacing = 12;
            int x = btnLogout.Left - lblGreeting.Width - spacing;
            int y = btnLogout.Top + (btnLogout.Height - lblGreeting.Height) / 2;

            lblGreeting.Location = new Point(Math.Max(0, x), Math.Max(0, y));
        }

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

        private async Task RefreshRoomListAsync(bool silent = false, bool tryEnter = false)
        {

            if (tryEnter)
            {
                if (!await _refreshLock.WaitAsync(0)) return;
            }
            else
            {
                await _refreshLock.WaitAsync();
            }
            try
            {
                var list = await LobbyClient.GetRoomListAsync();
                if (list == null)
                {
                    if (!silent)
                    {
                        MessageBox.Show("Không kết nối được Load Balancer.", "Lỗi mạng",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }
                RenderRoomList(list, silent);
            }
            finally { _refreshLock.Release(); }
        }

        private void RenderRoomList(RoomListResult result, bool silent)
        {
            var incoming = result?.Rooms ?? new List<Room>();
            var incomingIds = new HashSet<string>(incoming.Select(r => r.Id), StringComparer.Ordinal);

            var displayRooms = new List<Room>(incoming);
            if (silent && _lastRendered.Count > 0)
            {
                var nextMissingOnce = new HashSet<string>(StringComparer.Ordinal);
                foreach (var prev in _lastRendered)
                {
                    if (incomingIds.Contains(prev.Id)) continue; // still there
                    if (_missingOnceSilent.Contains(prev.Id)) continue; // missed twice → drop
                    // First miss for this room — keep it visible for one more tick.
                    displayRooms.Add(prev);
                    nextMissingOnce.Add(prev.Id);
                }
                _missingOnceSilent.Clear();
                foreach (var id in nextMissingOnce) _missingOnceSilent.Add(id);
            }
            else
            {
                _missingOnceSilent.Clear();
            }

            DiffApplyCards(displayRooms);

            _lastRendered.Clear();
            _lastRendered.AddRange(displayRooms);
        }

        /// <summary>
        /// Updates the FlowLayoutPanel in place: removes cards whose RoomId is no longer in
        /// <paramref name="rooms"/>, adds cards for new RoomIds, and mutates the labels on
        /// existing cards if their count/max changed. Layout is suspended for the duration
        /// so the user never sees an intermediate empty state.
        /// </summary>
        private void DiffApplyCards(List<Room> rooms)
        {
            flowLayoutPanel1.SuspendLayout();
            try
            {
                var existing = new Dictionary<string, RoomCard>(StringComparer.Ordinal);
                foreach (Control c in flowLayoutPanel1.Controls)
                    if (c is RoomCard rc && !string.IsNullOrEmpty(rc.RoomId))
                        existing[rc.RoomId] = rc;

                var keep = new HashSet<string>(rooms.Select(r => r.Id), StringComparer.Ordinal);

                // Remove cards that are no longer in the list (do this BEFORE adding so the
                // panel's child collection doesn't grow temporarily).
                foreach (var kv in existing)
                {
                    if (keep.Contains(kv.Key)) continue;
                    flowLayoutPanel1.Controls.Remove(kv.Value);
                    kv.Value.Dispose();
                }

                // Add new cards / update existing in input order so the visible ordering
                // matches the server's list.
                for (int i = 0; i < rooms.Count; i++)
                {
                    var r = rooms[i];
                    if (existing.TryGetValue(r.Id, out var card))
                    {
                        // Mutate mutable fields without recreating the card so its painted
                        // pixels stay stable — no flash, no scroll jump.
                        if (card.RoomName != r.Name) card.RoomName = r.Name;
                        var pwdMarker = r.HasPassword ? "***" : null;
                        if ((card.Password ?? string.Empty) != (pwdMarker ?? string.Empty))
                            card.Password = pwdMarker;
                        if (card.MaxPlayers != r.MaxUsers) card.MaxPlayers = r.MaxUsers;
                        if (card.CurrentPlayers != r.CurrentUsers) card.CurrentPlayers = r.CurrentUsers;
                        if (card.OwnerId != r.OwnerId) card.OwnerId = r.OwnerId;

                        // Move to the correct position if order changed.
                        if (flowLayoutPanel1.Controls.GetChildIndex(card) != i)
                            flowLayoutPanel1.Controls.SetChildIndex(card, i);
                    }
                    else
                    {
                        flowLayoutPanel1.Controls.Add(BuildRoomCard(r));
                        flowLayoutPanel1.Controls.SetChildIndex(flowLayoutPanel1.Controls[flowLayoutPanel1.Controls.Count - 1], i);
                    }
                }
            }
            finally
            {
                flowLayoutPanel1.ResumeLayout();
            }
        }

        private RoomCard BuildRoomCard(Room room)
        {
            var card = new RoomCard
            {
                RoomId = room.Id,
                RoomName = room.Name,
                Password = room.HasPassword ? "***" : null,
                MaxPlayers = room.MaxUsers,
                CurrentPlayers = room.CurrentUsers,
                // Setting OwnerId last triggers UpdateOwnerControlsVisibility() inside RoomCard,
                // which shows/hides the Delete + Change-password buttons depending on whether
                // the current user is this room's owner.
                OwnerId = room.OwnerId
            };

            card.OnJoinClick += (s, e) =>
            {
                var rc = s as RoomCard;
                if (rc.HasPassword)
                    ShowRequirePassword(rc);
                else
                    _ = JoinRoomAsync(rc.RoomId, "");
            };

            card.OnDeleteClick += async (s, e) =>
            {
                var rc = s as RoomCard;
                var confirm = MessageBox.Show(
                    $"Xóa phòng \"{rc.RoomName}\"? Hành động này không thể hoàn tác.",
                    "Xác nhận xóa phòng",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                var result = await LobbyClient.DeleteRoomAsync(rc.RoomId);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(result?.Message ?? "Không kết nối được server.",
                        "Lỗi xóa phòng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                await RefreshRoomListAsync();
            };

            card.OnChangePasswordClick += async (s, e) =>
            {
                var rc = s as RoomCard;
                var newPwd = PromptNewPassword(rc.RoomName);
                if (newPwd == null) return; // user cancelled

                var result = await LobbyClient.UpdateRoomPasswordAsync(rc.RoomId, newPwd);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(result?.Message ?? "Không kết nối được server.",
                        "Lỗi đổi mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                MessageBox.Show(result.Message, "Đổi mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                await RefreshRoomListAsync();
            };

            return card;
        }

        // DoubleBuffered is protected on Control — set it on the FlowLayoutPanel via
        // reflection. Without this, the panel repaints between Suspend/Resume layout calls
        // and the user still sees a brief flash even though we're only mutating in place.
        private static void EnableDoubleBuffering(Control c)
        {
            try
            {
                typeof(Control)
                    .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(c, true, null);
            }
            catch { /* best-effort — not having double-buffering only costs a paint flash */ }
        }

        // Returns null if the user cancelled, "" to clear the password, or the new password.
        private string PromptNewPassword(string roomName)
        {
            using (var dlg = new Form
            {
                Text = $"Đổi mật khẩu — {roomName}",
                Size = new Size(360, 200),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var lbl = new Label { Text = "Mật khẩu mới:", Location = new Point(20, 25), AutoSize = true };
                var txt = new TextBox { Location = new Point(130, 22), Size = new Size(190, 25), PasswordChar = '●' };
                var hint = new Label
                {
                    Text = "(bỏ trống để gỡ mật khẩu phòng)",
                    Location = new Point(130, 50),
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, 7.5f)
                };
                var btnOk = new Button { Text = "Lưu", Location = new Point(120, 90), Size = new Size(95, 32), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Hủy", Location = new Point(225, 90), Size = new Size(95, 32), DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, txt, hint, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        // ── Join logic ──────────────────────────────────────────────────

        private async Task JoinRoomAsync(string roomId, string password)
        {
            // 1) Resolve via LB: server checks room exists + password and returns where to
            //    direct-connect. No Join happens server-side here — so no spurious broadcast.
            var resolved = await LobbyClient.ResolveRoomAsync(roomId, password);
            await HandleResolveResultAsync(resolved, roomId, password, fromCode: null);
        }

        private async Task JoinRoomByCodeAsync(string inviteCode, string password)
        {
            // Two-step so LB has the resolved roomId for room-affinity routing on ROOM_RESOLVE:
            //   1) RESOLVE_INVITE_CODE → roomId (any canvas can answer)
            //   2) ROOM_RESOLVE with that roomId → LB sticky-routes → correct canvas verifies pwd
            var code = await LobbyClient.ResolveInviteCodeAsync(inviteCode);
            if (code == null || !code.Success)
            {
                MessageBox.Show(code?.Message ?? "Không kết nối được server.",
                    "Mã mời không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var resolved = await LobbyClient.ResolveRoomAsync(code.RoomId, password);
            await HandleResolveResultAsync(resolved, code.RoomId, password, fromCode: inviteCode);
        }

        private async Task HandleResolveResultAsync(ResolveRoomResult res, string roomId, string password, string fromCode)
        {
            if (res == null)
            {
                MessageBox.Show("Load Balancer không phản hồi.", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!res.Success)
            {
                // Special-case: password required (typically the invite-code flow where the
                // user didn't know the room had a password until the resolve told them).
                if (res.RequiresPassword && !string.IsNullOrEmpty(fromCode))
                {
                    ShowRequirePasswordForCode(fromCode);
                    return;
                }
                MessageBox.Show(res.Message, "Không vào được phòng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string canvasHost = res.ServerHost ?? Session.LB_HOST;
            int    canvasPort = res.ServerPort > 0 ? res.ServerPort : 9002;

            // Persist for reconnect attempts inside CanvasForm
            Session.CanvasHost = canvasHost;
            Session.CanvasPort = canvasPort;

            // 2) Open the PERSISTENT TCP directly to the resolved Canvas Server. From this
            //    moment on, the user's draw/chat traffic flows over this socket.
            bool connected = await CanvasClient.Instance.ConnectToServerAsync(canvasHost, canvasPort);
            if (!connected)
            {
                MessageBox.Show("Không kết nối được Canvas Server!", "Lỗi mạng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 3) Open CanvasForm (it subscribes to CanvasClient events in its ctor and sends
            //    ROOM_JOIN itself on Shown). The form's OnServerMessage handles ROOM_JOIN_RESULT
            //    + CHAT_HISTORY + ROOM_UPDATE — exactly ONE Join happens server-side now.
            var canvas = new CanvasForm();
            canvas.PrepareForJoin(roomId, password);
            canvas.Show();
            this.Hide();

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
