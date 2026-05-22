using System;
using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Recognition
{
    // Fits an axis-aligned ellipse from the stroke's bounding box and
    // measures how well every stroke point satisfies the canonical equation
    //        ((x - cx) / a)^2 + ((y - cy) / b)^2 = 1
    // The mean residual is the score; small => ellipse, large => not.
    public sealed class EllipseDetector : IShapeDetector
    {
        public RecognitionResult Detect(IList<PointF> stroke, RecognitionConfig config)
        {
            if (stroke.Count < config.MinPointCount) return RecognitionResult.None;
            if (StrokeAnalysis.Closedness(stroke) >= config.ClosednessThreshold) return RecognitionResult.None;

            var bounds = StrokeAnalysis.BoundingBox(stroke);
            if (bounds.Width < 2f || bounds.Height < 2f) return RecognitionResult.None;

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
            if (avgResidual > config.EllipseResidualThreshold) return RecognitionResult.None;

            float confidence = 1f - (avgResidual / config.EllipseResidualThreshold) * 0.7f;
            return new RecognitionResult(ShapeType.Ellipse, confidence, RecognizedShape.Ellipse(bounds));
        }
    }
}
