using System;
using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Recognition
{
    // A clean circle has every point equidistant from the centroid.
    // We measure that with the coefficient of variation of the radii:
    // CV = std(r) / mean(r). Below ~0.18 looks circular to the eye.
    //
    // We also reject obviously elongated strokes by checking bounding-box
    // aspect ratio, otherwise a clean ellipse would falsely register as a
    // circle with a too-large radius.
    public sealed class CircleDetector : IShapeDetector
    {
        public RecognitionResult Detect(IList<PointF> stroke, RecognitionConfig config)
        {
            if (stroke.Count < config.MinPointCount) return RecognitionResult.None;
            if (StrokeAnalysis.Closedness(stroke) >= config.ClosednessThreshold) return RecognitionResult.None;

            var bounds = StrokeAnalysis.BoundingBox(stroke);
            if (bounds.Width < 1f || bounds.Height < 1f) return RecognitionResult.None;

            float aspect = bounds.Width / bounds.Height;
            if (aspect < 1f) aspect = 1f / aspect;
            if (aspect > 1f + config.CircleAspectTolerance) return RecognitionResult.None;

            // Use bounding-box centre rather than centroid: the centroid is biased
            // by how the stroke was drawn (slow regions accumulate more points).
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
            if (variance < 0) variance = 0;
            double cv = Math.Sqrt(variance) / Math.Max(mean, 1e-6);

            if (cv > config.CircleRadiusVariance) return RecognitionResult.None;

            float confidence = 1f - (float)(cv / config.CircleRadiusVariance) * 0.6f;
            return new RecognitionResult(
                ShapeType.Circle,
                confidence,
                RecognizedShape.Circle(center, (float)mean));
        }
    }
}
