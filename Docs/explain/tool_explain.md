# Giải thích các Tool trong bảng vẽ (CanvasApp.Client)

Tài liệu này mô tả từng công cụ (tool) trên thanh công cụ của bảng vẽ: ý tưởng cài đặt, kiến thức/thuật toán nền tảng, và trích dẫn trực tiếp tới code nguồn.

---

## 1. Tổng quan kiến trúc Tool

Toàn bộ tool được quản lý bằng **một biến string** `_currentTool` trong [CanvasForm.cs](CanvasApp.Client/Forms/CanvasForm.cs). Khi người dùng bấm vào một nút trên `ToolStrip`, handler chỉ cần gán lại giá trị này. Logic vẽ trong `Canvas_MouseDown / MouseMove / MouseUp` sẽ rẽ nhánh dựa vào `_currentTool`.

[CanvasForm.cs:30](CanvasApp.Client/Forms/CanvasForm.cs#L30):
```csharp
private string _currentTool = "pen";
```

Phần wiring nút bấm (toolbar) — [CanvasForm.cs:149-264](CanvasApp.Client/Forms/CanvasForm.cs#L149-L264):
```csharp
// Tools
btnPen.Click += (s, e) => _currentTool = "pen";
btnEraser.Click += (s, e) => _currentTool = "eraser";
...
shapeMenu.Items.Add("Hình chữ nhật", null, (s, e) => _currentTool = "rectangle");
shapeMenu.Items.Add("Hình tròn",     null, (s, e) => _currentTool = "circle");
shapeMenu.Items.Add("Hình tam giác", null, (s, e) => _currentTool = "triangle");
...
btnLine.Click += (s, e) => _currentTool = "line";
btnText.Click += (s, e) => _currentTool = "text";
chkFill.Click += (s, e) => _currentTool = "fill";
arrowMenu.Items.Add("Mũi tên đơn",     null, (s, e) => _currentTool = "arrow");
arrowMenu.Items.Add("Mũi tên hai đầu", null, (s, e) => _currentTool = "arrow_double");
arrowMenu.Items.Add("Mũi tên đứt nét", null, (s, e) => _currentTool = "arrow_dashed");
arrowMenu.Items.Add("Mũi tên đậm",     null, (s, e) => _currentTool = "arrow_thick");
```

Mọi nét vẽ đều được đóng gói thành một `DrawAction` và:
1. Đẩy vào `_history` (nguồn chân lý để vẽ lại canvas).
2. Đẩy vào `_undoStack` để hỗ trợ Undo/Redo.
3. Gửi qua mạng tới server bằng `CanvasClient.Instance.SendDrawAsync(...)`.

Kiến thức nền: **Windows Forms + GDI+** (`System.Drawing`, `System.Drawing.Drawing2D`), mô hình **command pattern** (mỗi `DrawAction` là một lệnh có thể phát lại / undo), và pattern **event-driven** (subscribe sự kiện Mouse/Paint).

---

## 2. Pen — Bút vẽ tự do (Freehand)

**Ý tưởng:** Khi giữ chuột trái và kéo, mỗi vị trí mới của chuột được nối với vị trí trước đó bằng một đường thẳng nhỏ. Tổ hợp các đoạn ngắn đó tạo cảm giác là một nét bút liên tục.

**Trích dẫn — MouseDown** [CanvasForm.cs:873-893](CanvasApp.Client/Forms/CanvasForm.cs#L873-L893):
```csharp
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
```

**Trích dẫn — MouseMove** [CanvasForm.cs:929-947](CanvasApp.Client/Forms/CanvasForm.cs#L929-L947):
```csharp
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
```

Mỗi đoạn được vẽ bằng GDI+ `Graphics.DrawLine` với `Pen.StartCap = Pen.EndCap = LineCap.Round` để tránh hiện tượng "răng cưa" giữa hai đoạn nối nhau — [CanvasForm.cs:1078-1084](CanvasApp.Client/Forms/CanvasForm.cs#L1078-L1084):
```csharp
private void DrawLineLocal(Common.PointF from, Common.PointF to, string colorHex, int thickness)
{
    EnsureCanvasCovers(from.X, from.Y);
    EnsureCanvasCovers(to.X, to.Y);
    using (var pen = new Pen(HexToColor(colorHex), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        _graphics.DrawLine(pen, from.X, from.Y, to.X, to.Y);
}
```

**Kiến thức dùng:**
- GDI+ raster drawing (`Graphics`, `Pen`, `Bitmap`).
- Sampling sự kiện chuột không liên tục → xấp xỉ một đường cong bằng *polyline* (chuỗi đoạn thẳng).
- `LineCap.Round` cho lý do thẩm mỹ (giảm aliasing tại điểm nối).
- Mô hình *streaming*: mỗi `DRAW_MOVE` gửi từng đoạn để các peer khác render real-time.

---

## 3. Eraser — Tẩy

**Ý tưởng:** Eraser dùng đúng cơ chế của pen nhưng "vẽ" pixel trong suốt vào bitmap (chế độ `SourceCopy`), nên các pixel cũ bị thay thế thay vì pha trộn.

**Trích dẫn** [CanvasForm.cs:1086-1096](CanvasApp.Client/Forms/CanvasForm.cs#L1086-L1096):
```csharp
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
```

Thêm vào đó, `_thickness * 2` được dùng cho eraser để vùng tẩy rộng hơn nét bút thường ([CanvasForm.cs:880](CanvasApp.Client/Forms/CanvasForm.cs#L880)):
```csharp
int currentThickness = _currentTool == "eraser" ? _thickness * 2 : _thickness;
```

**Kiến thức dùng:** Alpha compositing — `CompositingMode.SourceCopy` ghi đè kênh alpha (so với mặc định `SourceOver` blend). Đây là kỹ thuật chuẩn của GDI+ để "xoá" pixel trên bitmap nền trong suốt.

---

## 4. Shape Tools — Hình chữ nhật / Tròn / Tam giác / Đường thẳng / Mũi tên

**Ý tưởng chung:** Khi `_currentTool` thuộc nhóm shape ([CanvasForm.cs:1790-1795](CanvasApp.Client/Forms/CanvasForm.cs#L1790-L1795)):
```csharp
private bool IsShapeTool(string tool)
{
    if (string.IsNullOrEmpty(tool)) return false;
    return tool == "rectangle" || tool == "circle" || tool == "line"
        || tool == "triangle" || tool.StartsWith("arrow");
}
```
hệ thống chỉ lưu **2 điểm**: điểm nhấn xuống và điểm thả ra. Trong khi kéo, chương trình vẽ "preview" overlay (không ghi vào bitmap); chỉ khi `MouseUp`, hình mới được commit vào bitmap thật và broadcast.

**Preview khi kéo chuột** — vẽ trực tiếp lên `Graphics` của panel mỗi `Paint`, [CanvasForm.cs:519-523](CanvasApp.Client/Forms/CanvasForm.cs#L519-L523):
```csharp
if (_isDrawing && _currentStroke != null && IsShapeTool(_currentTool))
{
    DrawShape(g, _currentStroke.Type, _lastPoint, _currentPoint, _currentStroke.Color, _currentStroke.Thickness);
}
```

**Commit khi thả chuột** — [CanvasForm.cs:979-990](CanvasApp.Client/Forms/CanvasForm.cs#L979-L990):
```csharp
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
```

**Hàm dispatch `DrawShape`** — [CanvasForm.cs:590-651](CanvasApp.Client/Forms/CanvasForm.cs#L590-L651):
```csharp
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
```

**Kiến thức dùng:**
- **Bounding box** từ hai điểm `p1`, `p2`: `x = min(p1.X, p2.X), width = |p1.X − p2.X|`. Cho phép người dùng kéo theo bất kỳ hướng nào.
- API GDI+: `DrawRectangle / FillRectangle`, `DrawEllipse / FillEllipse`, `DrawPolygon / FillPolygon`, `DrawLine`.
- Tam giác cân: đỉnh trên đặt tại trung điểm cạnh trên (`(p1.X + p2.X) / 2`), hai đáy ở hai góc dưới.
- Mũi tên: dùng `AdjustableArrowCap` (lớp có sẵn trong `System.Drawing.Drawing2D`) làm "đầu mũi tên". Biến thể *dashed* dùng `DashStyle.Dash`, biến thể *thick* dùng `filled = true` của arrow cap.
- Chuỗi `_fill` ở đuôi `Type` cho phép một enum string nhúng cả "có fill hay không" — bật bằng checkbox `chkFill`.

---

## 5. Fill / Flood Fill — Đổ màu

**Ý tưởng:** Đổ màu toàn bộ vùng các pixel liền kề có cùng màu với pixel được click. Đây là bài toán *flood fill* kinh điển, ở đây dùng BFS hàng đợi (queue) để tránh tràn stack khi vùng đổ rất lớn.

**Khi click chọn pixel nguồn** — [CanvasForm.cs:838-857](CanvasApp.Client/Forms/CanvasForm.cs#L838-L857):
```csharp
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
```

**Thuật toán BFS flood fill** — [CanvasForm.cs:1139-1188](CanvasApp.Client/Forms/CanvasForm.cs#L1139-L1188):
```csharp
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
```

**Kiến thức dùng:**
- **Thuật toán Flood Fill 4-connected** (lan toả 4 hướng N/E/S/W).
- **BFS** với `Queue<int>`: tránh đệ quy có thể tràn stack với vùng tô vài triệu pixel.
- **Mã hoá điểm thành chỉ số 1D**: `idx = y * bw + x`. Giải mã `x = idx % bw, y = idx / bw`. Nhỏ gọn hơn enqueue `Point` struct.
- **Bitmap.LockBits + Marshal.Copy**: truy cập trực tiếp dữ liệu pixel raw (32bpp ARGB) thay vì `GetPixel/SetPixel` chậm hơn ~50 lần.
- So sánh và ghi pixel theo từng kênh `B,G,R,A` (PixelFormat32bppArgb sắp xếp ngược trong bộ nhớ).
- Early-exit khi màu nguồn = màu đích (tránh loop vô tận do điều kiện dừng dựa theo "đổi màu pixel").

---

## 6. Text — Văn bản

**Ý tưởng:** Khi chọn tool `text` và click vào canvas, hiển thị một caret (con trỏ nhấp nháy) tại vị trí đó; người dùng gõ bàn phím, text hiện ngay khi gõ; khi click ra ngoài / bấm Enter, text được commit vào bitmap như một `DrawAction` kiểu `text:<nội-dung>`. Nếu click trúng một text đã có, vào chế độ chỉnh sửa lại text đó.

**Bắt đầu chỉnh sửa text** — [CanvasForm.cs:802-836](CanvasApp.Client/Forms/CanvasForm.cs#L802-L836):
```csharp
if (_currentTool == "text")
{
    var clickPt = ScreenToCanvas(e.X, e.Y);
    var existing = HitTestText(clickPt);
    if (existing != null)
    {
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
```

**Render preview với caret nhấp nháy** — [CanvasForm.cs:525-547](CanvasApp.Client/Forms/CanvasForm.cs#L525-L547):
```csharp
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
```

**Caret nhấp nháy** — dùng `Timer` interval 530ms (đúng chu kỳ caret hệ điều hành), [CanvasForm.cs:116-122](CanvasApp.Client/Forms/CanvasForm.cs#L116-L122):
```csharp
_cursorTimer = new System.Windows.Forms.Timer { Interval = 530 };
_cursorTimer.Tick += (s, ev) =>
{
    _cursorVisible = !_cursorVisible;
    if (_textEditActive) canvasPanel.Invalidate();
};
_cursorTimer.Start();
```

**Vẽ text khi commit vào bitmap** — [CanvasForm.cs:1044-1052](CanvasApp.Client/Forms/CanvasForm.cs#L1044-L1052):
```csharp
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
```

**Kiến thức dùng:**
- `Graphics.DrawString` + `Font` (GDI+).
- `Graphics.MeasureString` để xác định vị trí đặt caret (sau ký tự cuối).
- Encoding text trong `Type` (prefix `text:`) — đơn giản, không cần thay đổi schema `DrawAction`.
- `StringFormat.GenericTypographic` để đo width chính xác (loại bỏ padding mặc định).
- Bàn phím được lắng nghe qua `KeyPreview = true` để form bắt key ngay cả khi không có TextBox focus.

---

## 7. Smart Shape Recognition — Tool Pen "thông minh"

Đây là phần phức tạp nhất. Khi chọn Pen và bật `SmartShapeEnabled`, mỗi nét vẽ tay được phân tích sau khi thả chuột: nếu giống một hình cơ bản (line/rectangle/circle/ellipse), một **suggestion overlay** hiện ra cho phép người dùng "Accept" để thay nét tay bằng hình chuẩn, hoặc "Reject" để giữ nguyên.

### 7.1. Cấu trúc tổng quan

[ShapeSuggestionController.cs:13-43](CanvasApp.Client/Drawing/ShapeSuggestionController.cs#L13-L43) là **Facade**: `CanvasForm` chỉ giao tiếp với class này — nó chứa 3 thành phần con:
```csharp
public sealed class ShapeSuggestionController
{
    private readonly StrokeCollector _collector;
    private readonly ShapeRecognizer _recognizer;
    private readonly SuggestionOverlay _overlay;
    ...
}
```

Tích hợp vào `Canvas_MouseDown` — [CanvasForm.cs:863-871](CanvasApp.Client/Forms/CanvasForm.cs#L863-L871):
```csharp
if (_currentTool == "pen" && _suggest.SmartShapeEnabled)
{
    var smartPt = ScreenToCanvas(e.X, e.Y);
    _suggest.OnMouseDown(
        new System.Drawing.PointF(smartPt.X, smartPt.Y),
        _currentColor, _thickness);
    canvasPanel.Invalidate();
    return;
}
```

### 7.2. StrokeCollector — Thu thập điểm + decimation

[StrokeCollector.cs:33-43](CanvasApp.Client/Drawing/StrokeCollector.cs#L33-L43):
```csharp
public void Add(PointF p)
{
    if (!_active) return;
    if (_points.Count > 0)
    {
        var last = _points[_points.Count - 1];
        float dx = p.X - last.X, dy = p.Y - last.Y;
        if (dx * dx + dy * dy < _config.MinPointSpacing * _config.MinPointSpacing) return;
    }
    _points.Add(p);
}
```

**Kiến thức:** *Spatial decimation* — chỉ giữ điểm cách điểm trước ≥ `MinPointSpacing` (1.5 đv). Đảm bảo số điểm "đều" bất chấp tần số mouse-event của OS.

### 7.3. StrokeAnalysis — Toolbox hình học

[StrokeAnalysis.cs](CanvasApp.Client/Drawing/Recognition/StrokeAnalysis.cs) là tập hợp helper *thuần hàm*: `Distance`, `Perimeter`, `BoundingBox`, `Centroid`, `Closedness`, `PerpendicularDistance`, `AngleAt`, `Simplify` (RDP), `Smooth` (Chaikin).

**Smoothing — thuật toán Chaikin's corner-cutting** [StrokeAnalysis.cs:115-134](CanvasApp.Client/Drawing/Recognition/StrokeAnalysis.cs#L115-L134):
```csharp
public static List<PointF> Smooth(IList<PointF> pts, int passes)
{
    var current = new List<PointF>(pts);
    for (int p = 0; p < passes; p++)
    {
        if (current.Count < 3) break;
        var next = new List<PointF>(current.Count * 2);
        next.Add(current[0]);
        for (int i = 0; i < current.Count - 1; i++)
        {
            var a = current[i];
            var b = current[i + 1];
            next.Add(new PointF(0.75f * a.X + 0.25f * b.X, 0.75f * a.Y + 0.25f * b.Y));
            next.Add(new PointF(0.25f * a.X + 0.75f * b.X, 0.25f * a.Y + 0.75f * b.Y));
        }
        next.Add(current[current.Count - 1]);
        current = next;
    }
    return current;
}
```
Mỗi pass thay 1 đoạn `AB` bằng 2 điểm chia tại tỉ lệ 1/4 và 3/4 → đường mềm hơn, khử jitter do tay rung.

**Đơn giản hoá — Ramer–Douglas–Peucker (RDP)** [StrokeAnalysis.cs:67-97](CanvasApp.Client/Drawing/Recognition/StrokeAnalysis.cs#L67-L97):
```csharp
public static List<PointF> Simplify(IList<PointF> pts, float epsilon)
{
    var keep = new bool[pts.Count];
    keep[0] = true;
    keep[pts.Count - 1] = true;
    SimplifyRecursive(pts, 0, pts.Count - 1, epsilon, keep);
    ...
}

private static void SimplifyRecursive(IList<PointF> pts, int first, int last, float epsilon, bool[] keep)
{
    if (last <= first + 1) return;
    float maxDist = 0f;
    int maxIdx = -1;
    var a = pts[first];
    var b = pts[last];
    for (int i = first + 1; i < last; i++)
    {
        float d = PerpendicularDistance(pts[i], a, b);
        if (d > maxDist) { maxDist = d; maxIdx = i; }
    }
    if (maxDist > epsilon && maxIdx >= 0)
    {
        keep[maxIdx] = true;
        SimplifyRecursive(pts, first, maxIdx, epsilon, keep);
        SimplifyRecursive(pts, maxIdx, last, epsilon, keep);
    }
}
```
RDP giữ lại các điểm "cong" quan trọng, bỏ qua những điểm gần thẳng — biến chuỗi vài trăm điểm thành một đa giác vài đỉnh để phân tích nhanh.

**Closedness — kiểm tra nét vẽ kín hay mở** [StrokeAnalysis.cs:46-52](CanvasApp.Client/Drawing/Recognition/StrokeAnalysis.cs#L46-L52):
```csharp
public static float Closedness(IList<PointF> pts)
{
    if (pts.Count < 3) return 1f;
    float perim = Perimeter(pts);
    if (perim < 1f) return 1f;
    return Distance(pts[0], pts[pts.Count - 1]) / perim;
}
```
Tỉ lệ `distance(first, last) / perimeter` — nhỏ → kín (vòng tròn / chữ nhật); lớn → mở (đường thẳng).

### 7.4. Các Detector

Mỗi detector implement interface `IShapeDetector.Detect(stroke, config) → RecognitionResult`. `RecognitionResult` chứa `ShapeType`, `Confidence ∈ [0,1]`, `RecognizedShape`. Detector nào trả về *match* với confidence cao nhất sẽ thắng — [ShapeRecognizer.cs:29-52](CanvasApp.Client/Drawing/Recognition/ShapeRecognizer.cs#L29-L52):
```csharp
public RecognitionResult Recognize(IList<PointF> stroke)
{
    if (stroke == null || stroke.Count < _config.MinPointCount) return RecognitionResult.None;
    if (StrokeAnalysis.Perimeter(stroke) < _config.MinStrokeLength) return RecognitionResult.None;

    var smoothed = StrokeAnalysis.Smooth(stroke, _config.SmoothingPasses);

    RecognitionResult best = RecognitionResult.None;
    foreach (var detector in _detectors)
    {
        var result = detector.Detect(smoothed, _config);
        if (!result.IsMatch) continue;
        if (result.Confidence > best.Confidence) best = result;
    }

    if (best.Confidence < _config.MinConfidence) return RecognitionResult.None;
    return best;
}
```

#### a) LineDetector — Đo độ lệch trung bình so với chord

[LineDetector.cs:14-41](CanvasApp.Client/Drawing/Recognition/LineDetector.cs#L14-L41):
```csharp
var a = stroke[0];
var b = stroke[stroke.Count - 1];
float chord = StrokeAnalysis.Distance(a, b);
...
double sum = 0;
for (int i = 1; i < stroke.Count - 1; i++)
    sum += StrokeAnalysis.PerpendicularDistance(stroke[i], a, b);
float avgDev = (float)(sum / Math.Max(1, stroke.Count - 2));
float normalised = avgDev / chord;

float confidence = 1f - (normalised / 0.15f);
```
**Kiến thức:** mỗi điểm tính khoảng cách vuông góc tới đường nối 2 đầu mút; trung bình hoá rồi chuẩn hoá theo chiều dài chord. Đường thẳng "sạch" có `normalised < 0.02`.

#### b) RectangleDetector — RDP + góc 90°

[RectangleDetector.cs:17-61](CanvasApp.Client/Drawing/Recognition/RectangleDetector.cs#L17-L61):
```csharp
float closed = StrokeAnalysis.Closedness(stroke);
if (closed >= config.ClosednessThreshold) return RecognitionResult.None;

var bounds = StrokeAnalysis.BoundingBox(stroke);
float diag = (float)Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
...
float epsilon = diag * config.RdpEpsilonRatio;
var simplified = StrokeAnalysis.Simplify(stroke, epsilon);
...
if (simplified.Count < 4 || simplified.Count > 6) return RecognitionResult.None;
while (simplified.Count > 4) RemoveShortestEdge(simplified);

int n = simplified.Count;
float maxDeviation = 0f;
for (int i = 0; i < n; i++)
{
    var prev = simplified[(i - 1 + n) % n];
    var curr = simplified[i];
    var next = simplified[(i + 1) % n];
    float angle = StrokeAnalysis.AngleAt(prev, curr, next);
    float dev = Math.Abs(angle - 90f);
    if (dev > maxDeviation) maxDeviation = dev;
}

if (maxDeviation > config.RectAngleTolerance) return RecognitionResult.None;
float confidence = 1f - (maxDeviation / config.RectAngleTolerance) * 0.5f;
```
**Kiến thức:**
- RDP rút gọn nét tay thành ~4–6 đỉnh.
- Nếu thừa đỉnh → gộp cạnh ngắn nhất thành trung điểm (`RemoveShortestEdge`).
- Đo từng góc nội bằng `AngleAt` (dot-product cosine), tất cả phải gần 90°.

#### c) CircleDetector — Coefficient of variation của bán kính

[CircleDetector.cs:17-53](CanvasApp.Client/Drawing/Recognition/CircleDetector.cs#L17-L53):
```csharp
var bounds = StrokeAnalysis.BoundingBox(stroke);
float aspect = bounds.Width / bounds.Height;
if (aspect < 1f) aspect = 1f / aspect;
if (aspect > 1f + config.CircleAspectTolerance) return RecognitionResult.None;

var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);

double sum = 0, sumSq = 0;
foreach (var p in stroke)
{
    float r = StrokeAnalysis.Distance(p, center);
    sum += r;
    sumSq += r * r;
}
int n = stroke.Count;
double mean = sum / n;
double variance = (sumSq / n) - mean * mean;
double cv = Math.Sqrt(variance) / Math.Max(mean, 1e-6);

if (cv > config.CircleRadiusVariance) return RecognitionResult.None;
float confidence = 1f - (float)(cv / config.CircleRadiusVariance) * 0.6f;
```
**Kiến thức:**
- **CV (Coefficient of Variation)** = `std(r) / mean(r)`: đường tròn lý tưởng có CV → 0.
- **Variance shortcut** = `E[r²] − (E[r])²` cho phép tính 1 vòng O(n) (không cần lưu r).
- Loại bỏ các nét bị "dài/dẹt" trước bằng aspect-ratio bounding box.
- Lấy *tâm* từ bbox center thay vì centroid để tránh thiên lệch khi tay vẽ chậm tại 1 vùng.

#### d) EllipseDetector — Residual của phương trình ellipse

[EllipseDetector.cs:14-39](CanvasApp.Client/Drawing/Recognition/EllipseDetector.cs#L14-L39):
```csharp
var bounds = StrokeAnalysis.BoundingBox(stroke);
float cx = bounds.Left + bounds.Width / 2f;
float cy = bounds.Top + bounds.Height / 2f;
float a = bounds.Width / 2f;
float b = bounds.Height / 2f;

double sum = 0;
foreach (var p in stroke)
{
    float nx = (p.X - cx) / a;
    float ny = (p.Y - cy) / b;
    sum += Math.Abs(nx * nx + ny * ny - 1.0);
}
float avgResidual = (float)(sum / stroke.Count);
```
**Kiến thức:** với ellipse `(x-cx)²/a² + (y-cy)²/b² = 1`, điểm nằm chính xác trên đường có giá trị = 1. Đo `|nx² + ny² − 1|` trung bình → residual nhỏ → ellipse.

### 7.5. Tuning — `RecognitionConfig`

[RecognitionConfig.cs](CanvasApp.Client/Drawing/Recognition/RecognitionConfig.cs) tập trung mọi "magic number". Đáng chú ý:
- `MinConfidence = 0.75f` — ngưỡng "match" tổng thể.
- `ClosednessThreshold = 0.20f` — đường được coi là kín nếu `dist(first,last) < 20% perimeter`.
- `RectAngleTolerance = 15°` — sai số góc 90° cho phép.
- `CircleRadiusVariance = 0.18f` — CV bán kính cho phép.
- `SmoothingPasses = 2` — số lần Chaikin smoothing trước phân tích.

### 7.6. Overlay & flow Accept/Reject

[SuggestionOverlay.cs:39-71](CanvasApp.Client/Drawing/SuggestionOverlay.cs#L39-L71) vẽ:
1. *Ghost* nét tay người dùng (mờ).
2. Hình chuẩn được nhận diện (đậm hơn).
3. Hai nút "Accept (Enter)" / "Reject (Esc)" được anchor dưới bounding box hình.

[ShapeSuggestionController.cs:96-115](CanvasApp.Client/Drawing/ShapeSuggestionController.cs#L96-L115):
```csharp
public void AcceptSuggestion()
{
    if (!_overlay.Visible) return;
    var result = _pendingStroke != null
        ? _recognizer.Recognize(_pendingStroke)
        : RecognitionResult.None;
    _overlay.Hide();
    if (result.IsMatch) CommitShape?.Invoke(result.Shape, _color, _thickness);
    _pendingStroke = null;
}

public void RejectSuggestion()
{
    if (!_overlay.Visible) return;
    _overlay.Hide();
    if (_pendingStroke != null) CommitFreehand?.Invoke(_pendingStroke, _color, _thickness);
    _pendingStroke = null;
}
```

**Kiến thức tổng kết Smart Shape:**
- *Strategy pattern* (mỗi detector là một strategy), *Facade pattern* (`ShapeSuggestionController`).
- Hình học giải tích: chord/perpendicular distance, bounding box, centroid.
- Thống kê: mean, variance, coefficient of variation.
- Đại số tuyến tính cơ bản: cosine angle qua dot product.
- Thuật toán polyline: Chaikin smoothing, Ramer-Douglas-Peucker simplification.
- UX feedback: ghost stroke + confirm/reject overlay (lấy cảm hứng từ Excalidraw / Apple Notes).

---

## 8. Image Import / Select — Chèn & chỉnh sửa ảnh

**Ý tưởng:** Ảnh không được rasterize thẳng vào `_bitmap` (vì khi đó không thể di chuyển nữa). Mỗi ảnh là một object `CanvasImage` trong dictionary `_imagesById`, được vẽ lại mỗi frame ở trên bitmap nền.

[CanvasForm.Images.cs:17-36](CanvasApp.Client/Forms/CanvasForm.Images.cs#L17-L36):
```csharp
private class CanvasImage
{
    public string Id;
    public Image Image;
    public float X, Y, Width, Height;
    public RectangleF Bounds => new RectangleF(X, Y, Width, Height);
}

private enum ResizeHandle { None, Move, NW, N, NE, E, SE, S, SW, W }

private readonly Dictionary<string, CanvasImage> _imagesById = new Dictionary<string, CanvasImage>();
private readonly List<string> _imageOrder = new List<string>();
```

Tool `select` cho phép kéo / resize / xoá. Khi `MouseDown` trên một handle (góc/cạnh), `_activeHandle` lưu loại handle để `MouseMove` biết phải resize hướng nào.

[CanvasForm.Images.cs:53](CanvasApp.Client/Forms/CanvasForm.Images.cs#L53):
```csharp
_btnSelect.Click += (s, e) => _currentTool = "select";
```

**Kiến thức dùng:**
- *Scene graph 2D* nhỏ — bitmap nền + layer object ảnh.
- Hit-testing hình chữ nhật (`RectangleF.Contains`).
- Bounding box handles (NW/N/NE/E/SE/S/SW/W) — pattern phổ biến trong các editor đồ hoạ.
- Limit kích thước file (`MaxImageBytes = 3 MB`) — đồng bộ với `MaxPayloadBytes` server (4 MB sau khi base64).

---

## 9. Undo / Redo

**Ý tưởng:** Mỗi `DrawAction` được push lên `_undoStack` khi commit. `Undo` pop ra, gỡ khỏi `_history`, redraw toàn bộ canvas, gửi `DRAW_UNDO` cho server kèm `ActionId` để các peer khác cũng gỡ.

[CanvasForm.cs:153-181](CanvasApp.Client/Forms/CanvasForm.cs#L153-L181):
```csharp
btnUndo.Click += async (s, e) =>
{
    if (_undoStack.Count == 0) return;
    var action = _undoStack.Pop();
    _redoStack.Push(action);
    if (!string.IsNullOrEmpty(action.ActionId))
        _history.RemoveAll(a => a.ActionId == action.ActionId);
    RedrawCanvas();
    await CanvasClient.Instance.SendAsync(new Common.Message(MessageType.DRAW_UNDO,
        new UndoNotification { ActionId = action.ActionId }));
};
btnRedo.Click += async (s, e) =>
{
    if (_redoStack.Count == 0) return;
    var action = _redoStack.Pop();
    action.ActionId = NewActionId();
    _undoStack.Push(action);
    _history.Add(action);
    DrawActionLocal(action);
    canvasPanel.Invalidate();
    await CanvasClient.Instance.SendDrawAsync(MessageTypeFor(action), action);
};
```

**Kiến thức dùng:**
- **Command pattern + 2 stack** (`Stack<DrawAction>` undo + redo).
- Khi Redo → gán `ActionId` mới vì id cũ đã bị xoá khỏi history các peer (đảm bảo bất biến: mọi action đang sống đều có id duy nhất).
- *Replay* để render lại canvas (`RedrawCanvas` đi qua toàn bộ `_history` và gọi `DrawActionLocal`).

---

## 10. Zoom / Pan

**Ý tưởng:** Tách *bitmap space* khỏi *screen space*. Mọi điểm chuột đều được convert sang toạ độ canvas, sau đó `Graphics.Transform` áp dụng pan + zoom khi render.

**Quy đổi screen ↔ canvas** — [CanvasForm.cs:1021-1024](CanvasApp.Client/Forms/CanvasForm.cs#L1021-L1024):
```csharp
private Common.PointF ScreenToCanvas(int x, int y)
{
    return new Common.PointF((x - _panOffset.X) / _zoom, (y - _panOffset.Y) / _zoom);
}
```

**Zoom theo wheel + giữ con trỏ làm tâm** — [CanvasForm.cs:1005-1018](CanvasApp.Client/Forms/CanvasForm.cs#L1005-L1018):
```csharp
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
```

**Pan bằng chuột giữa** — [CanvasForm.cs:785-792](CanvasApp.Client/Forms/CanvasForm.cs#L785-L792):
```csharp
if (e.Button == MouseButtons.Middle)
{
    _isPanning = true;
    _lastMousePos = e.Location;
    canvasPanel.Cursor = Cursors.Hand;
    return;
}
```

**Kiến thức dùng:**
- **Affine transformation** (translate + scale).
- Công thức giữ điểm cố định khi zoom: `pan' = mousePos - (mousePos - pan) * (zoomNew / zoomOld)`.
- Anti-flicker: bật `DoubleBuffered` cho `Panel` qua reflection ([CanvasForm.cs:104-108](CanvasApp.Client/Forms/CanvasForm.cs#L104-L108)).

---

## 11. Canvas "vô hạn" — EnsureCanvasCovers

Bitmap nội bộ có biên cố định nhưng khi nét vẽ chạm gần biên, hệ thống **tự cấp phát bitmap lớn hơn**, copy bitmap cũ vào, và shift `_canvasOffsetX/Y` để hệ toạ độ canvas ổn định.

[CanvasForm.cs:1100-1137](CanvasApp.Client/Forms/CanvasForm.cs#L1100-L1137):
```csharp
private void EnsureCanvasCovers(float cx, float cy)
{
    if (_bitmap == null) return;

    const int margin = 200;
    const int grow   = 2000;

    int bx = (int)(cx + _canvasOffsetX);
    int by = (int)(cy + _canvasOffsetY);

    int growLeft   = bx < margin                    ? grow + margin - bx                       : 0;
    int growTop    = by < margin                    ? grow + margin - by                       : 0;
    int growRight  = bx >= _bitmap.Width  - margin  ? grow + bx - _bitmap.Width  + margin + 1  : 0;
    int growBottom = by >= _bitmap.Height - margin  ? grow + by - _bitmap.Height + margin + 1  : 0;

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
```

**Kiến thức dùng:**
- *Dynamic resizing buffer* (kiểu như `List<T>` nhưng cho bitmap 2D).
- Translate transform để toạ độ canvas (có thể âm) ánh xạ đúng vào pixel bitmap luôn ≥ 0.
- `g.Clear(Color.Transparent)` đảm bảo vùng mở rộng trong suốt (không phải đen).

---

## 12. Tóm tắt bảng tool

| Tool | Lưu trữ | Kiến thức chính | Vị trí |
|------|---------|-----------------|--------|
| Pen | polyline (N điểm) | GDI+, polyline approximation | [CanvasForm.cs:1078](CanvasApp.Client/Forms/CanvasForm.cs#L1078) |
| Eraser | polyline + `SourceCopy` | Alpha compositing | [CanvasForm.cs:1086](CanvasApp.Client/Forms/CanvasForm.cs#L1086) |
| Rectangle / Circle / Triangle / Line | 2 điểm | Bounding box, polygon fill | [CanvasForm.cs:590](CanvasApp.Client/Forms/CanvasForm.cs#L590) |
| Arrow (4 biến thể) | 2 điểm + cap | `AdjustableArrowCap`, `DashStyle` | [CanvasForm.cs:627](CanvasApp.Client/Forms/CanvasForm.cs#L627) |
| Fill | 1 điểm seed | Flood Fill BFS, `LockBits` | [CanvasForm.cs:1139](CanvasApp.Client/Forms/CanvasForm.cs#L1139) |
| Text | điểm + chuỗi | `DrawString`, `MeasureString`, caret timer | [CanvasForm.cs:1044](CanvasApp.Client/Forms/CanvasForm.cs#L1044) |
| Smart Shape | polyline → recognize | Chaikin, RDP, CV, residual, dot-product | [ShapeRecognizer.cs](CanvasApp.Client/Drawing/Recognition/ShapeRecognizer.cs) |
| Image Select | overlay objects | Hit-testing, resize handles | [CanvasForm.Images.cs:53](CanvasApp.Client/Forms/CanvasForm.Images.cs#L53) |
| Undo / Redo | 2 stacks | Command pattern, replay | [CanvasForm.cs:153](CanvasApp.Client/Forms/CanvasForm.cs#L153) |
| Zoom / Pan | scale + offset | Affine transform, MouseWheel anchor | [CanvasForm.cs:1005](CanvasApp.Client/Forms/CanvasForm.cs#L1005) |

---

## 13. Các kiến thức nền chung

- **GDI+ / `System.Drawing.Drawing2D`**: `Graphics`, `Pen`, `Brush`, `Bitmap.LockBits`, `AdjustableArrowCap`, `SmoothingMode.AntiAlias`, `CompositingMode.SourceCopy`.
- **Hình học tính toán**: bounding box, perpendicular distance, centroid, dot-product cosine angle.
- **Thuật toán polyline**: Chaikin smoothing, Ramer–Douglas–Peucker.
- **Thuật toán raster**: Flood Fill BFS với queue 1D.
- **Thống kê**: mean, variance, coefficient of variation cho nhận diện circle.
- **Design patterns**: Strategy (detectors), Facade (`ShapeSuggestionController`), Command + 2-stack (undo/redo).
- **Networking**: mỗi action serialize qua `CanvasClient.Instance.SendDrawAsync` → server broadcast → peer apply lại bằng cùng `DrawActionLocal`.
- **Sự kiện Windows Forms**: `MouseDown/Move/Up/Wheel`, `KeyDown/KeyPress`, `Paint`, `Timer`.

Code chia thành 3 lớp rõ ràng:
1. **`CanvasForm.cs`** — UI + dispatcher tool (vẽ cơ bản, undo, zoom).
2. **`CanvasApp.Client.Drawing.Recognition/*`** — module nhận dạng hình thuần hàm.
3. **`CanvasApp.Client.Drawing.Rendering/*`** + `SuggestionOverlay` — render preview overlay riêng biệt.

Việc cô lập các detector sau interface `IShapeDetector` giúp thêm hình mới (ví dụ: hình tam giác, ngôi sao) chỉ cần viết 1 class và thêm vào constructor của `ShapeRecognizer` — không động đến `CanvasForm`.
