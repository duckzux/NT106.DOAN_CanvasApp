using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
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

        public CanvasForm()
        {
            InitializeComponent();

            // Setup canvas bitmap
            this.Load += (s, e) => InitCanvas();
            canvasPanel.Paint += CanvasPanel_Paint;
            canvasPanel.MouseDown += Canvas_MouseDown;
            canvasPanel.MouseMove += Canvas_MouseMove;
            canvasPanel.MouseUp += Canvas_MouseUp;

            // Tool buttons
            btnPen.Click += (s, e) => _currentTool = "pen";
            btnEraser.Click += (s, e) => _currentTool = "eraser";
            btnClear.Click += async (s, e) =>
            {
                if (MessageBox.Show("Xóa toàn bộ canvas?", "Xác nhận",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    ClearCanvas();
                    await CanvasClient.Instance.SendAsync(new CanvasApp.Common.Message(MessageType.DRAW_CLEAR));
                }
            };
            btnColor.Click += (s, e) =>
            {
                if (colorDialog1.ShowDialog() == DialogResult.OK)
                    _currentColor = colorDialog1.Color;
            };

            // Color palette
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

            // Server message handler
            CanvasClient.Instance.OnMessageReceived += new Action<CanvasApp.Common.Message>(OnServerMessage);
        }

        public void SetRoom(Room room, List<DrawAction> initialState)
        {
            _room = room;
            this.Load += (s, e) =>
            {
                lblRoomName.Text = room.Name;
                lblRoomCode.Text = $"Mã: {room.Id}";
                // Replay canvas state
                foreach (var action in initialState)
                    DrawActionLocal(action);
                canvasPanel.Invalidate();
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

            _currentStroke = new DrawAction
            {
                Type = _currentTool,
                Color = ColorToHex(_currentTool == "eraser" ? Color.White : _currentColor),
                Thickness = _currentTool == "eraser" ? 20 : _thickness,
                Points = new List<Common.PointF> { _lastPoint }
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

            // Gửi DRAW_END để server lưu state
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
        private void OnServerMessage(CanvasApp.Common.Message msg)
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
                        rtbChatHistory.AppendText($"{chat.Username}: {chat.Text}\r\n");
                        break;

                    case MessageType.ROOM_UPDATE:
                        // Notification users joined/left - skip
                        break;
                }
            }));
        }

        // ── Chat ────────────────────────────────────────────────────────
        private async System.Threading.Tasks.Task SendChat()
        {
            var text = txtMessageInput.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            await CanvasClient.Instance.SendChatAsync(text);
            rtbChatHistory.AppendText($"Bạn: {text}\r\n");
            txtMessageInput.Clear();
        }

        // ── Color palette wiring ────────────────────────────────────────
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

        // ── Helpers ─────────────────────────────────────────────────────
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
    }
}
