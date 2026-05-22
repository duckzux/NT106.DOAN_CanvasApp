using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Rendering
{
    // Stateless drawing of a RecognizedShape onto any Graphics surface.
    // Keep this in sync with CanvasForm.DrawShape() so the preview matches
    // what gets committed.
    public static class ShapeRenderer
    {
        public static void Draw(Graphics g, Shapes.RecognizedShape shape, Color color, int thickness)
        {
            if (shape == null) return;
            using (var pen = new Pen(color, thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                switch (shape.Type)
                {
                    case ShapeType.Line:
                        g.DrawLine(pen, shape.Start, shape.End);
                        break;
                    case ShapeType.Rectangle:
                    {
                        var r = shape.Bounds;
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        break;
                    }
                    case ShapeType.Circle:
                    case ShapeType.Ellipse:
                    {
                        var r = shape.Bounds;
                        g.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                        break;
                    }
                }
            }
        }

        // Draws the raw user stroke as a soft polyline (used by the ghosted
        // preview while a suggestion is on screen).
        public static void DrawPolyline(Graphics g, IList<PointF> points, Color color, int thickness)
        {
            if (points == null || points.Count < 2) return;
            using (var pen = new Pen(color, thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                var arr = new PointF[points.Count];
                for (int i = 0; i < points.Count; i++) arr[i] = points[i];
                g.DrawLines(pen, arr);
            }
        }
    }
}
