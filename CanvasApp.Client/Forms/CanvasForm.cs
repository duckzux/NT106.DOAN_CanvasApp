using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Web.Configuration;
using System.Windows.Forms;
using CanvasApp.Client.Controls;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    public partial class CanvasForm : Form
    {
        // ── Drawing state ───────────────────────────────────────────────
        private Bitmap _bitmap;
        private Graphics _graphics;
        private bool _isDrawing = false;
        private Common.PointF _lastPoint;
        private Color _currentColor = Color.Black;
        private int _thickness = 3;
        private string _currentTool = "pen";
        private DrawAction _currentStroke;

        private Room _room;
        private List<RoomMember> _initialMembers; // members lúc join (truyền từ LobbyForm)

        public CanvasForm()
        {
            InitializeComponent();

            //Chống nhấp nháy màn hình khi vẽ 
            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, canvasPanel, new object[] { true });

            this.Load += (s, e) => InitCanvas();
            canvasPanel.Paint += CanvasPanel_Paint;
            canvasPanel.MouseDown += Canvas_MouseDown;
            canvasPanel.MouseMove += Canvas_MouseMove;
            canvasPanel.MouseUp += Canvas_MouseUp;

            // Tools
            btnPen.Click += (s, e) => _currentTool = "pen";
            btnEraser.Click += (s, e) => _currentTool = "eraser";
            btnClear.Click += async (s, e) =>
            {
                if (MessageBox.Show("Xóa toàn bộ canvas?", "Xác nhận",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    ClearCanvas();
                    await CanvasClient.Instance.SendAsync(new Common.Message(MessageType.DRAW_CLEAR));
                }
            };
            btnColor.Click += (s, e) =>
            {
                if (colorDialog1.ShowDialog() == DialogResult.OK)
                    _currentColor = colorDialog1.Color;
            };

            WirePaletteColors();

            // Chat
            btnSendMessage.Click += async (s, e) => await SendChat();
            txtMessageInput.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SendChat(); }
            };

            // Window controls
            ctrlClose.Click += async (s, e) =>
            {
                await CanvasClient.Instance.LeaveRoomAsync();
                this.Close();
            };

            CanvasClient.Instance.OnMessageReceived += OnServerMessage;
        }

        public void SetRoom(Room room, List<DrawAction> initialState, List<RoomMember> initialMembers = null)
        {
            _room = room;
            _initialMembers = initialMembers ?? new List<RoomMember>();
            this.Load += (s, e) =>
            {
                lblRoomName.Text = room.Name;
                lblRoomCode.Text = $"Mã: {room.Id}";
                foreach (var action in initialState)
                    DrawActionLocal(action);
                canvasPanel.Invalidate();

                // Populate user list lúc join
                RenderUserList(_initialMembers);
                AppendSystemMessage($"Bạn đã vào phòng '{room.Name}'.");
            };
        }

        // ── Canvas init ─────────────────────────────────────────────────
        private void InitCanvas()
        {
            _bitmap = new Bitmap(Math.Max(canvasPanel.Width, 100), Math.Max(canvasPanel.Height, 100));
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.Clear(Color.White);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            canvasPanel.Invalidate();
        }

        private void ClearCanvas()
        {
            if (_graphics != null)
            {
                _graphics.Clear(Color.White);
                canvasPanel.Invalidate();
            }
        }

        private void CanvasPanel_Paint(object sender, PaintEventArgs e)
        {
            if (_bitmap != null) e.Graphics.DrawImage(_bitmap, 0, 0);
        }

        // ── Mouse drawing ───────────────────────────────────────────────
        private async void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _isDrawing = true;
            _lastPoint = new Common.PointF(e.X, e.Y);

            int currentThickness = _currentTool == "eraser" ? _thickness * 2 : _thickness;

            _currentStroke = new DrawAction
            {
                Type = _currentTool,
                Color = ColorToHex(_currentTool == "earaser" ? Color.White : _currentColor),
                Thickness = currentThickness,
                Points = new List<Common.PointF> {  _lastPoint}
            };

            await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_START, _currentStroke);
        }

        private async void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            lblCoordinates.Text = $"X: {e.X}, Y: {e.Y}";
            if (!_isDrawing || _currentStroke == null) return;

            var current = new Common.PointF(e.X, e.Y);
            DrawLineLocal(_lastPoint, current, _currentStroke.Color, _currentStroke.Thickness);
            _currentStroke.Points.Add(current);

            await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_MOVE, new DrawAction
            {
                Type = _currentTool,
                Color = _currentStroke.Color,
                Thickness = _currentStroke.Thickness,
                Points = new List<Common.PointF> { _lastPoint, current }
            });

            _lastPoint = current;
            canvasPanel.Invalidate();
        }

        private async void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            if (_currentStroke != null)
                await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_END, _currentStroke);
            _currentStroke = null;
        }

        // ── Apply remote draw action ────────────────────────────────────
        private void DrawActionLocal(DrawAction action)
        {
            if (_graphics == null || action.Points == null || action.Points.Count < 2) return;
            for (int i = 1; i < action.Points.Count; i++)
                DrawLineLocal(action.Points[i - 1], action.Points[i], action.Color, action.Thickness);
        }

        private void DrawLineLocal(Common.PointF from, Common.PointF to, string colorHex, int thickness)
        {
            using (var pen = new Pen(HexToColor(colorHex), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                _graphics.DrawLine(pen, from.X, from.Y, to.X, to.Y);
            }
        }

        // ── Server message handler ──────────────────────────────────────
        private void OnServerMessage(Common.Message msg)
        {
            if (this.IsDisposed) return;
            this.BeginInvoke((Action)(() =>
            {
                switch (msg.Type)
                {
                    case MessageType.DRAW_MOVE:
                    case MessageType.DRAW_END:
                    case MessageType.DRAW_SHAPE:
                        var action = msg.GetData<DrawAction>();
                        DrawActionLocal(action);
                        canvasPanel.Invalidate();
                        break;

                    case MessageType.DRAW_CLEAR:
                        ClearCanvas();
                        break;

                    case MessageType.CHAT_MESSAGE:
                        var chat = msg.GetData<ChatMessage>();
                        AppendChatMessage(chat.Username, chat.Text);
                        break;

                    case MessageType.ROOM_UPDATE:
                        // ── Strongly-typed parse ────────────────────────
                        var update = msg.GetData<RoomMembersUpdate>();
                        if (update == null) break;

                        // Cập nhật user list panel
                        RenderUserList(update.Members);

                        // Notification join/leave
                        if (!string.IsNullOrEmpty(update.JoinedUsername))
                            AppendSystemMessage($"{update.JoinedUsername} đã tham gia phòng.");
                        if (!string.IsNullOrEmpty(update.LeftUsername))
                            AppendSystemMessage($"{update.LeftUsername} đã rời phòng.");
                        break;
                }
            }));
        }

        // ── Render user list vào pnlUserList ────────────────────────────
        private void RenderUserList(List<RoomMember> members)
        {
            pnlUserList.Controls.Clear();
            if (members == null) return;

            int yPos = 5;
            foreach (var m in members)
            {
                var item = new UserListItem();
                item.Width = pnlUserList.Width - 10;   // set width TRƯỚC SetData để badge align đúng
                item.SetData(m.Username ?? "Unknown", m.Role ?? "Member", m.AvatarColor ?? "#7856CF");
                item.Location = new Point(5, yPos);
                pnlUserList.Controls.Add(item);
                yPos += item.Height + 5;
            }
        }

        // ── Chat ────────────────────────────────────────────────────────
        private async System.Threading.Tasks.Task SendChat()
        {
            var text = txtMessageInput.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            await CanvasClient.Instance.SendChatAsync(text);
            txtMessageInput.Clear();
            // Không append local — server sẽ broadcast lại cho mình thấy
        }

        private void AppendChatMessage(string user, string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AppendChatMessage(user, text)));
                return;
            }

            bool isMe = user == Session.CurrentUser?.Username;
            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.SelectionLength = 0;
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Bold);
            rtbChatHistory.SelectionColor = isMe ? Color.FromArgb(120, 86, 207) : Color.Black;
            rtbChatHistory.AppendText($"{(isMe ? "Bạn" : user)}: ");
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Regular);
            rtbChatHistory.SelectionColor = rtbChatHistory.ForeColor;
            rtbChatHistory.AppendText($"{text}\r\n");
            rtbChatHistory.ScrollToCaret();
        }

        private void AppendSystemMessage(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AppendSystemMessage(message)));
                return;
            }

            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.SelectionLength = 0;
            rtbChatHistory.SelectionColor = Color.DimGray;
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Italic);
            rtbChatHistory.AppendText($"[Hệ thống] {message}\r\n");
            rtbChatHistory.SelectionColor = rtbChatHistory.ForeColor;
            rtbChatHistory.SelectionFont = rtbChatHistory.Font;
            rtbChatHistory.ScrollToCaret();
        }

        // ── Color palette ───────────────────────────────────────────────
        private void WirePaletteColors()
        {
            pnlColorBlack.Click += (s, e) => _currentColor = Color.Black;
            pnlColorGray.Click += (s, e) => _currentColor = Color.Gray;
            pnlColorRed.Click += (s, e) => _currentColor = Color.Red;
            pnlColorOrange.Click += (s, e) => _currentColor = Color.Orange;
            pnlColorYellow.Click += (s, e) => _currentColor = Color.Yellow;
            pnlColorGreen.Click += (s, e) => _currentColor = Color.Green;
            pnlColorBlue.Click += (s, e) => _currentColor = Color.Blue;
            pnlColorCyan.Click += (s, e) => _currentColor = Color.Cyan;
            pnlColorPurple.Click += (s, e) => _currentColor = Color.Purple;
            pnlColorPink.Click += (s, e) => _currentColor = Color.Pink;
            pnlColorBrown.Click += (s, e) => _currentColor = Color.Brown;
            pnlColorWhite.Click += (s, e) => _currentColor = Color.White;
        }

        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        private static Color HexToColor(string hex)
        {
            try { return ColorTranslator.FromHtml(hex); }
            catch { return Color.Black; }
        }

        // Designer event stubs
        private void guna2ControlBox2_Click(object sender, EventArgs e)
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        private void toolStripStatusLabel1_Click(object sender, EventArgs e) { }
        private void btnCircle_Click(object sender, EventArgs e) { _currentTool = "circle"; }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CanvasClient.Instance.OnMessageReceived -= OnServerMessage;
            base.OnFormClosed(e);
        }

        //brush-size
        private void tscbSize_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(tscbSize.Text, out int newSize))
            {
                _thickness = newSize;
            }
        }
    }
}
