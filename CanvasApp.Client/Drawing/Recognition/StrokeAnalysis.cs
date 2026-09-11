using System;
using System.Collections.Generic;
using System.Drawing;

namespace CanvasApp.Client.Drawing.Recognition
{
    // Pure geometry helpers used by all detectors. No state, no side effects.
    internal static class StrokeAnalysis
    {
        public static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float Perimeter(IList<PointF> pts)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Distance(pts[i - 1], pts[i]);
            return total;
        }

        public static RectangleF BoundingBox(IList<PointF> pts)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        public static PointF Centroid(IList<PointF> pts)
        {
            double sx = 0, sy = 0;
            foreach (var p in pts) { sx += p.X; sy += p.Y; }
            return new PointF((float)(sx / pts.Count), (float)(sy / pts.Count));
        }

        // Closedness = gap between endpoints relative to perimeter.
        // Returns a ratio: small => closed, large => open.
        public static float Closedness(IList<PointF> pts)
        {
            if (pts.Count < 3) return 1f;
            float perim = Perimeter(pts);
            if (perim < 1f) return 1f;
            return Distance(pts[0], pts[pts.Count - 1]) / perim;
        }

        // Perpendicular distance from point p to the infinite line through a-b.
        public static float PerpendicularDistance(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-6f) return Distance(p, a);
            float cross = Math.Abs(dy * p.X - dx * p.Y + b.X * a.Y - b.Y * a.X);
            return cross / (float)Math.Sqrt(len2);
        }

        // Ramer-Douglas-Peucker polygon simplification.
        // Recursively keeps the point furthest from the current segment if its
        // perpendicular distance exceeds epsilon. O(n log n) average.
        public static List<PointF> Simplify(IList<PointF> pts, float epsilon)
        {
            var keep = new bool[pts.Count];
            keep[0] = true;
            keep[pts.Count - 1] = true;
            SimplifyRecursive(pts, 0, pts.Count - 1, epsilon, keep);

            var result = new List<PointF>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
            return result;
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

        // Interior angle at vertex b of the polyline a-b-c, in degrees [0, 180].
        public static float AngleAt(PointF a, PointF b, PointF c)
        {
            float v1x = a.X - b.X, v1y = a.Y - b.Y;
            float v2x = c.X - b.X, v2y = c.Y - b.Y;
            float dot = v1x * v2x + v1y * v2y;
            float m1 = (float)Math.Sqrt(v1x * v1x + v1y * v1y);
            float m2 = (float)Math.Sqrt(v2x * v2x + v2y * v2y);
            if (m1 < 1e-6f || m2 < 1e-6f) return 0f;
            float cos = dot / (m1 * m2);
            if (cos > 1f) cos = 1f; else if (cos < -1f) cos = -1f;
            return (float)(Math.Acos(cos) * 180.0 / Math.PI);
        }

        // Chaikin's corner-cutting smoothing. Each pass replaces every segment
        // with two new points at 1/4 and 3/4 along it. Closed strokes wrap around.
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
    }
}
