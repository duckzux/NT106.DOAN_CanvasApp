using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web;
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
        // Chronological log of every committed action (local + remote). Source of truth for RedrawCanvas.
        private readonly List<DrawAction> _history = new List<DrawAction>();
        // Local user's undo/redo stacks — contain only actions THIS client created.
        private Stack<DrawAction> _undoStack = new Stack<DrawAction>();
        private Stack<DrawAction> _redoStack = new Stack<DrawAction>();
        private Graphics _graphics;
        private bool _isDrawing = false;
        private Common.PointF _lastPoint;
        private Common.PointF _currentPoint;
        private Color _currentColor = Color.Black;
        private int _thickness = 3;
        private string _currentTool = "pen";
        private DrawAction _currentStroke;

        private float _zoom = 1.0f;
        private System.Drawing.PointF _panOffset = new System.Drawing.PointF(0, 0);
        private bool _isPanning = false;
        private System.Drawing.PointF _lastMousePos;
        private int _canvasOffsetX;
        private int _canvasOffsetY;

        private Room _room;
        private string _roomPassword;
        // RoomId for the upcoming ROOM_JOIN, set by PrepareForJoin BEFORE Show().
        // Used so the form can send the join itself on Shown — that ensures CHAT_HISTORY
        // and ROOM_JOIN_RESULT arrive while OnServerMessage is already subscribed.
        private string _pendingRoomId;
        private string _template = "Blank";
        private List<RoomMember> _initialMembers; // members lúc join (truyền từ LobbyForm)

        // ── Inline text editing state ───────────────────────────────────
        private bool _textEditActive;
        private string _editText = "";
        private Common.PointF _editCanvasPos;
        private System.Windows.Forms.Timer _cursorTimer;
        private bool _cursorVisible = true;
        // Set when editing an existing text action (vs. placing new). On commit we tell
        // peers to remove the original via DRAW_UNDO + ActionId. On cancel we restore it.
        private string _editingActionId;
        private DrawAction _editingOriginal;

        // Chat file storage: fileId → (FileName, Data)
        private readonly Dictionary<string, (string FileName, byte[] Data)> _chatFiles =
            new Dictionary<string, (string, byte[])>();

        // Room members mapping: UserId -> RoomMember
        private readonly Dictionary<int, RoomMember> _roomMembers = new Dictionary<int, RoomMember>();

        private class StrokeNotification
        {
            public string Username { get; set; }
            public Color Color { get; set; }
            public Common.PointF CanvasPos { get; set; }
            public DateTime ExpiryTime { get; set; }
        }
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, StrokeNotification> _strokeNotifications = 
            new System.Collections.Concurrent.ConcurrentDictionary<int, StrokeNotification>();

        // Smart shape recognition controller (Drawing/ subsystem)
        private readonly Drawing.ShapeSuggestionController _suggest =
            new Drawing.ShapeSuggestionController();

        public CanvasForm()
        {
            InitializeComponent();

            // Scale copy icon (64×64) xuống vừa button 20×20
            if (btnCopyCode.Image != null)
                btnCopyCode.Image = new Bitmap(btnCopyCode.Image, 15, 15);

            // Load btnImportBg icon từ embedded resource (resx ResXFileRef không reliable)
            using (var stream = typeof(CanvasForm).Assembly
                .GetManifestResourceStream("CanvasApp.Client.Resources.upload.png"))
            {
                if (stream != null)
                    btnImportBg.Image = Image.FromStream(stream);
            }
            btnImportBg.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnImportBg.ImageTransparentColor = Color.Empty;
            btnImportBg.ToolTipText = "Chèn ảnh vào canvas";

            // Wire the "select" toolbar button (used to drag/resize imported images).
            InitImageSelectButton();

            //Chống nhấp nháy màn hình khi vẽ
            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, canvasPanel, new object[] { true });

            this.Load += (s, e) => InitCanvas();
            this.KeyPreview = true;
            this.KeyDown += CanvasForm_KeyDown;
            this.KeyPress += CanvasForm_KeyPress;
            canvasPanel.Paint += CanvasPanel_Paint;

            _cursorTimer = new System.Windows.Forms.Timer { Interval = 530 };
            _cursorTimer.Tick += (s, ev) =>
            {
                _cursorVisible = !_cursorVisible;
                if (_textEditActive) canvasPanel.Invalidate();
            };
            _cursorTimer.Start();

            var notificationTimer = new System.Windows.Forms.Timer { Interval = 200 };
            notificationTimer.Tick += (s, ev) =>
            {
                if (_strokeNotifications.Count == 0) return;
                var now = DateTime.UtcNow;
                bool expiredAny = false;
                foreach (var kv in _strokeNotifications)
                {
                    if (now > kv.Value.ExpiryTime)
                    {
                        _strokeNotifications.TryRemove(kv.Key, out _);
                        expiredAny = true;
                    }
                }
                if (expiredAny)
                {
                    canvasPanel.Invalidate();
                }
            };
            notificationTimer.Start();
            canvasPanel.MouseDown += Canvas_MouseDown;
            canvasPanel.MouseMove += Canvas_MouseMove;
            canvasPanel.MouseUp += Canvas_MouseUp;
            canvasPanel.MouseWheel += CanvasPanel_MouseWheel;

            // Tools
            btnPen.Click += (s, e) => _currentTool = "pen";
            btnEraser.Click += (s, e) => _currentTool = "eraser";
            // Undo - Redo
            btnUndo.Click += async (s, e) =>
            {
                if (_undoStack.Count == 0) return;
                var action = _undoStack.Pop();
                _redoStack.Push(action);
                if (!string.IsNullOrEmpty(action.ActionId))
                    _history.RemoveAll(a => a.ActionId == action.ActionId);
                RedrawCanvas();
                // Send the ActionId explicitly so the server takes the targeted UndoActionById
                // path instead of falling back to UndoLastAction. The fallback usually picks the
                // same action, but if the client's _undoStack drifted out of sync with the
                // server's _undoStacks (e.g. a draw action wasn't acknowledged), the fallback
                // would undo a different action than the one this button click just removed
                // from the local history — peers would then see a different stroke disappear.
                await CanvasClient.Instance.SendAsync(new Common.Message(MessageType.DRAW_UNDO,
                    new UndoNotification { ActionId = action.ActionId }));
            };
            btnRedo.Click += async (s, e) =>
            {
                if (_redoStack.Count == 0) return;
                var action = _redoStack.Pop();
                // Generate a fresh ActionId — the previous one is gone from every peer's history.
                action.ActionId = NewActionId();
                _undoStack.Push(action);
                _history.Add(action);
                DrawActionLocal(action);
                canvasPanel.Invalidate();
                await CanvasClient.Instance.SendDrawAsync(MessageTypeFor(action), action);
            };

            // Import image — places as a movable/resizable overlay layer (see CanvasForm.Images.cs).
            btnImportBg.Click += async (s, e) => await ImportImageFromFile();
            btnExport.Click += (s, e) =>
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "PNG Image|*.png|JPEG Image|*.jpg";
                    saveFileDialog.Title = "Xuất bản vẽ ra file ảnh";
                    saveFileDialog.FileName = "my_canvas_export"; 

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        // Tính bounding box của toàn bộ nội dung đã vẽ
                        RectangleF bounds = GetContentBounds();
                        int exportW = Math.Max((int)Math.Ceiling(bounds.Width), 1);
                        int exportH = Math.Max((int)Math.Ceiling(bounds.Height), 1);
                        int originX = (int)bounds.X;
                        int originY = (int)bounds.Y;

                        Bitmap bmp = new Bitmap(exportW, exportH);

                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.White);
                            DrawBackgroundTemplateForExport(g, exportW, exportH, originX, originY);
                            if (_bitmap != null)
                            {
                                // bitmap pixel (bx,by) = canvas (bx-_canvasOffsetX, by-_canvasOffsetY)
                                // export pixel (0,0) = canvas (originX, originY)
                                // → vẽ bitmap tại export (-_canvasOffsetX - originX, -_canvasOffsetY - originY)
                                g.DrawImage(_bitmap, -_canvasOffsetX - originX, -_canvasOffsetY - originY);
                            }
                            // Imported images live on top of the rasterized strokes; export them in
                            // z-order at their current canvas position.
                            foreach (var id in _imageOrder)
                            {
                                if (!_imagesById.TryGetValue(id, out var ci) || ci.Image == null) continue;
                                g.DrawImage(ci.Image, ci.X - originX, ci.Y - originY, ci.Width, ci.Height);
                            }
                        }

                        System.Drawing.Imaging.ImageFormat format = System.Drawing.Imaging.ImageFormat.Png;

                        if (saveFileDialog.FilterIndex == 2)
                        {
                            format = System.Drawing.Imaging.ImageFormat.Jpeg;
                        }
                        bmp.Save(saveFileDialog.FileName, format);
                        bmp.Dispose();

                        MessageBox.Show("Đã xuất ảnh thành công rùi nha bạn ơi! 🎉", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };

            // Shape tools — rectangle/circle/triangle merged into one dropdown on btnRectangle
            // (matches the btnArrow pattern). btnLine stays as its own button.
            var shapeMenu = new ContextMenuStrip();
            shapeMenu.Items.Add("Hình chữ nhật", null, (s, e) => _currentTool = "rectangle");
            shapeMenu.Items.Add("Hình tròn",     null, (s, e) => _currentTool = "circle");
            shapeMenu.Items.Add("Hình tam giác", null, (s, e) => _currentTool = "triangle");
            btnRectangle.Click += (s, e) =>
            {
                var screenPt = toolStrip1.PointToScreen(new Point(btnRectangle.Bounds.Right, btnRectangle.Bounds.Top));
                shapeMenu.Show(screenPt);
            };
            btnRectangle.ToolTipText = "Hình chữ nhật / tròn / tam giác";

            // Hide the now-redundant standalone buttons (kept declared in Designer.cs).
            toolStrip1.Items.Remove(btnCircle);
            toolStrip1.Items.Remove(btnTriangle);

            btnLine.Click += (s, e) => _currentTool = "line";
            btnText.Click += (s, e) => _currentTool = "text";
            chkFill.Click += (s, e) => _currentTool = "fill";

            // Arrow tool — click opens a dropdown of arrow variants.
            var arrowMenu = new ContextMenuStrip();
            arrowMenu.Items.Add("Mũi tên đơn",        null, (s, e) => _currentTool = "arrow");
            arrowMenu.Items.Add("Mũi tên hai đầu",    null, (s, e) => _currentTool = "arrow_double");
            arrowMenu.Items.Add("Mũi tên đứt nét",    null, (s, e) => _currentTool = "arrow_dashed");
            arrowMenu.Items.Add("Mũi tên đậm",        null, (s, e) => _currentTool = "arrow_thick");
            btnArrow.Click += (s, e) =>
            {
                // Anchor menu to the right of the toolstrip button.
                var btn = btnArrow;
                var screenPt = toolStrip1.PointToScreen(new Point(btn.Bounds.Right, btn.Bounds.Top));
                arrowMenu.Show(screenPt);
            };
            
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
            btnAttachFile.Click += async (s, e) => await AttachFile();
            rtbChatHistory.DetectUrls = true;
            rtbChatHistory.LinkClicked += (s, e) =>
            {
                const string prefix = "https://canvas-file/";
                if (!e.LinkText.StartsWith(prefix)) return;
                var fileId = e.LinkText.Substring(prefix.Length);
                if (_chatFiles.TryGetValue(fileId, out var entry))
                {
                    using (var sfd = new SaveFileDialog { FileName = entry.FileName })
                        if (sfd.ShowDialog() == DialogResult.OK)
                            System.IO.File.WriteAllBytes(sfd.FileName, entry.Data);
                }
            };

            // Copy invite code button
            btnCopyCode.Click += (s, e) =>
            {
                var code = _room?.InviteCode ?? _room?.Id;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Mã mời: {code}");
                if (!string.IsNullOrEmpty(_roomPassword))
                    sb.AppendLine($"Mật khẩu: {_roomPassword}");
                Clipboard.SetText(sb.ToString().TrimEnd());

                var origColor = btnCopyCode.BackColor;
                btnCopyCode.BackColor = Color.LightGreen;
                System.Threading.Tasks.Task.Delay(700).ContinueWith(_ =>
                    this.BeginInvoke((Action)(() => btnCopyCode.BackColor = origColor)));
            };

            // Window controls
            ctrlClose.Click += async (s, e) =>
            {
                await CanvasClient.Instance.LeaveRoomAsync();
                this.Close();
            };

            CanvasClient.Instance.OnMessageReceived += OnServerMessage;

            // Send ROOM_JOIN once the form is shown so the handle is live BEFORE the server
            // starts streaming ROOM_JOIN_RESULT / CHAT_HISTORY / ROOM_UPDATE on this socket.
            // Without this we'd race the subscription and lose CHAT_HISTORY.
            this.Shown += async (s, e) =>
            {
                if (!string.IsNullOrEmpty(_pendingRoomId))
                    await CanvasClient.Instance.JoinRoomAsync(_pendingRoomId, _roomPassword ?? "");
            };

            // ── Smart shape recognition wiring ──────────────────────────
            _suggest.CommitShape += async (shape, color, thickness) =>
            {
                var action = new DrawAction
                {
                    ActionId = NewActionId(),
                    Type = shape.ToDrawActionType(),
                    Color = ColorToHex(color),
                    Thickness = thickness,
                    Points = new List<Common.PointF>
                    {
                        new Common.PointF(shape.Start.X, shape.Start.Y),
                        new Common.PointF(shape.End.X,   shape.End.Y)
                    }
                };
                DrawActionLocal(action);
                _history.Add(action);
                _undoStack.Push(action);
                _redoStack.Clear();
                canvasPanel.Invalidate();
                await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_SHAPE, action);
            };

            _suggest.CommitFreehand += async (pts, color, thickness) =>
            {
                var action = new DrawAction
                {
                    ActionId = NewActionId(),
                    Type = "pen",
                    Color = ColorToHex(color),
                    Thickness = thickness,
                    Points = new List<Common.PointF>(pts.Count)
                };
                foreach (var p in pts) action.Points.Add(new Common.PointF(p.X, p.Y));
                DrawActionLocal(action);
                _history.Add(action);
                _undoStack.Push(action);
                _redoStack.Clear();
                canvasPanel.Invalidate();
                await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_END, action);
            };

            // Ctrl+Shift+R bật/tắt chế độ tự động gợi ý hình
            this.KeyDown += (s, e) =>
            {
                if (e.Control && e.Shift && e.KeyCode == Keys.R)
                {
                    _suggest.SmartShapeEnabled = !_suggest.SmartShapeEnabled;
                    lblCoordinates.Text = _suggest.SmartShapeEnabled
                        ? "Smart shape: ON"
                        : "Smart shape: OFF";
                    e.SuppressKeyPress = true;
                }
            };
        }

        /// <summary>
        /// Called BEFORE Show() to seed the password (needed for reconnect) and roomId. The
        /// actual Room + snapshot + members arrive later via <see cref="ApplyJoinResult"/>
        /// when the persistent connection's ROOM_JOIN_RESULT lands. We split it this way so
        /// the LB-routed pre-join (ROOM_RESOLVE) doesn't have to ship snapshot data — the
        /// LB pre-join no longer triggers a real Join on the server.
        /// </summary>
        public void PrepareForJoin(string roomId, string password)
        {
            _pendingRoomId = roomId;
            _roomPassword = password;
        }

        /// <summary>
        /// Apply the ROOM_JOIN_RESULT received over the persistent connection: render member
        /// panel, replay snapshot + delta actions onto the canvas, show the welcome system
        /// message. Safe to call any time after Show() (handle is required).
        /// </summary>
        private void ApplyJoinResult(JoinRoomResult joinRes)
        {
            _room = joinRes.Room;
            _template = joinRes.Room?.Template ?? "Blank";
            _initialMembers = joinRes.Members ?? new List<RoomMember>();

            lblRoomName.Text = $"Phòng vẽ: {joinRes.Room.Name}";
            var code = joinRes.Room.InviteCode ?? joinRes.Room.Id;
            lblRoomCode.Text = $"Mã mời: {code}";

            if (!string.IsNullOrEmpty(joinRes.SnapshotData))
            {
                try
                {
                    var baseline = SnapshotHelper.Decompress(joinRes.SnapshotData);
                    foreach (var action in baseline)
                    {
                        DrawActionLocal(action);
                        if (string.IsNullOrEmpty(action.ActionId))
                            action.ActionId = "srv-" + action.SeqNo;
                        _history.Add(action);
                    }
                }
                catch (Exception ex)
                {
                    AppendSystemMessage($"[Cảnh báo] Không thể tải snapshot: {ex.Message}");
                }
            }

            foreach (var action in joinRes.CanvasState)
            {
                DrawActionLocal(action);
                if (string.IsNullOrEmpty(action.ActionId))
                    action.ActionId = "srv-" + action.SeqNo;
                _history.Add(action);
            }

            canvasPanel.Invalidate();
            RenderUserList(_initialMembers);
            AppendSystemMessage($"Bạn đã vào phòng '{joinRes.Room.Name}'.");

            // Chat history is now embedded in the join result (server fetches it before
            // adding the joiner to the room's broadcast list, so anything we get via live
            // CHAT_MESSAGE after this point is strictly newer and won't duplicate).
            if (joinRes.ChatHistory != null)
            {
                foreach (var m in joinRes.ChatHistory)
                    AppendChatMessage(m.Username, m.Text);
            }
        }

        // ── Canvas init ─────────────────────────────────────────────────
        // Fixed initial canvas-space so every client starts with the same shared region —
        // panel-dependent sizing made bitmaps diverge between clients with different window sizes
        // and lost edge strokes drawn while zoomed out. EnsureCanvasCovers still grows on demand.
        private const int InitialHalfExtent = 2000;

        private void InitCanvas()
        {
            _canvasOffsetX = InitialHalfExtent;
            _canvasOffsetY = InitialHalfExtent;
            _bitmap = new Bitmap(_canvasOffsetX * 2, _canvasOffsetY * 2);
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.Clear(Color.Transparent);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // Dịch gốc tọa độ canvas (0,0) vào giữa bitmap
            _graphics.TranslateTransform(_canvasOffsetX, _canvasOffsetY);
            canvasPanel.Invalidate();
        }

        private void ClearCanvas()
        {
            if (_graphics != null)
            {
                _graphics.Clear(Color.Transparent);
                _history.Clear();
                _undoStack.Clear();
                _redoStack.Clear();
                canvasPanel.Invalidate();
            }
        }

        private void CanvasPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.TranslateTransform(_panOffset.X, _panOffset.Y);
            g.ScaleTransform(_zoom, _zoom);

            DrawBackgroundTemplate(g);

            if (_bitmap != null)
            {
                g.DrawImage(_bitmap, -_canvasOffsetX, -_canvasOffsetY);
            }

            // Imported images + selection handles overlay (CanvasForm.Images.cs).
            RenderImagesAndHandles(g);

            // Draw active shape preview during a shape-tool drag.
            if (_isDrawing && _currentStroke != null && IsShapeTool(_currentTool))
            {
                DrawShape(g, _currentStroke.Type, _lastPoint, _currentPoint, _currentStroke.Color, _currentStroke.Thickness);
            }

            // Inline text editing preview — no background, no border
            if (_textEditActive)
            {
                float fontSize = Math.Max(8f, _thickness);
                using (var font = new Font("Arial", fontSize, FontStyle.Regular, GraphicsUnit.Point))
                using (var brush = new SolidBrush(_currentColor))
                {
                    e.Graphics.DrawString(_editText, font, brush, _editCanvasPos.X, _editCanvasPos.Y);
                    if (_cursorVisible)
                    {
                        var origin = System.Drawing.PointF.Empty;
                        SizeF sz = e.Graphics.MeasureString(
                            _editText.Length == 0 ? " " : _editText, font,
                            origin, StringFormat.GenericTypographic);
                        float cx = _editCanvasPos.X +
                            (_editText.Length == 0 ? 0 :
                             e.Graphics.MeasureString(_editText, font, origin,
                                 StringFormat.GenericTypographic).Width);
                        using (var pen = new Pen(_currentColor, Math.Max(0.5f, 1f / _zoom)))
                            e.Graphics.DrawLine(pen, cx, _editCanvasPos.Y, cx, _editCanvasPos.Y + sz.Height);
                    }
                }
            }

            // Smart shape recognition: live polyline + suggestion overlay.
            // Rendered after everything else so it appears on top.
            _suggest.Render(e.Graphics, _zoom);

            // Vẽ nhãn của Hướng 2 (Hiện tên khi vẽ xong 1 nét)
            if (_strokeNotifications.Count > 0)
            {
                var savedTransform = g.Transform;
                g.ResetTransform();
                try
                {
                    foreach (var notif in _strokeNotifications.Values)
                    {
                        // Chuyển đổi tọa độ canvas sang tọa độ màn hình thực tế (Screen Space)
                        float sx = notif.CanvasPos.X * _zoom + _panOffset.X;
                        float sy = notif.CanvasPos.Y * _zoom + _panOffset.Y;

                        using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
                        using (var bgBrush = new SolidBrush(Color.FromArgb(220, notif.Color.R, notif.Color.G, notif.Color.B)))
                        using (var textBrush = new SolidBrush(Color.White))
                        using (var borderPen = new Pen(Color.White, 1.5f))
                        {
                            string label = "🎨 " + notif.Username;
                            var size = g.MeasureString(label, font);
                            float tx = sx + 10;
                            float ty = sy - size.Height / 2;

                            // Vẽ nhãn tên bo góc hoặc hình chữ nhật hiện đại bán trong suốt
                            g.FillRectangle(bgBrush, tx, ty, size.Width + 6, size.Height + 4);
                            g.DrawRectangle(borderPen, tx, ty, size.Width + 6, size.Height + 4);
                            g.DrawString(label, font, textBrush, tx + 3, ty + 2);
                        }
                    }
                }
                finally
                {
                    g.Transform = savedTransform;
                }
            }
        }

        private void DrawShape(Graphics g, string type, Common.PointF p1, Common.PointF p2, string colorHex, int thickness)
        {
            bool isFill = type.EndsWith("_fill");
            string baseType = type.Replace("_fill", "");
            Color color = HexToColor(colorHex);

            int x = (int)Math.Min(p1.X, p2.X);
            int y = (int)Math.Min(p1.Y, p2.Y);
            int width = (int)Math.Abs(p1.X - p2.X);
            int height = (int)Math.Abs(p1.Y - p2.Y);

            using (Pen pen = new Pen(color, thickness))
            using (SolidBrush brush = new SolidBrush(color))
            {
                if (baseType == "rectangle")
                {
                    if (isFill) g.FillRectangle(brush, x, y, width, height);
                    else g.DrawRectangle(pen, x, y, width, height);
                }
                else if (baseType == "circle") 
                {
                    if (isFill) g.FillEllipse(brush, x, y, width, height);
                    else g.DrawEllipse(pen, x, y, width, height);
                }
                else if (baseType == "triangle")
                {
                    var top = new System.Drawing.PointF((p1.X + p2.X) / 2f, Math.Min(p1.Y, p2.Y));
                    var bl  = new System.Drawing.PointF(Math.Min(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
                    var br  = new System.Drawing.PointF(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y));
                    var pts = new System.Drawing.PointF[] { top, bl, br };
                    if (isFill) g.FillPolygon(brush, pts);
                    else        g.DrawPolygon(pen, pts);
                }
                else if (baseType == "line")
                {
                    g.DrawLine(pen, p1.X, p1.Y, p2.X, p2.Y);
                }
                else if (baseType.StartsWith("arrow"))
                {
                    // arrow / arrow_double / arrow_dashed / arrow_thick
                    if (baseType == "arrow_double")
                    {
                        pen.CustomStartCap = new AdjustableArrowCap(5, 5);
                        pen.CustomEndCap   = new AdjustableArrowCap(5, 5);
                    }
                    else if (baseType == "arrow_dashed")
                    {
                        pen.DashStyle    = DashStyle.Dash;
                        pen.CustomEndCap = new AdjustableArrowCap(5, 5);
                    }
                    else if (baseType == "arrow_thick")
                    {
                        pen.CustomEndCap = new AdjustableArrowCap(8, 8, true);
                    }
                    else
                    {
                        pen.CustomEndCap = new AdjustableArrowCap(5, 5);
                    }
                    g.DrawLine(pen, p1.X, p1.Y, p2.X, p2.Y);
                }
            }
        }

        private void DrawBackgroundTemplate(Graphics g)
        {
            int startX = (int)(-_panOffset.X / _zoom) - 50;
            int startY = (int)(-_panOffset.Y / _zoom) - 50;
            int w = (int)(canvasPanel.Width / _zoom) + 100;
            int h = (int)(canvasPanel.Height / _zoom) + 100;

            g.FillRectangle(Brushes.White, startX, startY, w, h);

            if (_template == "Blank") return;

            const int step = 30;
            int xStart = (startX / step) * step;
            int yStart = (startY / step) * step;
            int xEnd = startX + w;
            int yEnd = startY + h;

            float lineWidth = Math.Max(0.3f, 1f / _zoom);

            if (_template == "Dotted")
            {
                float dotR = Math.Max(1f, 1.5f / _zoom);
                using (var brush = new SolidBrush(Color.FromArgb(180, 180, 200)))
                    for (int x = xStart; x <= xEnd; x += step)
                        for (int y = yStart; y <= yEnd; y += step)
                            g.FillEllipse(brush, x - dotR, y - dotR, dotR * 2, dotR * 2);
            }
            else if (_template == "Lined")
            {
                using (var pen = new Pen(Color.FromArgb(200, 200, 210), lineWidth))
                    for (int y = yStart; y <= yEnd; y += step)
                        g.DrawLine(pen, startX, y, xEnd, y);
            }
            else if (_template == "Grid")
            {
                using (var pen = new Pen(Color.FromArgb(200, 200, 210), lineWidth))
                {
                    for (int x = xStart; x <= xEnd; x += step)
                        g.DrawLine(pen, x, startY, x, yEnd);
                    for (int y = yStart; y <= yEnd; y += step)
                        g.DrawLine(pen, startX, y, xEnd, y);
                }
            }
        }

        // canvasOriginX/Y: toạ độ canvas tương ứng với pixel (0,0) của export bitmap
        private void DrawBackgroundTemplateForExport(Graphics g, int width, int height, int canvasOriginX = 0, int canvasOriginY = 0)
        {
            if (_template == "Blank") return;

            const int step = 30;
            // Offset nội bộ để pattern align với canvas coordinates
            int xOff = ((-canvasOriginX) % step + step) % step;
            int yOff = ((-canvasOriginY) % step + step) % step;

            if (_template == "Dotted")
            {
                float dotR = 1.5f;
                using (var brush = new SolidBrush(Color.FromArgb(180, 180, 200)))
                    for (int x = xOff; x <= width; x += step)
                        for (int y = yOff; y <= height; y += step)
                            g.FillEllipse(brush, x - dotR, y - dotR, dotR * 2, dotR * 2);
            }
            else if (_template == "Lined")
            {
                using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1f))
                    for (int y = yOff; y <= height; y += step)
                        g.DrawLine(pen, 0, y, width, y);
            }
            else if (_template == "Grid")
            {
                using (var pen = new Pen(Color.FromArgb(200, 200, 210), 1f))
                {
                    for (int x = xOff; x <= width; x += step)
                        g.DrawLine(pen, x, 0, x, height);
                    for (int y = yOff; y <= height; y += step)
                        g.DrawLine(pen, 0, y, width, y);
                }
            }
        }

        // Scan trực tiếp bitmap để tìm vùng có nét vẽ thực sự (kể cả remote users)
        private RectangleF GetContentBounds()
        {
            const int padding = 8;

            if (_bitmap == null)
                return new RectangleF(0, 0, canvasPanel.Width, canvasPanel.Height);

            int bw = _bitmap.Width, bh = _bitmap.Height;
            var bmpData = _bitmap.LockBits(
                new Rectangle(0, 0, bw, bh),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int stride = bmpData.Stride;
            byte[] pixels = new byte[stride * bh];
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
            _bitmap.UnlockBits(bmpData);

            int minBX = bw, maxBX = -1, minBY = bh, maxBY = -1;
            for (int y = 0; y < bh; y++)
            {
                int rowBase = y * stride;
                for (int x = 0; x < bw; x++)
                {
                    // Format32bppArgb byte order: B G R A
                    if (pixels[rowBase + x * 4 + 3] > 0)
                    {
                        if (x < minBX) minBX = x;
                        if (x > maxBX) maxBX = x;
                        if (y < minBY) minBY = y;
                        if (y > maxBY) maxBY = y;
                    }
                }
            }

            if (maxBX < 0) // Không có nội dung nào
                return new RectangleF(0, 0, canvasPanel.Width, canvasPanel.Height);

            // Chuyển từ bitmap pixel → canvas coordinates
            // canvas = bitmap_pixel - _canvasOffset
            float cx = minBX - _canvasOffsetX - padding;
            float cy = minBY - _canvasOffsetY - padding;
            float cw = (maxBX - minBX) + padding * 2;
            float ch = (maxBY - minBY) + padding * 2;
            return new RectangleF(cx, cy, cw, ch);
        }

        // ── Mouse drawing ───────────────────────────────────────────────
        private async void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _lastMousePos = e.Location;
                canvasPanel.Cursor = Cursors.Hand;
                return;
            }
            if (_isPanning) return;

            if (e.Button != MouseButtons.Left) return;

            if (_textEditActive)
                CommitTextEdit();

            // Image select/move/resize takes precedence over drawing tools when active.
            if (TrySelectToolMouseDown(e)) return;

            if (_currentTool == "text")
            {
                var clickPt = ScreenToCanvas(e.X, e.Y);
                var existing = HitTestText(clickPt);
                if (existing != null)
                {
                    // Begin editing an existing text. Pull it out of local state so the
                    // bitmap redraws without it; we put it back on cancel or replace on commit.
                    _editingActionId = existing.ActionId;
                    _editingOriginal = existing;
                    _editText        = existing.Type.Substring(5);
                    _editCanvasPos   = existing.Points[0];
                    _currentColor    = HexToColor(existing.Color);
                    _thickness       = Math.Max(1, existing.Thickness);
                    _textEditActive  = true;
                    _cursorVisible   = true;

                    _history.RemoveAll(a => a.ActionId == existing.ActionId);
                    var keep = _undoStack.Where(a => a.ActionId != existing.ActionId).ToArray();
                    // Stack ctor reverses iteration order; pass in original push order to preserve top.
                    _undoStack = new Stack<DrawAction>(keep.Reverse());
                    _redoStack.Clear();
                    RedrawCanvas();
                    return;
                }

                _editingActionId = null;
                _editingOriginal = null;
                _textEditActive = true;
                _editText = "";
                _editCanvasPos = clickPt;
                _cursorVisible = true;
                canvasPanel.Invalidate();
                return;
            }

            if (_currentTool == "fill")
            {
                var pt = ScreenToCanvas(e.X, e.Y);
                EnsureCanvasCovers(pt.X, pt.Y);
                int bx = (int)(pt.X + _canvasOffsetX);
                int by = (int)(pt.Y + _canvasOffsetY);
                FloodFill(bx, by, _currentColor);
                canvasPanel.Invalidate();
                var fillAction = new DrawAction
                {
                    ActionId = NewActionId(),
                    Type = "fill",
                    Color = ColorToHex(_currentColor),
                    Points = new List<Common.PointF> { pt }
                };
                _history.Add(fillAction);
                _undoStack.Push(fillAction);
                _redoStack.Clear();
                await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_FILL, fillAction);
                return;
            }

            // Smart shape recognition intercept (pen tool only).
            // The controller buffers points locally and only commits to the
            // bitmap/network when the user accepts or rejects the suggestion.
            if (_currentTool == "pen" && _suggest.SmartShapeEnabled)
            {
                var smartPt = ScreenToCanvas(e.X, e.Y);
                _suggest.OnMouseDown(
                    new System.Drawing.PointF(smartPt.X, smartPt.Y),
                    _currentColor, _thickness);
                canvasPanel.Invalidate();
                return;
            }

            _isDrawing = true;
            Common.PointF realPoint = ScreenToCanvas(e.X, e.Y);
            _lastPoint = realPoint;
            _currentPoint = realPoint;

            string toolType = IsShapeTool(_currentTool) && chkFill.Checked ? _currentTool + "_fill" : _currentTool;

            int currentThickness = _currentTool == "eraser" ? _thickness * 2 : _thickness;

            _currentStroke = new DrawAction
            {
                ActionId = NewActionId(),
                Type = toolType,
                Color = ColorToHex(_currentTool == "eraser" ? Color.White : _currentColor),
                Thickness = currentThickness,
                Points = new List<Common.PointF> { _lastPoint }
            };
            if (!IsShapeTool(_currentTool)) 
            {
                await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_START, _currentStroke);
            }
        }

        private async void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _panOffset.X += e.X - _lastMousePos.X;
                _panOffset.Y += e.Y - _lastMousePos.Y;
                _lastMousePos = e.Location;
                canvasPanel.Invalidate();
                return;
            }

            lblCoordinates.Text = $"X: {e.X}, Y: {e.Y}";

            // Image drag/resize update (when select tool is mid-gesture).
            if (TrySelectToolMouseMove(e)) return;

            if (_suggest.IsDrawing)
            {
                var smartPt = ScreenToCanvas(e.X, e.Y);
                _suggest.OnMouseMove(new System.Drawing.PointF(smartPt.X, smartPt.Y));
                canvasPanel.Invalidate();
                return;
            }

            if (!_isDrawing || _currentStroke == null) return;

            var current = ScreenToCanvas(e.X, e.Y);

            if (IsShapeTool(_currentTool))
            {
                _currentPoint = current; // Cập nhật để vẽ pre-view shape
                canvasPanel.Invalidate();
            }
            else
            {
                if (_currentTool == "eraser")
                    EraseLineLocal(_lastPoint, current, _currentStroke.Thickness);
                else
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
        }

        private async void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = false;
                canvasPanel.Cursor = Cursors.Default;
                return;
            }

            if (_suggest.IsDrawing)
            {
                var smartPt = ScreenToCanvas(e.X, e.Y);
                _suggest.OnMouseUp(new System.Drawing.PointF(smartPt.X, smartPt.Y), _zoom);
                canvasPanel.Invalidate();
                return;
            }

            // Commit image drag/resize and broadcast TRANSFORM to peers.
            if (await TrySelectToolMouseUpAsync(e)) return;

            if (!_isDrawing) return;
            _isDrawing = false;

            if (_currentStroke == null) return;

            // Capture và clear ngay trước await để tránh race condition
            var stroke = _currentStroke;
            _currentStroke = null;

            if (IsShapeTool(_currentTool))
            {
                stroke.Points.Add(_currentPoint);
                if (stroke.Points.Count >= 2)
                {
                    DrawActionLocal(stroke);
                    _history.Add(stroke);
                    _undoStack.Push(stroke);
                    _redoStack.Clear();
                    canvasPanel.Invalidate();
                    await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_SHAPE, stroke);
                }
            }
            else
            {
                if (stroke.Points.Count > 0)
                {
                    _history.Add(stroke);
                    _undoStack.Push(stroke);
                    _redoStack.Clear();
                }
                await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_END, stroke);
            }
        }

        //Handling scroll events
        private void CanvasPanel_MouseWheel(object sender, MouseEventArgs e)
        {
            float oldZoom = _zoom;

            if (e.Delta > 0) _zoom *= 1.1f;
            else _zoom /= 1.1f;

            _zoom = Math.Max(0.25f, Math.Min(_zoom, 10f));

            _panOffset.X = e.X - (e.X - _panOffset.X) * (_zoom / oldZoom);
            _panOffset.Y = e.Y - (e.Y - _panOffset.Y) * (_zoom / oldZoom);

            canvasPanel.Invalidate(); 
        }

        // Chuyen doi toa do chuot
        private Common.PointF ScreenToCanvas(int x, int y)
        {
            return new Common.PointF((x - _panOffset.X) / _zoom, (y - _panOffset.Y) / _zoom);
        }

        // ── Apply remote draw action ────────────────────────────────────
        private void DrawActionLocal(DrawAction action)
        {
            if (action == null || action.Points == null || _graphics == null) return;
            if (action.Type == "image")
            {
                // Images are overlays — register/update in _imagesById, do NOT rasterize into the bitmap.
                ApplyImageAction(action);
                return;
            }
            if (action.Type.Contains("rectangle") || action.Type.Contains("circle") || action.Type.Contains("line") || action.Type.Contains("arrow") || action.Type.Contains("triangle"))
            {
                if (action.Points.Count < 2) return;
                EnsureCanvasCovers(action.Points[0].X, action.Points[0].Y);
                EnsureCanvasCovers(action.Points[1].X, action.Points[1].Y);
                DrawShape(_graphics, action.Type, action.Points[0], action.Points[1], action.Color, action.Thickness);
                return;
            }
            if (action.Type.StartsWith("text:"))
            {
                if (action.Points.Count < 1) return;
                EnsureCanvasCovers(action.Points[0].X, action.Points[0].Y);
                string textContent = action.Type.Substring(5);
                using (var font = new Font("Arial", Math.Max(1f, action.Thickness), FontStyle.Regular, GraphicsUnit.Point))
                using (var brush = new SolidBrush(HexToColor(action.Color)))
                    _graphics.DrawString(textContent, font, brush, action.Points[0].X, action.Points[0].Y);
                return;
            }

            if (action.Type == "fill")
            {
                if (action.Points.Count < 1) return;
                EnsureCanvasCovers(action.Points[0].X, action.Points[0].Y);
                int bx = (int)(action.Points[0].X + _canvasOffsetX);
                int by = (int)(action.Points[0].Y + _canvasOffsetY);
                FloodFill(bx, by, HexToColor(action.Color));
                return;
            }

            if (action.Points.Count < 2) return;

            if (action.Type == "eraser")
            {
                for (int i = 1; i < action.Points.Count; i++)
                    EraseLineLocal(action.Points[i - 1], action.Points[i], action.Thickness);
                return;
            }

            for (int i = 1; i < action.Points.Count; i++)
                DrawLineLocal(action.Points[i - 1], action.Points[i], action.Color, action.Thickness);
        }

        private void DrawLineLocal(Common.PointF from, Common.PointF to, string colorHex, int thickness)
        {
            EnsureCanvasCovers(from.X, from.Y);
            EnsureCanvasCovers(to.X, to.Y);
            using (var pen = new Pen(HexToColor(colorHex), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                _graphics.DrawLine(pen, from.X, from.Y, to.X, to.Y);
        }

        private void EraseLineLocal(Common.PointF from, Common.PointF to, int thickness)
        {
            EnsureCanvasCovers(from.X, from.Y);
            EnsureCanvasCovers(to.X, to.Y);
            var saved = _graphics.CompositingMode;
            _graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            using (var pen = new Pen(Color.FromArgb(0, 0, 0, 0), thickness)
                   { StartCap = LineCap.Round, EndCap = LineCap.Round })
                _graphics.DrawLine(pen, from.X, from.Y, to.X, to.Y);
            _graphics.CompositingMode = saved;
        }

        // Expands the bitmap if canvas point (cx, cy) maps outside the current bitmap bounds.
        // All draw calls go through this so the canvas is effectively unbounded.
        private void EnsureCanvasCovers(float cx, float cy)
        {
            if (_bitmap == null) return;

            const int margin = 200;
            const int grow   = 2000;

            int bx = (int)(cx + _canvasOffsetX);
            int by = (int)(cy + _canvasOffsetY);

            int growLeft   = bx < margin                    ? grow + margin - bx                         : 0;
            int growTop    = by < margin                    ? grow + margin - by                         : 0;
            int growRight  = bx >= _bitmap.Width  - margin  ? grow + bx - _bitmap.Width  + margin + 1   : 0;
            int growBottom = by >= _bitmap.Height - margin  ? grow + by - _bitmap.Height + margin + 1   : 0;

            if (growLeft == 0 && growTop == 0 && growRight == 0 && growBottom == 0) return;

            int newW = _bitmap.Width  + growLeft + growRight;
            int newH = _bitmap.Height + growTop  + growBottom;

            var newBitmap = new Bitmap(newW, newH);
            using (var g = Graphics.FromImage(newBitmap))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(_bitmap, growLeft, growTop);
            }

            _canvasOffsetX += growLeft;
            _canvasOffsetY += growTop;

            _graphics.Dispose();
            _bitmap.Dispose();

            _bitmap = newBitmap;
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.TranslateTransform(_canvasOffsetX, _canvasOffsetY);
        }

        private void FloodFill(int bitmapX, int bitmapY, Color fillColor)
        {
            if (_bitmap == null) return;
            int bw = _bitmap.Width, bh = _bitmap.Height;
            if (bitmapX < 0 || bitmapX >= bw || bitmapY < 0 || bitmapY >= bh) return;

            var bmpData = _bitmap.LockBits(new Rectangle(0, 0, bw, bh),
                System.Drawing.Imaging.ImageLockMode.ReadWrite,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int stride = bmpData.Stride;
            byte[] pixels = new byte[stride * bh];
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);

            int si = bitmapY * stride + bitmapX * 4;
            byte tB = pixels[si], tG = pixels[si + 1], tR = pixels[si + 2], tA = pixels[si + 3];
            byte fB = fillColor.B, fG = fillColor.G, fR = fillColor.R, fA = fillColor.A;

            if (tB == fB && tG == fG && tR == fR && tA == fA)
            {
                _bitmap.UnlockBits(bmpData);
                return;
            }

            var queue = new Queue<int>();
            var visited = new bool[bw * bh];
            queue.Enqueue(bitmapY * bw + bitmapX);

            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int x = idx % bw, y = idx / bw;
                if (visited[idx]) continue;
                visited[idx] = true;

                int pi = y * stride + x * 4;
                if (pixels[pi] != tB || pixels[pi + 1] != tG ||
                    pixels[pi + 2] != tR || pixels[pi + 3] != tA) continue;

                pixels[pi] = fB; pixels[pi + 1] = fG;
                pixels[pi + 2] = fR; pixels[pi + 3] = fA;

                if (x + 1 < bw)  queue.Enqueue(y * bw + x + 1);
                if (x - 1 >= 0)  queue.Enqueue(y * bw + x - 1);
                if (y + 1 < bh)  queue.Enqueue((y + 1) * bw + x);
                if (y - 1 >= 0)  queue.Enqueue((y - 1) * bw + x);
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            _bitmap.UnlockBits(bmpData);
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
                    {
                        // Preview segment — paint it but don't persist to _history (DRAW_END will deliver the full stroke).
                        var action = msg.GetData<DrawAction>();
                        DrawActionLocal(action);
                        canvasPanel.Invalidate();
                        break;
                    }

                    case MessageType.DRAW_END:
                    case MessageType.DRAW_SHAPE:
                    case MessageType.DRAW_FILL:
                    {
                        var action = msg.GetData<DrawAction>();
                        if (action == null) break;
                        DrawActionLocal(action);
                        if (string.IsNullOrEmpty(action.ActionId))
                            action.ActionId = "srv-" + action.SeqNo; // legacy/server-assigned fallback
                        _history.Add(action);
                        // A remote action invalidates my pending redo branch (Figma semantics).
                        _redoStack.Clear();

                        // Stroke notification trigger
                        if (action.UserId != Session.CurrentUser?.Id && action.Points != null && action.Points.Count > 0)
                        {
                            lock (_roomMembers)
                            {
                                if (_roomMembers.TryGetValue(action.UserId, out var member))
                                {
                                    _strokeNotifications[action.UserId] = new StrokeNotification
                                    {
                                        Username = member.Username,
                                        Color = HexToColor(member.AvatarColor ?? "#7856CF"),
                                        CanvasPos = action.Points.Last(),
                                        ExpiryTime = DateTime.UtcNow.AddSeconds(2)
                                    };
                                }
                            }
                        }

                        canvasPanel.Invalidate();
                        break;
                    }

                    case MessageType.DRAW_IMAGE:
                    {
                        var action = msg.GetData<DrawAction>();
                        if (action == null) break;
                        ApplyImageAction(action);
                        if (string.IsNullOrEmpty(action.ActionId))
                            action.ActionId = "srv-" + action.SeqNo;
                        _history.Add(action);
                        _redoStack.Clear();

                        // Stroke notification trigger for images
                        if (action.UserId != Session.CurrentUser?.Id && action.Points != null && action.Points.Count > 0)
                        {
                            lock (_roomMembers)
                            {
                                if (_roomMembers.TryGetValue(action.UserId, out var member))
                                {
                                    _strokeNotifications[action.UserId] = new StrokeNotification
                                    {
                                        Username = member.Username,
                                        Color = HexToColor(member.AvatarColor ?? "#7856CF"),
                                        CanvasPos = action.Points.Last(),
                                        ExpiryTime = DateTime.UtcNow.AddSeconds(2)
                                    };
                                }
                            }
                        }

                        canvasPanel.Invalidate();
                        break;
                    }

                    case MessageType.DRAW_IMAGE_TRANSFORM:
                    {
                        var action = msg.GetData<DrawAction>();
                        if (action == null || string.IsNullOrEmpty(action.ActionId)) break;
                        ApplyImageAction(action);
                        // Keep the CREATE entry's bounds in sync so RedrawCanvas reproduces latest state.
                        var createAction = _history.FirstOrDefault(a => a?.ActionId == action.ActionId && a.Type == "image");
                        if (createAction != null && action.Points != null && action.Points.Count >= 2)
                        {
                            createAction.Points[0] = action.Points[0];
                            createAction.Points[1] = action.Points[1];
                        }
                        canvasPanel.Invalidate();
                        break;
                    }

                    case MessageType.DRAW_UNDO:
                    {
                        var notif = msg.GetData<UndoNotification>();
                        if (notif == null || string.IsNullOrEmpty(notif.ActionId)) break;
                        var removed = _history.RemoveAll(a => a.ActionId == notif.ActionId);
                        if (removed > 0)
                        {
                            // A peer undid one of their actions — my own redo branch is no longer valid.
                            _redoStack.Clear();
                            RedrawCanvas();
                        }
                        break;
                    }

                    case MessageType.DRAW_CLEAR:
                        ClearCanvas();
                        break;

                    case MessageType.CHAT_MESSAGE:
                        var chat = msg.GetData<ChatMessage>();
                        AppendChatMessage(chat.Username, chat.Text);
                        break;

                    case MessageType.CHAT_HISTORY:
                        var hist = msg.GetData<ChatHistoryResult>();
                        if (hist?.Messages != null)
                            foreach (var m in hist.Messages)
                                AppendChatMessage(m.Username, m.Text);
                        break;

                    case MessageType.CHAT_FILE:
                        var fileMsg = msg.GetData<ChatMessage>();
                        if (fileMsg?.FileData != null)
                        {
                            var fileId = Guid.NewGuid().ToString("N").Substring(0, 12);
                            _chatFiles[fileId] = (fileMsg.FileName, Convert.FromBase64String(fileMsg.FileData));
                            AppendFileMessage(fileMsg.Username, fileMsg.FileName, fileMsg.FileSizeBytes, fileId);
                        }
                        break;

                    case MessageType.ROOM_JOIN_RESULT:
                    {
                        var joinRes = msg.GetData<JoinRoomResult>();
                        if (joinRes == null || !joinRes.Success)
                        {
                            MessageBox.Show(joinRes?.Message ?? "Không vào được phòng",
                                "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            this.Close();
                            break;
                        }
                        ApplyJoinResult(joinRes);
                        break;
                    }

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
                        {
                            AppendSystemMessage($"{update.LeftUsername} đã rời phòng.");
                            // Remove any active stroke notification for this user
                            var keyToRemove = _strokeNotifications.FirstOrDefault(kv => kv.Value.Username == update.LeftUsername).Key;
                            if (keyToRemove != 0)
                            {
                                _strokeNotifications.TryRemove(keyToRemove, out _);
                                canvasPanel.Invalidate();
                            }
                        }
                        break;
                }
            }));
        }

        private void UpdateRoomMembers(List<RoomMember> members)
        {
            if (members == null) return;
            lock (_roomMembers)
            {
                _roomMembers.Clear();
                foreach (var m in members)
                {
                    _roomMembers[m.UserId] = m;
                }
            }
        }

        // ── Render user list vào pnlUserList ────────────────────────────
        private void RenderUserList(List<RoomMember> members)
        {
            UpdateRoomMembers(members);

            pnlUserList.SuspendLayout();
            pnlUserList.Controls.Clear();
            // AutoScroll on the panel handles >3 members; suppress the horizontal bar so the vertical
            // scrollbar appearing (which shrinks ClientSize) doesn't trigger a horizontal one in turn.
            pnlUserList.HorizontalScroll.Enabled = false;
            pnlUserList.HorizontalScroll.Visible = false;
            pnlUserList.AutoScroll = true;

            if (members != null)
            {
                int yPos = 5;
                // ClientSize accounts for the vertical scrollbar so items don't overflow horizontally.
                int itemWidth = pnlUserList.ClientSize.Width - 10;
                foreach (var m in members)
                {
                    var item = new UserListItem { Width = itemWidth };
                    item.SetData(m.Username ?? "Unknown", m.Role ?? "Member", m.AvatarColor ?? "#7856CF");
                    item.Location = new Point(5, yPos);
                    pnlUserList.Controls.Add(item);
                    yPos += item.Height + 5;
                }
            }
            pnlUserList.ResumeLayout();
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

        private async System.Threading.Tasks.Task AttachFile()
        {
            using (var ofd = new OpenFileDialog { Title = "Chọn file để gửi" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                var info = new System.IO.FileInfo(ofd.FileName);
                const long maxBytes = 2L * 1024 * 1024; // 2 MB
                if (info.Length > maxBytes)
                {
                    MessageBox.Show("File quá lớn (tối đa 2 MB).", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var data = System.IO.File.ReadAllBytes(ofd.FileName);
                var fileName = System.IO.Path.GetFileName(ofd.FileName);
                await CanvasClient.Instance.SendFileAsync(fileName, data);
            }
        }

        // Win32 scroll messages — RichTextBox exposes no managed API to read/restore the scroll
        // position, so we use SendMessage to keep the reader anchored when new messages arrive.
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_LINESCROLL = 0x00B6;

        private bool IsChatAtBottom()
        {
            if (rtbChatHistory.TextLength == 0) return true;
            int firstVisible = (int)SendMessage(rtbChatHistory.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            int lineHeight = Math.Max(1, rtbChatHistory.Font.Height);
            int visibleLines = Math.Max(1, rtbChatHistory.ClientSize.Height / lineHeight);
            int totalLines = rtbChatHistory.GetLineFromCharIndex(rtbChatHistory.TextLength) + 1;
            // Treat anything within one visible line of the bottom as "at bottom".
            return firstVisible + visibleLines >= totalLines - 1;
        }

        private void AppendChatMessage(string user, string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AppendChatMessage(user, text)));
                return;
            }

            bool wasAtBottom = IsChatAtBottom();
            int firstVisibleBefore = (int)SendMessage(rtbChatHistory.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

            bool isMe = user == Session.CurrentUser?.Username;
            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.SelectionLength = 0;
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Bold);
            rtbChatHistory.SelectionColor = isMe ? Color.FromArgb(120, 86, 207) : Color.Black;
            rtbChatHistory.AppendText($"{(isMe ? "Bạn" : user)}: ");
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Regular);
            rtbChatHistory.SelectionColor = rtbChatHistory.ForeColor;
            rtbChatHistory.AppendText($"{text}\r\n");
            RestoreChatScroll(wasAtBottom, firstVisibleBefore);
        }

        private void RestoreChatScroll(bool wasAtBottom, int firstVisibleBefore)
        {
            if (wasAtBottom)
            {
                rtbChatHistory.ScrollToCaret();
            }
            else
            {
                // The user was reading earlier history — selection updates above auto-scrolled to the
                // bottom; scroll back so their reading position is preserved.
                int firstVisibleNow = (int)SendMessage(rtbChatHistory.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                int delta = firstVisibleBefore - firstVisibleNow;
                if (delta != 0)
                    SendMessage(rtbChatHistory.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            }
        }

        private void AppendSystemMessage(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AppendSystemMessage(message)));
                return;
            }

            bool wasAtBottom = IsChatAtBottom();
            int firstVisibleBefore = (int)SendMessage(rtbChatHistory.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.SelectionLength = 0;
            rtbChatHistory.SelectionColor = Color.DimGray;
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Italic);
            rtbChatHistory.AppendText($"[Hệ thống] {message}\r\n");
            rtbChatHistory.SelectionColor = rtbChatHistory.ForeColor;
            rtbChatHistory.SelectionFont = rtbChatHistory.Font;
            RestoreChatScroll(wasAtBottom, firstVisibleBefore);
        }

        private void AppendFileMessage(string user, string fileName, long sizeBytes, string fileId)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AppendFileMessage(user, fileName, sizeBytes, fileId)));
                return;
            }

            bool wasAtBottom = IsChatAtBottom();
            int firstVisibleBefore = (int)SendMessage(rtbChatHistory.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

            bool isMe = user == Session.CurrentUser?.Username;
            rtbChatHistory.SelectionStart = rtbChatHistory.TextLength;
            rtbChatHistory.SelectionLength = 0;
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Bold);
            rtbChatHistory.SelectionColor = isMe ? Color.FromArgb(120, 86, 207) : Color.Black;
            rtbChatHistory.AppendText($"{(isMe ? "Bạn" : user)}: 📎 {fileName} ({FormatFileSize(sizeBytes)})\r\n");
            rtbChatHistory.SelectionFont = new Font(rtbChatHistory.Font, FontStyle.Regular);
            rtbChatHistory.SelectionColor = Color.DimGray;
            // Hiển thị link tải xuống — RichTextBox tự detect https:// thành link có thể nhấp
            rtbChatHistory.AppendText($"    https://canvas-file/{fileId}\r\n");
            rtbChatHistory.SelectionFont = rtbChatHistory.Font;
            rtbChatHistory.SelectionColor = rtbChatHistory.ForeColor;
            RestoreChatScroll(wasAtBottom, firstVisibleBefore);
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024):F1} MB";
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

            // GDI+ objects must be explicitly disposed — the GC will eventually finalize them
            // but the underlying native handles + bitmap pixel buffer (up to ~8 MB for a 4000²
            // canvas) stick around until the next gen-2 collection. Multiple lobby<->canvas
            // round-trips without disposal pile up several copies in memory.
            try { _graphics?.Dispose(); } catch { }
            try { _bitmap?.Dispose(); }   catch { }
            try
            {
                foreach (var ci in _imagesById.Values)
                    ci.Image?.Dispose();
                _imagesById.Clear();
                _imageOrder.Clear();
            }
            catch { }
            _graphics = null;
            _bitmap = null;

            base.OnFormClosed(e);
        }
        // ── Undo & Redo Logic ───────────────────────────────────────────
        private void RedrawCanvas()
        {
            _graphics.Clear(Color.Transparent);
            // Image overlays live outside the bitmap; replay history rebuilds them from CREATE actions.
            ResetImagesForRedraw();
            foreach (var action in _history)
                DrawActionLocal(action);
            canvasPanel.Invalidate();
        }

        private static string NewActionId() => Guid.NewGuid().ToString("N");

        // Maps a DrawAction.Type back to the network message type used to broadcast it.
        // Used when redoing a local action — we re-send it as if the user just drew it again.
        private static string MessageTypeFor(DrawAction action)
        {
            if (action == null || string.IsNullOrEmpty(action.Type)) return MessageType.DRAW_END;
            if (action.Type == "fill") return MessageType.DRAW_FILL;
            if (action.Type == "image") return MessageType.DRAW_IMAGE;
            if (action.Type.StartsWith("text:")) return MessageType.DRAW_SHAPE;
            if (action.Type.Contains("rectangle") || action.Type.Contains("circle")
                || action.Type.Contains("line") || action.Type.Contains("arrow")
                || action.Type.Contains("triangle"))
                return MessageType.DRAW_SHAPE;
            return MessageType.DRAW_END; // pen, eraser
        }

        private void CanvasForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_textEditActive)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitTextEdit();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    CancelTextEdit();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Back)
                {
                    if (_editText.Length > 0)
                        _editText = _editText.Substring(0, _editText.Length - 1);
                    canvasPanel.Invalidate();
                    e.SuppressKeyPress = true;
                }
                return; // block Ctrl+Z / Ctrl+Y while typing
            }

            // Smart shape suggestion: Enter accepts, Esc rejects.
            if (_suggest.HasPendingSuggestion)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    _suggest.AcceptSuggestion();
                    canvasPanel.Invalidate();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Escape)
                {
                    _suggest.RejectSuggestion();
                    canvasPanel.Invalidate();
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.Delete && !string.IsNullOrEmpty(_selectedImageId))
            {
                _ = DeleteSelectedImageAsync();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Z)
            {
                btnUndo.PerformClick();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Y)
            {
                btnRedo.PerformClick();
                e.SuppressKeyPress = true;
            }
        }

        private void CanvasForm_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!_textEditActive) return;
            if (e.KeyChar == '\r' || e.KeyChar == '\b' || e.KeyChar == '\x1b') return;
            if (e.KeyChar >= 32)
            {
                _editText += e.KeyChar;
                canvasPanel.Invalidate();
            }
            e.Handled = true;
        }

        private async void CommitTextEdit()
        {
            _textEditActive = false;
            string text = _editText.Trim();
            string oldId = _editingActionId;
            _editingActionId = null;
            _editingOriginal = null;
            _editText = "";
            canvasPanel.Invalidate();

            // If we were editing existing text, tell server + peers to drop the original.
            if (!string.IsNullOrEmpty(oldId))
            {
                await CanvasClient.Instance.SendAsync(new Common.Message(
                    MessageType.DRAW_UNDO,
                    new UndoNotification { ActionId = oldId }));
            }

            if (string.IsNullOrEmpty(text)) return;

            var textAction = new DrawAction
            {
                ActionId = NewActionId(),
                Type = "text:" + text,
                Color = ColorToHex(_currentColor),
                Thickness = Math.Max(8, _thickness),
                Points = new List<Common.PointF> { _editCanvasPos }
            };
            DrawActionLocal(textAction);
            _history.Add(textAction);
            _undoStack.Push(textAction);
            _redoStack.Clear();
            canvasPanel.Invalidate();
            await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_SHAPE, textAction);
        }

        private void CancelTextEdit()
        {
            _textEditActive = false;
            // Editing existing text — restore the original locally; peers were never told it was removed.
            if (_editingOriginal != null)
            {
                _history.Add(_editingOriginal);
                _undoStack.Push(_editingOriginal);
                RedrawCanvas();
            }
            _editingActionId = null;
            _editingOriginal = null;
            _editText = "";
            canvasPanel.Invalidate();
        }

        // Hit-tests text actions in _history (top-down). Returns the topmost text whose
        // measured rect contains the canvas point, or null.
        private DrawAction HitTestText(Common.PointF canvasPt)
        {
            if (_graphics == null) return null;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                var a = _history[i];
                if (a?.Type == null || !a.Type.StartsWith("text:")) continue;
                if (a.Points == null || a.Points.Count == 0) continue;
                string content = a.Type.Substring(5);
                if (string.IsNullOrEmpty(content)) continue;
                using (var font = new Font("Arial", Math.Max(1f, a.Thickness), FontStyle.Regular, GraphicsUnit.Point))
                {
                    SizeF sz = _graphics.MeasureString(content, font);
                    var rect = new RectangleF(a.Points[0].X, a.Points[0].Y, sz.Width, sz.Height);
                    if (rect.Contains(canvasPt.X, canvasPt.Y)) return a;
                }
            }
            return null;
        }

        // ── Shape & Brush Utilities ─────────────────────────────────────
        private bool IsShapeTool(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return false;
            return tool == "rectangle" || tool == "circle" || tool == "line"
                || tool == "triangle" || tool.StartsWith("arrow");
        }
        //brush-size
        private void tscbSize_TextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(tscbSize.Text, out int newSize))
            {
                _thickness = newSize;
            }
        }

        private void btnUndo_Click(object sender, EventArgs e)
        {

        }

        private void btnCopyCode_Click(object sender, EventArgs e)
        {

        }

        private void btnAttachFile_Click(object sender, EventArgs e)
        {

        }

        private void btnTriangle_Click(object sender, EventArgs e)
        {

        }
    }
}
