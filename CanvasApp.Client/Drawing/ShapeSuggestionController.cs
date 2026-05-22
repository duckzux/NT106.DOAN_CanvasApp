using System;
using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Recognition;
using CanvasApp.Client.Drawing.Rendering;
using CanvasApp.Client.Drawing.Shapes;

namespace CanvasApp.Client.Drawing
{
    // Facade. CanvasForm only talks to this one type. Mouse events go in,
    // commit events come out, the form decides how to push results to the
    // bitmap and the network.
    public sealed class ShapeSuggestionController
    {
        private readonly StrokeCollector _collector;
        private readonly ShapeRecognizer _recognizer;
        private readonly SuggestionOverlay _overlay;
        private readonly RecognitionConfig _config;

        private Color _color;
        private int _thickness;
        private List<PointF> _pendingStroke; // freehand we are still considering

        // Raised when the user accepts a suggestion. The form commits the
        // clean shape to the bitmap and broadcasts a DRAW_SHAPE.
        public event Action<RecognizedShape, Color, int> CommitShape;

        // Raised when the user rejects (or when recognition failed): the form
        // commits the original freehand stroke and broadcasts it.
        public event Action<List<PointF>, Color, int> CommitFreehand;

        public bool SmartShapeEnabled { get; set; } = true;

        public bool IsDrawing => _collector.IsActive;
        public bool HasPendingSuggestion => _overlay.Visible;

        public ShapeSuggestionController(RecognitionConfig config = null)
        {
            _config = config ?? RecognitionConfig.Default;
            _collector = new StrokeCollector(_config);
            _recognizer = new ShapeRecognizer(_config);
            _overlay = new SuggestionOverlay();
        }

        public void OnMouseDown(PointF canvasPoint, Color color, int thickness)
        {
            // A click that lands on the floating buttons is a decision, not a new stroke.
            if (HasPendingSuggestion)
            {
                if (_overlay.HitTest(canvasPoint, out bool accept))
                {
                    if (accept) AcceptSuggestion();
                    else RejectSuggestion();
                    return;
                }
                // Any other click while a suggestion is open commits the freehand
                // and starts a fresh stroke (matches Excalidraw/Figma behaviour).
                RejectSuggestion();
            }

            _color = color;
            _thickness = thickness;
            _collector.Begin(canvasPoint);
        }

        public void OnMouseMove(PointF canvasPoint)
        {
            _collector.Add(canvasPoint);
        }

        // Returns the recognition result so the form can log it if desired.
        public RecognitionResult OnMouseUp(PointF canvasPoint, float currentZoom)
        {
            if (!_collector.IsActive) return RecognitionResult.None;
            _pendingStroke = _collector.EndAndTake(canvasPoint);

            if (!SmartShapeEnabled)
            {
                CommitFreehand?.Invoke(_pendingStroke, _color, _thickness);
                _pendingStroke = null;
                return RecognitionResult.None;
            }

            var result = _recognizer.Recognize(_pendingStroke);
            if (!result.IsMatch)
            {
                CommitFreehand?.Invoke(_pendingStroke, _color, _thickness);
                _pendingStroke = null;
                return result;
            }

            _overlay.Show(result.Shape, _pendingStroke, _color, _thickness, currentZoom);
            return result;
        }

        public void AcceptSuggestion()
        {
            if (!_overlay.Visible) return;
            // We need a reference to the shape before Hide() clears it.
            // We re-recognise from the cached stroke to obtain it.
            var result = _pendingStroke != null
                ? _recognizer.Recognize(_pendingStroke)
                : RecognitionResult.None;
            _overlay.Hide();
            if (result.IsMatch) CommitShape?.Invoke(result.Shape, _color, _thickness);
            _pendingStroke = null;
        }

        public void RejectSuggestion()
        {
            if (!_overlay.Visible) return;
            _overlay.Hide();
            if (_pendingStroke != null) CommitFreehand?.Invoke(_pendingStroke, _color, _thickness);
            _pendingStroke = null;
        }

        // Cancels any in-progress stroke without committing. Useful when the
        // user switches tools mid-stroke.
        public void CancelStroke()
        {
            _collector.Cancel();
            _overlay.Hide();
            _pendingStroke = null;
        }

        // Draws the live in-progress polyline + the suggestion overlay.
        // The caller must apply pan/zoom transforms before invoking this.
        public void Render(Graphics g, float zoom)
        {
            if (_collector.IsActive)
            {
                ShapeRenderer.DrawPolyline(g,
                    new List<PointF>(_collector.Points),
                    _color, _thickness);
            }
            _overlay.Render(g, zoom);
        }
    }
}
