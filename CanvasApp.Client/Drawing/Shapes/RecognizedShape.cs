using System.Drawing;

namespace CanvasApp.Client.Drawing.Shapes
{
    // Geometry produced by a detector. Coordinates are always in canvas space
    // (the same space CanvasForm.ScreenToCanvas yields), never pixel space.
    public sealed class RecognizedShape
    {
        public ShapeType Type { get; }
        public PointF Start { get; }      // line endpoint OR rect/ellipse top-left
        public PointF End { get; }        // line endpoint OR rect/ellipse bottom-right
        public PointF Center { get; }     // circle/ellipse center (zero for line/rect)
        public float Radius { get; }      // circle only (zero otherwise)

        private RecognizedShape(ShapeType type, PointF start, PointF end, PointF center, float radius)
        {
            Type = type;
            Start = start;
            End = end;
            Center = center;
            Radius = radius;
        }

        public static RecognizedShape Line(PointF a, PointF b)
            => new RecognizedShape(ShapeType.Line, a, b, PointF.Empty, 0);

        public static RecognizedShape Rectangle(RectangleF r)
            => new RecognizedShape(ShapeType.Rectangle,
                new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom),
                PointF.Empty, 0);

        public static RecognizedShape Circle(PointF center, float radius)
            => new RecognizedShape(ShapeType.Circle,
                new PointF(center.X - radius, center.Y - radius),
                new PointF(center.X + radius, center.Y + radius),
                center, radius);

        public static RecognizedShape Ellipse(RectangleF r)
            => new RecognizedShape(ShapeType.Ellipse,
                new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom),
                new PointF(r.Left + r.Width / 2f, r.Top + r.Height / 2f), 0);

        public RectangleF Bounds => RectangleF.FromLTRB(
            System.Math.Min(Start.X, End.X), System.Math.Min(Start.Y, End.Y),
            System.Math.Max(Start.X, End.X), System.Math.Max(Start.Y, End.Y));

        // Maps to the existing DrawAction.Type string used elsewhere in the client.
        // Keep this in sync with CanvasForm.DrawShape() which dispatches on these strings.
        public string ToDrawActionType()
        {
            switch (Type)
            {
                case ShapeType.Line:      return "line";
                case ShapeType.Rectangle: return "rectangle";
                case ShapeType.Circle:    return "circle";
                case ShapeType.Ellipse:   return "circle"; // existing renderer draws an ellipse from bbox
                default: return "line";
            }
        }
    }
}
