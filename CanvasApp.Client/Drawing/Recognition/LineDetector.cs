using System;
using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Recognition
{
    // Detects a straight line by measuring how far each point lies from the
    // chord between the first and last point. We use the actual endpoints
    // rather than a least-squares fit because the user's intent is captured
    // better that way (their hand started here and ended there).
    public sealed class LineDetector : IShapeDetector
    {
        public RecognitionResult Detect(IList<PointF> stroke, RecognitionConfig config)
        {
            if (stroke.Count < 2) return RecognitionResult.None;

            var a = stroke[0];
            var b = stroke[stroke.Count - 1];
            float chord = StrokeAnalysis.Distance(a, b);
            if (chord < 1f) return RecognitionResult.None;

            // Average perpendicular deviation, normalised by chord length.
            double sum = 0;
            for (int i = 1; i < stroke.Count - 1; i++)
                sum += StrokeAnalysis.PerpendicularDistance(stroke[i], a, b);
            float avgDev = (float)(sum / Math.Max(1, stroke.Count - 2));
            float normalised = avgDev / chord;

            // Empirically, a clean line has normalised < ~0.02. Anything past 0.15
            // is definitely curved. Map linearly into a confidence score.
            float confidence = 1f - (normalised / 0.15f);
            if (confidence <= 0f) return RecognitionResult.None;
            if (confidence > 1f) confidence = 1f;

            // Bonus: open strokes are more likely lines than circles/rects.
            float closed = StrokeAnalysis.Closedness(stroke);
            if (closed < config.ClosednessThreshold) confidence *= 0.5f;

            return new RecognitionResult(ShapeType.Line, confidence, RecognizedShape.Line(a, b));
        }
    }
}
