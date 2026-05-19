namespace CanvasApp.Client.Drawing.Recognition
{
    // Centralises every magic number used by the recognition pipeline.
    // Tuned for mouse input on a 1080p panel; touch input typically wants
    // larger MinStrokeLength and looser tolerances.
    public sealed class RecognitionConfig
    {
        // Minimum perimeter a stroke must reach before we even try to recognise it.
        // Below this the user probably just tapped or made a tiny correction.
        public float MinStrokeLength { get; set; } = 30f;

        public int MinPointCount { get; set; } = 8;

        // A shape under this overall confidence is treated as "no match".
        public float MinConfidence { get; set; } = 0.75f;

        // Closed-curve test: dist(first, last) / perimeter must be below this
        // for the stroke to be considered a closed contour (circle/ellipse/rect).
        public float ClosednessThreshold { get; set; } = 0.20f;

        // Allowed deviation of a polygon corner from 90 degrees (in degrees).
        public float RectAngleTolerance { get; set; } = 15f;

        // RDP tolerance as a fraction of the stroke's bounding-box diagonal.
        public float RdpEpsilonRatio { get; set; } = 0.05f;

        // Circle test: standard deviation of radius / mean radius must be below this.
        public float CircleRadiusVariance { get; set; } = 0.18f;

        // For circle vs ellipse: if bbox aspect ratio is within +/- this many percent
        // of 1.0 we prefer the circle interpretation (cleaner final shape).
        public float CircleAspectTolerance { get; set; } = 0.20f;

        // Ellipse test: average normalised residual from the ellipse equation.
        public float EllipseResidualThreshold { get; set; } = 0.25f;

        // Chaikin smoothing passes applied to the raw stroke before analysis.
        // Two passes is enough to kill quantisation jitter without rounding corners.
        public int SmoothingPasses { get; set; } = 2;

        // Two points within this many canvas units of each other are dropped
        // during collection (decimation). Improves analysis speed.
        public float MinPointSpacing { get; set; } = 1.5f;

        public static RecognitionConfig Default => new RecognitionConfig();
    }
}
