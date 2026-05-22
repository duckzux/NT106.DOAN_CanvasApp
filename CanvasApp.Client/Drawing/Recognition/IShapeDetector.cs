using System.Collections.Generic;
using System.Drawing;

namespace CanvasApp.Client.Drawing.Recognition
{
    // Implement one detector per shape family. Detectors must be pure:
    // no shared state, safe to call from any thread.
    public interface IShapeDetector
    {
        RecognitionResult Detect(IList<PointF> stroke, RecognitionConfig config);
    }
}
