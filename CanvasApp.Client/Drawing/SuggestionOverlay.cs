using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using CanvasApp.Client.Drawing.Rendering;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing
{
    // Visual layer drawn on top of the canvas while a suggestion is pending.
    // It owns:
    //   - the semi-transparent ghost of the user's original stroke
    //   - the semi-transparent corrected shape
    //   - two floating buttons (accept / reject) anchored to the shape's bbox
    //
    // The overlay is rendered in canvas space, so it scales correctly with the
    // existing zoom/pan transform applied in CanvasPanel_Paint.
    public sealed class SuggestionOverlay
    {
        // Accept/reject buttons in canvas space. The controller hit-tests against
        // these on MouseDown to decide which (if any) was clicked.
        public RectangleF AcceptButton { get; private set; }
        public RectangleF RejectButton { get; private set; }

        public bool Visible { get; private set; }

        private RecognizedShape _shape;
        private System.Collections.Generic.IList<PointF> _ghostStroke;
        private Color _strokeColor;
        private int _strokeThickness;

        // Returned ratio drives a fade-in animation; CanvasForm can advance
        // this by invalidating on a tick. Range [0,1].
        public float Alpha { get; private set; } = 1f;

        // 28 canvas-units high — large enough to click without pixel-hunting.
        private const float BtnHeight = 28f;
        private const float BtnGap = 6f;

        public void Show(RecognizedShape shape, System.Collections.Generic.IList<PointF> ghost,
                         Color color, int thickness, float zoom)
        {
            _shape = shape;
            _ghostStroke = ghost;
            _strokeColor = color;
            _strokeThickness = thickness;
            LayoutButtons(zoom);
            Visible = true;
            Alpha = 1f;
        }

        public void Hide()
        {
            Visible = false;
            _shape = null;
            _ghostStroke = null;
        }

        private void LayoutButtons(float zoom)
        {
            var b = _shape.Bounds;
            // Button width scales with zoom so it stays a consistent pixel size on screen.
            float btnW = 92f / Math.Max(0.4f, zoom);
            float btnH = BtnHeight / Math.Max(0.4f, zoom);
            float gap = BtnGap / Math.Max(0.4f, zoom);

            float anchorX = b.Right - (btnW * 2 + gap);
            float anchorY = b.Bottom + gap * 2;

            RejectButton = new RectangleF(anchorX, anchorY, btnW, btnH);
            AcceptButton = new RectangleF(anchorX + btnW + gap, anchorY, btnW, btnH);
        }

        public bool HitTest(PointF canvasPos, out bool accept)
        {
            accept = false;
            if (!Visible) return false;
            if (AcceptButton.Contains(canvasPos)) { accept = true; return true; }
            if (RejectButton.Contains(canvasPos)) { accept = false; return true; }
            return false;
        }

        public void Render(Graphics g, float zoom)
        {
            if (!Visible || _shape == null) return;

            int alpha = (int)(Alpha * 255);
            int ghostAlpha = (int)(Alpha * 90);

            // Ghost of the user's original stroke — kept visible per the spec.
            if (_ghostStroke != null)
            {
                ShapeRenderer.DrawPolyline(g, _ghostStroke,
                    Color.FromArgb(ghostAlpha, _strokeColor),
                    _strokeThickness);
            }

            // The corrected shape, rendered slightly thicker so the suggestion
            // reads as the "stronger" option without yet being committed.
            ShapeRenderer.Draw(g, _shape,
                Color.FromArgb(alpha, _strokeColor),
                _strokeThickness);

            DrawButton(g, AcceptButton, "Accept (Enter)", Color.FromArgb(alpha, 56, 142, 60), zoom);
            DrawButton(g, RejectButton, "Reject (Esc)",   Color.FromArgb(alpha, 198, 40, 40), zoom);
        }

        private static void DrawButton(Graphics g, RectangleF rect, string label, Color fill, float zoom)
        {
            using (var path = RoundedRect(rect, 6f / Math.Max(0.4f, zoom)))
            using (var brush = new SolidBrush(fill))
            using (var border = new Pen(Color.FromArgb(fill.A, 30, 30, 30), 1f / Math.Max(0.4f, zoom)))
            {
                g.FillPath(brush, path);
                g.DrawPath(border, path);
            }

            float fontSize = Math.Max(7f, 9f / Math.Max(0.5f, zoom));
            using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Point))
            using (var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            using (var textBrush = new SolidBrush(Color.FromArgb(fill.A, Color.White)))
            {
                g.DrawString(label, font, textBrush, rect, fmt);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
