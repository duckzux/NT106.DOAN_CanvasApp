using System.Collections.Generic;
using System.Drawing;
using CanvasApp.Client.Drawing.Recognition;

namespace CanvasApp.Client.Drawing
{
    // Captures raw mouse points in canvas space, with built-in spatial
    // decimation: a new point is only kept if it lies at least MinPointSpacing
    // away from the previous one. This keeps point counts predictable across
    // a wide range of mouse hardware and operating-system event rates.
    public sealed class StrokeCollector
    {
        private readonly List<PointF> _points = new List<PointF>(256);
        private readonly RecognitionConfig _config;
        private bool _active;

        public StrokeCollector(RecognitionConfig config = null)
        {
            _config = config ?? RecognitionConfig.Default;
        }

        public bool IsActive => _active;
        public IReadOnlyList<PointF> Points => _points;
        public int Count => _points.Count;

        public void Begin(PointF p)
        {
            _points.Clear();
            _points.Add(p);
            _active = true;
        }

        public void Add(PointF p)
        {
            if (!_active) return;
            if (_points.Count > 0)
            {
                var last = _points[_points.Count - 1];
                float dx = p.X - last.X, dy = p.Y - last.Y;
                if (dx * dx + dy * dy < _config.MinPointSpacing * _config.MinPointSpacing) return;
            }
            _points.Add(p);
        }

        public List<PointF> EndAndTake(PointF p)
        {
            Add(p);
            _active = false;
            var copy = new List<PointF>(_points);
            _points.Clear();
            return copy;
        }

        public void Cancel()
        {
            _active = false;
            _points.Clear();
        }
    }
}
