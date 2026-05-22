using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing.Recognition
{
    public sealed class RecognitionResult
    {
        public ShapeType ShapeType { get; }
        public float Confidence { get; }    // 0..1
        public RecognizedShape Shape { get; }

        public RecognitionResult(ShapeType type, float confidence, RecognizedShape shape)
        {
            ShapeType = type;
            Confidence = confidence;
            Shape = shape;
        }

        public static RecognitionResult None { get; } =
            new RecognitionResult(ShapeType.Unknown, 0f, null);

        public bool IsMatch => ShapeType != ShapeType.Unknown && Shape != null;
    }
}
