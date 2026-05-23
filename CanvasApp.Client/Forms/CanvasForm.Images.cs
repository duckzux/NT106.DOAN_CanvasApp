using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    // Image overlay system: imported images are NOT rasterized into _bitmap. They live as
    // independent objects in _imagesById and are repainted each frame on top of the bitmap,
    // which is what makes them movable/resizable after placement.
    partial class CanvasForm
    {
        private class CanvasImage
        {
            public string Id;
            public Image Image;
            public float X, Y, Width, Height;
            public RectangleF Bounds => new RectangleF(X, Y, Width, Height);
        }

        private enum ResizeHandle { None, Move, NW, N, NE, E, SE, S, SW, W }

        private readonly Dictionary<string, CanvasImage> _imagesById = new Dictionary<string, CanvasImage>();
        // Back-to-front z-order. Drawn first → bottom; last → top.
        private readonly List<string> _imageOrder = new List<string>();
        private string _selectedImageId;
        private ResizeHandle _activeHandle = ResizeHandle.None;
        private Common.PointF _imgDragStart;
        private RectangleF _imgDragStartBounds;
        private const float MinImageDim = 20f;
        // Cap on raw bytes accepted from disk — base64 expands ~4/3×, server's MaxPayloadBytes is 4 MB.
        private const long MaxImageBytes = 3L * 1024 * 1024;

        private ToolStripButton _btnSelect;

        /// <summary>Wire up the "select" tool button on the toolbar. Call from constructor.</summary>
        private void InitImageSelectButton()
        {
            _btnSelect = new ToolStripButton
            {
                Name = "btnSelect",
                Text = "↖",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = false,
                Size = new Size(43, 28),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ToolTipText = "Chọn ảnh để di chuyển / thay đổi kích thước (Delete để xoá)"
            };
            _btnSelect.Click += (s, e) => _currentTool = "select";
            int insertAt = toolStrip1.Items.IndexOf(btnImportBg);
            if (insertAt < 0) toolStrip1.Items.Add(_btnSelect);
            else toolStrip1.Items.Insert(insertAt, _btnSelect);
        }

        /// <summary>OpenFileDialog → read bytes → place image at viewport center → broadcast.</summary>
        private async System.Threading.Tasks.Task ImportImageFromFile()
        {
            byte[] bytes;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Image Files(*.BMP;*.JPG;*.JPEG;*.PNG)|*.BMP;*.JPG;*.JPEG;*.PNG";
                dlg.Title = "Chèn ảnh vào canvas";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try { bytes = File.ReadAllBytes(dlg.FileName); }
                catch (Exception ex)
                {
                    MessageBox.Show("Không đọc được file: " + ex.Message, "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            if (bytes.Length > MaxImageBytes)
            {
                MessageBox.Show($"Ảnh quá lớn (tối đa {MaxImageBytes / (1024 * 1024)} MB).",
                    "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int srcW, srcH;
            try
            {
                using (var probe = Image.FromStream(new MemoryStream(bytes)))
                { srcW = probe.Width; srcH = probe.Height; }
            }
            catch
            {
                MessageBox.Show("File không phải ảnh hợp lệ.", "Lỗi",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            const float MaxDim = 400f;
            float w, h;
            if (srcW >= srcH) { w = Math.Min(srcW, MaxDim); h = w * srcH / srcW; }
            else              { h = Math.Min(srcH, MaxDim); w = h * srcW / srcH; }

            var center = ScreenToCanvas(canvasPanel.Width / 2, canvasPanel.Height / 2);
            float x = center.X - w / 2;
            float y = center.Y - h / 2;

            var action = new DrawAction
            {
                ActionId = NewActionId(),
                Type = "image",
                Points = new List<Common.PointF>
                {
                    new Common.PointF(x, y),
                    new Common.PointF(x + w, y + h),
                },
                ImageData = Convert.ToBase64String(bytes),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            ApplyImageAction(action);
            _history.Add(action);
            _undoStack.Push(action);
            _redoStack.Clear();
            _selectedImageId = action.ActionId;
            _currentTool = "select";
            canvasPanel.Invalidate();

            await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_IMAGE, action);
        }

        /// <summary>Apply CREATE or TRANSFORM. CREATE requires ImageData; TRANSFORM updates existing bounds.</summary>
        private void ApplyImageAction(DrawAction action)
        {
            if (action == null || string.IsNullOrEmpty(action.ActionId)) return;
            if (action.Points == null || action.Points.Count < 2) return;

            var p0 = action.Points[0];
            var p1 = action.Points[1];

            if (_imagesById.TryGetValue(action.ActionId, out var existing))
            {
                existing.X = p0.X;
                existing.Y = p0.Y;
                existing.Width = p1.X - p0.X;
                existing.Height = p1.Y - p0.Y;
                return;
            }

            if (string.IsNullOrEmpty(action.ImageData)) return; // can't create without bytes

            byte[] bytes;
            try { bytes = Convert.FromBase64String(action.ImageData); }
            catch { return; }

            Image img;
            try { img = Image.FromStream(new MemoryStream(bytes)); }
            catch { return; }

            _imagesById[action.ActionId] = new CanvasImage
            {
                Id = action.ActionId,
                Image = img,
                X = p0.X,
                Y = p0.Y,
                Width = p1.X - p0.X,
                Height = p1.Y - p0.Y,
            };
            _imageOrder.Add(action.ActionId);
        }

        private void RemoveImageLocal(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return;
            if (_imagesById.TryGetValue(imageId, out var img))
            {
                img.Image?.Dispose();
                _imagesById.Remove(imageId);
            }
            _imageOrder.Remove(imageId);
            if (_selectedImageId == imageId) _selectedImageId = null;
        }

        /// <summary>Wipe overlay state. Called from RedrawCanvas before replaying history.</summary>
        private void ResetImagesForRedraw()
        {
            foreach (var img in _imagesById.Values) img.Image?.Dispose();
            _imagesById.Clear();
            _imageOrder.Clear();
            _selectedImageId = null;
            _activeHandle = ResizeHandle.None;
        }

        /// <summary>Returns the topmost image whose bounds contain canvasPt, or null.</summary>
        private CanvasImage HitTestImage(Common.PointF canvasPt)
        {
            for (int i = _imageOrder.Count - 1; i >= 0; i--)
            {
                if (_imagesById.TryGetValue(_imageOrder[i], out var img) &&
                    img.Bounds.Contains(canvasPt.X, canvasPt.Y))
                    return img;
            }
            return null;
        }

        /// <summary>Hit-test the 8 resize handles around the selected image. Returns None if no handle hit.</summary>
        private ResizeHandle HitTestHandle(CanvasImage img, Common.PointF canvasPt)
        {
            // Handle hitbox is a screen-pixel-constant square, expressed in canvas coords by dividing by zoom.
            float r = 8f / Math.Max(_zoom, 0.0001f);
            var b = img.Bounds;
            float[] xs = { b.Left, b.Left + b.Width / 2f, b.Right };
            float[] ys = { b.Top,  b.Top  + b.Height / 2f, b.Bottom };
            ResizeHandle[,] handles =
            {
                { ResizeHandle.NW, ResizeHandle.N,    ResizeHandle.NE },
                { ResizeHandle.W,  ResizeHandle.None, ResizeHandle.E  },
                { ResizeHandle.SW, ResizeHandle.S,    ResizeHandle.SE },
            };
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    var h = handles[row, col];
                    if (h == ResizeHandle.None) continue;
                    if (Math.Abs(canvasPt.X - xs[col]) <= r &&
                        Math.Abs(canvasPt.Y - ys[row]) <= r)
                        return h;
                }
            }
            return ResizeHandle.None;
        }

        /// <summary>Apply a delta (dx, dy in canvas coords) starting from startBounds, per the chosen handle.</summary>
        private static void ApplyTransformDelta(CanvasImage img, ResizeHandle handle,
            RectangleF startBounds, float dx, float dy)
        {
            float x = startBounds.X, y = startBounds.Y;
            float w = startBounds.Width, h = startBounds.Height;
            switch (handle)
            {
                case ResizeHandle.Move: x += dx; y += dy; break;
                case ResizeHandle.NW:   x += dx; y += dy; w -= dx; h -= dy; break;
                case ResizeHandle.N:             y += dy;          h -= dy; break;
                case ResizeHandle.NE:            y += dy; w += dx; h -= dy; break;
                case ResizeHandle.E:                      w += dx;          break;
                case ResizeHandle.SE:                     w += dx; h += dy; break;
                case ResizeHandle.S:                               h += dy; break;
                case ResizeHandle.SW:   x += dx;          w -= dx; h += dy; break;
                case ResizeHandle.W:    x += dx;          w -= dx;          break;
            }
            // Clamp to min size; if a left/top handle pushes past the opposite edge, freeze position.
            if (w < MinImageDim)
            {
                if (handle == ResizeHandle.NW || handle == ResizeHandle.W || handle == ResizeHandle.SW)
                    x = startBounds.Right - MinImageDim;
                w = MinImageDim;
            }
            if (h < MinImageDim)
            {
                if (handle == ResizeHandle.NW || handle == ResizeHandle.N || handle == ResizeHandle.NE)
                    y = startBounds.Bottom - MinImageDim;
                h = MinImageDim;
            }
            img.X = x; img.Y = y; img.Width = w; img.Height = h;
        }

        /// <summary>Paint all imported images followed by selection outline + 8 handles.</summary>
        private void RenderImagesAndHandles(Graphics g)
        {
            foreach (var id in _imageOrder)
            {
                if (!_imagesById.TryGetValue(id, out var ci) || ci.Image == null) continue;
                g.DrawImage(ci.Image, ci.X, ci.Y, ci.Width, ci.Height);
            }

            if (_selectedImageId == null) return;
            if (!_imagesById.TryGetValue(_selectedImageId, out var sel)) return;

            float strokeWidth = Math.Max(0.5f, 1.5f / _zoom);
            float r = 8f / Math.Max(_zoom, 0.0001f);
            var b = sel.Bounds;

            using (var pen = new Pen(Color.DodgerBlue, strokeWidth) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

            float[] xs = { b.Left, b.Left + b.Width / 2f, b.Right };
            float[] ys = { b.Top,  b.Top  + b.Height / 2f, b.Bottom };
            using (var fill = new SolidBrush(Color.White))
            using (var stroke = new Pen(Color.DodgerBlue, strokeWidth))
            {
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        if (row == 1 && col == 1) continue;
                        var rect = new RectangleF(xs[col] - r, ys[row] - r, r * 2, r * 2);
                        g.FillRectangle(fill, rect);
                        g.DrawRectangle(stroke, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                }
            }
        }

        /// <summary>Handle MouseDown when _currentTool=="select". Returns true if the event was consumed.</summary>
        private bool TrySelectToolMouseDown(MouseEventArgs e)
        {
            if (_currentTool != "select" || e.Button != MouseButtons.Left) return false;

            var canvasPos = ScreenToCanvas(e.X, e.Y);

            // First, see if the user grabbed a handle of the currently selected image.
            if (_selectedImageId != null && _imagesById.TryGetValue(_selectedImageId, out var sel))
            {
                var h = HitTestHandle(sel, canvasPos);
                if (h != ResizeHandle.None)
                {
                    _activeHandle = h;
                    _imgDragStart = canvasPos;
                    _imgDragStartBounds = sel.Bounds;
                    return true;
                }
            }

            // Otherwise hit-test against all images (top-down).
            var hit = HitTestImage(canvasPos);
            if (hit != null)
            {
                _selectedImageId = hit.Id;
                _activeHandle = ResizeHandle.Move;
                _imgDragStart = canvasPos;
                _imgDragStartBounds = hit.Bounds;
                // Bring to front for clarity when stacked.
                _imageOrder.Remove(hit.Id);
                _imageOrder.Add(hit.Id);
                canvasPanel.Invalidate();
                return true;
            }

            // Empty area — deselect.
            if (_selectedImageId != null)
            {
                _selectedImageId = null;
                canvasPanel.Invalidate();
            }
            return true; // consume the click so it doesn't trigger pen drawing
        }

        private bool TrySelectToolMouseMove(MouseEventArgs e)
        {
            if (_activeHandle == ResizeHandle.None || _selectedImageId == null) return false;
            if (!_imagesById.TryGetValue(_selectedImageId, out var img)) { _activeHandle = ResizeHandle.None; return false; }

            var canvasPos = ScreenToCanvas(e.X, e.Y);
            float dx = canvasPos.X - _imgDragStart.X;
            float dy = canvasPos.Y - _imgDragStart.Y;
            ApplyTransformDelta(img, _activeHandle, _imgDragStartBounds, dx, dy);
            canvasPanel.Invalidate();
            return true;
        }

        /// <summary>Commit a drag/resize: update history's CREATE entry and broadcast TRANSFORM.</summary>
        private async System.Threading.Tasks.Task<bool> TrySelectToolMouseUpAsync(MouseEventArgs e)
        {
            if (_activeHandle == ResizeHandle.None || _selectedImageId == null) return false;
            var imageId = _selectedImageId;
            _activeHandle = ResizeHandle.None;

            if (!_imagesById.TryGetValue(imageId, out var img)) return true;

            // Keep history's CREATE entry in sync so RedrawCanvas (e.g. after undo of an unrelated
            // action) reproduces the latest position/size.
            var createAction = _history.FirstOrDefault(a => a?.ActionId == imageId && a.Type == "image");
            if (createAction != null && createAction.Points != null && createAction.Points.Count >= 2)
            {
                createAction.Points[0] = new Common.PointF(img.X, img.Y);
                createAction.Points[1] = new Common.PointF(img.X + img.Width, img.Y + img.Height);
            }

            var transform = new DrawAction
            {
                ActionId = imageId,
                Type = "image",
                Points = new List<Common.PointF>
                {
                    new Common.PointF(img.X, img.Y),
                    new Common.PointF(img.X + img.Width, img.Y + img.Height),
                },
            };
            await CanvasClient.Instance.SendDrawAsync(MessageType.DRAW_IMAGE_TRANSFORM, transform);
            return true;
        }

        /// <summary>Delete the selected image locally and notify peers via DRAW_UNDO (reuses the undo channel).</summary>
        private async System.Threading.Tasks.Task DeleteSelectedImageAsync()
        {
            var id = _selectedImageId;
            if (string.IsNullOrEmpty(id)) return;

            RemoveImageLocal(id);
            _history.RemoveAll(a => a.ActionId == id);
            // Rebuild the undo stack without this image's action (preserves order of the others).
            var kept = _undoStack.Where(a => a.ActionId != id).ToArray();
            _undoStack = new Stack<DrawAction>(kept.Reverse());
            canvasPanel.Invalidate();

            await CanvasClient.Instance.SendAsync(new Common.Message(
                MessageType.DRAW_UNDO,
                new UndoNotification { ActionId = id }));
        }
    }
}
