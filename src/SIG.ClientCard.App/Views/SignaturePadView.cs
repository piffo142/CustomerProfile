using System.Text;

namespace SIG.ClientCard.App.Views;

/// <summary>
/// Dependency-free signature pad: a GraphicsView collecting strokes, exported
/// as an SVG document. Vector output means no platform bitmap rendering is
/// needed and the signature scales losslessly.
/// </summary>
public sealed class SignaturePadView : GraphicsView
{
    private readonly List<List<PointF>> _strokes = [];
    private List<PointF>? _current;

    public SignaturePadView()
    {
        Drawable = new SignatureDrawable(_strokes);
        BackgroundColor = Colors.White;

        StartInteraction += (_, e) =>
        {
            _current = [e.Touches[0]];
            _strokes.Add(_current);
            Invalidate();
        };
        DragInteraction += (_, e) =>
        {
            _current?.Add(e.Touches[0]);
            Invalidate();
        };
        EndInteraction += (_, _) => _current = null;
    }

    public bool HasInk => _strokes.Any(s => s.Count > 1);

    public void Clear()
    {
        _strokes.Clear();
        _current = null;
        Invalidate();
    }

    public string ToSvg()
    {
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
          .Append((int)Math.Max(1, Width)).Append(' ').Append((int)Math.Max(1, Height))
          .Append("\"><g fill=\"none\" stroke=\"#1E2A1D\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">");

        foreach (var stroke in _strokes.Where(s => s.Count > 1))
        {
            sb.Append("<polyline points=\"");
            foreach (var point in stroke)
            {
                sb.Append(point.X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(',')
                  .Append(point.Y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(' ');
            }

            sb.Append("\"/>");
        }

        sb.Append("</g></svg>");
        return sb.ToString();
    }

    private sealed class SignatureDrawable(List<List<PointF>> strokes) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.StrokeColor = Color.FromArgb("1E2A1D");
            canvas.StrokeSize = 2;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            foreach (var stroke in strokes)
            {
                for (var i = 1; i < stroke.Count; i++)
                {
                    canvas.DrawLine(stroke[i - 1], stroke[i]);
                }
            }
        }
    }
}
