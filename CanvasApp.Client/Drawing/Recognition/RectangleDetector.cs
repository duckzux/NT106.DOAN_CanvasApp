using System;
using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Recognition
{
    // RDP-simplifies the stroke down to a polygon. If we end up with 4 or 5
    // dominant vertices (the 5th case is when the stroke closes back at the
    // start point, creating two near-equal corners), and all corner angles
    // are within tolerance of 90 degrees, we call it a rectangle.
    //
    // The clean shape we emit is the axis-aligned bounding box. Rotated
    // rectangles are listed as a future improvement.
    public sealed class RectangleDetector : IShapeDetector
    {
        public RecognitionResult Detect(IList<PointF> stroke, RecognitionConfig config)
        {
            if (stroke.Count < config.MinPointCount) return RecognitionResult.None;

            float closed = StrokeAnalysis.Closedness(stroke);
            if (closed >= config.ClosednessThreshold) return RecognitionResult.None;

            var bounds = StrokeAnalysis.BoundingBox(stroke);
            float diag = (float)Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
            if (diag < 1f) return RecognitionResult.None;

            float epsilon = diag * config.RdpEpsilonRatio;
            var simplified = StrokeAnalysis.Simplify(stroke, epsilon);

            // Drop the duplicate closing vertex if present (first ~ last).
            if (simplified.Count >= 2 &&
                StrokeAnalysis.Distance(simplified[0], simplified[simplified.Count - 1]) < epsilon)
            {
                simplified.RemoveAt(simplified.Count - 1);
            }

            if (simplified.Count < 4 || simplified.Count > 6) return RecognitionResult.None;

            // If we have 5-6 vertices, collapse the two closest neighbours until we have 4.
            while (simplified.Count > 4) RemoveShortestEdge(simplified);

            // Check that all four interior angles are near 90 degrees.
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

            // Confidence inversely proportional to corner deviation.
            float confidence = 1f - (maxDeviation / config.RectAngleTolerance) * 0.5f;
            return new RecognitionResult(ShapeType.Rectangle, confidence, RecognizedShape.Rectangle(bounds));
        }

        private static void RemoveShortestEdge(List<PointF> verts)
        {
            int n = verts.Count;
            if (n < 2) return;

            int shortestIdx = 0;
            float shortest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d = StrokeAnalysis.Distance(verts[i], verts[(i + 1) % n]);
                if (d < shortest) { shortest = d; shortestIdx = i; }
            }

            // Merge edge endpoints into their midpoint. j is the wrap-aware
            // "next" index, so it may be 0 when shortestIdx == n-1.
            int j = (shortestIdx + 1) % n;
            var mid = new PointF(
                (verts[shortestIdx].X + verts[j].X) / 2f,
                (verts[shortestIdx].Y + verts[j].Y) / 2f);
            verts[shortestIdx] = mid;

            // Always remove verts[j]. Safe whether j = shortestIdx + 1
            // (mid keeps its index) or j = 0 (mid shifts to verts.Count-1
            // after the removal, which is still a valid vertex of the
            // now-smaller polygon).
            verts.RemoveAt(j);
        }
    }
}
