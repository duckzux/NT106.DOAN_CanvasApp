using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Recognition
{
    // Orchestrates all detectors. Detectors are registered up-front (one
    // instance each, they are stateless), making the system trivially
    // extensible: add a new IShapeDetector to the list and ship.
    public sealed class ShapeRecognizer
    {
        private readonly List<IShapeDetector> _detectors;
        private readonly RecognitionConfig _config;

        public ShapeRecognizer(RecognitionConfig config = null)
        {
            _config = config ?? RecognitionConfig.Default;
            _detectors = new List<IShapeDetector>
            {
                new LineDetector(),
                new RectangleDetector(),
                new CircleDetector(),
                new EllipseDetector()
            };
        }

        public void AddDetector(IShapeDetector detector) => _detectors.Add(detector);

        public RecognitionResult Recognize(IList<PointF> stroke)
        {
            if (stroke == null || stroke.Count < _config.MinPointCount) return RecognitionResult.None;
            if (StrokeAnalysis.Perimeter(stroke) < _config.MinStrokeLength) return RecognitionResult.None;

            // Smooth once before analysis. Detectors operate on the smoothed
            // copy so a shaky hand doesn't tank their confidence scores.
            var smoothed = StrokeAnalysis.Smooth(stroke, _config.SmoothingPasses);

            RecognitionResult best = RecognitionResult.None;
            foreach (var detector in _detectors)
            {
                var result = detector.Detect(smoothed, _config);
                if (!result.IsMatch) continue;
                if (result.Confidence > best.Confidence) best = result;
            }

            // Suppress matches below the global floor.
            if (best.Confidence < _config.MinConfidence) return RecognitionResult.None;

            // Circle wins over ellipse when both fire — the circle is the
            // "cleaner" suggestion for a roughly round drawing.
            return best;
        }
    }
}
