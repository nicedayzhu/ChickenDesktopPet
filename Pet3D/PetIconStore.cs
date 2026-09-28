using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace ChickenDesktopPet3D;

// SVG paths stay vectors at any desktop DPI. This small decoder covers the
// path/shape/transform subset used by the official pet icons, without a web view.
internal sealed class PetIconStore
{
    private readonly Dictionary<string, ImageSource> images = new(StringComparer.Ordinal);
    internal Dictionary<string, string> DecodeErrors { get; } = new(StringComparer.Ordinal);
    public int OfficialIconCount { get; private set; }

    public void Load(PetUiResources resources)
    {
        foreach (var (name, bytes) in resources.Icons)
        {
            try
            {
                using var stream = new MemoryStream(bytes, false);
                var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("Empty SVG");
                var drawing = ReadElement(root, "white", "none");
                if (drawing.Children.Count == 0) throw new InvalidDataException("No SVG paths");
                var box = Numbers((string?)root.Attribute("viewBox"));
                var rect = box.Length == 4 ? new Rect(box[0], box[1], box[2], box[3])
                    : new Rect(0, 0, Number(root, "width", 32), Number(root, "height", 32));
                // A transparent rectangle preserves the source viewBox, including padding.
                var frame = new DrawingGroup { ClipGeometry = new RectangleGeometry(rect) };
                frame.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(rect)));
                frame.Children.Add(drawing);
                var source = new DrawingImage(frame);
                source.Freeze();
                images[name] = source;
            }
            catch (Exception ex)
            {
                var reason = ex.GetType().Name + ": " + (ex.Message.Length > 180 ? ex.Message[^180..] : ex.Message);
                DecodeErrors[name] = reason; ErrorLog.Trace($"SVG icon fallback {name}: {reason}");
            }
        }
        OfficialIconCount = images.Count;
    }

    public Image Create(string name, double size = 26) => new()
    {
        Source = Get(name), Width = size, Height = size, Stretch = Stretch.Uniform,
        IsHitTestVisible = false, SnapsToDevicePixels = true,
    };

    public ImageSource Get(string name)
    {
        if (images.TryGetValue(name, out var cached)) return cached;
        var path = name switch
        {
            "pet_feed" => "M4,23 L28,23 24,28 8,28 Z M9,8 A2,2 0 1 0 13,8 A2,2 0 1 0 9,8 M20,4 A2,2 0 1 0 24,4 A2,2 0 1 0 20,4 M17,16 A2,2 0 1 0 21,16 A2,2 0 1 0 17,16",
            "pet_activity_sleep" => "M19,3 C5,3 0,21 13,28 C23,33 33,22 29,14 C22,23 11,14 19,3 Z",
            "zoom_in" => "M13,3 A10,10 0 1 0 13,23 A10,10 0 1 0 13,3 M20,21 L29,30 M8,13 L18,13 M13,8 L13,18",
            "photo" => "M3,9 L9,9 12,5 21,5 24,9 29,9 29,27 3,27 Z M16,12 A6,6 0 1 0 16,24 A6,6 0 1 0 16,12",
            "edit_label" => "M5,22 L22,5 27,10 10,27 4,28 Z M18,9 L23,14",
            "expand_video" => "M4,12 L4,4 12,4 M20,4 L28,4 28,12 M28,20 L28,28 20,28 M12,28 L4,28 4,20",
            "shrink_video" => "M4,12 L12,12 12,4 M20,4 L20,12 28,12 M28,20 L20,20 20,28 M12,28 L12,20 4,20",
            "close" => "M8,8 L24,24 M24,8 L8,24",
            "left" => "M21,5 L10,16 21,27",
            "right" => "M11,5 L22,16 11,27",
            "reset" => "M8,8 A12,12 0 1 1 4,20 M8,2 L8,10 0,10",
            _ => "M4,16 L16,4 28,16 16,28 Z",
        };
        var geometry = Geometry.Parse(path);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        group.Children.Add(new GeometryDrawing(name is "pet_feed" or "pet_activity_sleep" ? Brushes.White : null,
            new Pen(Brushes.White, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry));
        var image = new DrawingImage(group); image.Freeze(); images[name] = image;
        return image;
    }

    private static DrawingGroup ReadElement(XElement element, string inheritedFill, string inheritedStroke)
    {
        var fill = Attribute(element, "fill", inheritedFill);
        var stroke = Attribute(element, "stroke", inheritedStroke);
        var group = new DrawingGroup { Opacity = Number(element, "opacity", 1) };
        var transform = (string?)element.Attribute("transform");
        if (transform is not null)
        {
            var transforms = new TransformGroup();
            foreach (Match match in Regex.Matches(transform, @"(matrix|translate|scale|rotate)\s*\(([^)]+)\)"))
            {
                var p = Numbers(match.Groups[2].Value);
                Transform? t = match.Groups[1].Value switch
                {
                    "matrix" when p.Length == 6 => new MatrixTransform(new Matrix(p[0], p[1], p[2], p[3], p[4], p[5])),
                    "translate" when p.Length > 0 => new TranslateTransform(p[0], p.Length > 1 ? p[1] : 0),
                    "scale" when p.Length > 0 => new ScaleTransform(p[0], p.Length > 1 ? p[1] : p[0]),
                    "rotate" when p.Length > 0 => new RotateTransform(p[0], p.Length > 2 ? p[1] : 0, p.Length > 2 ? p[2] : 0),
                    _ => null,
                };
                if (t is not null) transforms.Children.Insert(0, t);
            }
            group.Transform = transforms;
        }
        Geometry? geometry = element.Name.LocalName switch
        {
            "path" => Geometry.Parse((string?)element.Attribute("d") ?? "").Clone(),
            "rect" => new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height")), Number(element, "rx"), Number(element, "ry", Number(element, "rx"))),
            "circle" => new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), Number(element, "r"), Number(element, "r")),
            "ellipse" => new EllipseGeometry(new Point(Number(element, "cx"), Number(element, "cy")), Number(element, "rx"), Number(element, "ry")),
            "line" => new LineGeometry(new Point(Number(element, "x1"), Number(element, "y1")), new Point(Number(element, "x2"), Number(element, "y2"))),
            "polygon" or "polyline" => Points((string?)element.Attribute("points"), element.Name.LocalName == "polygon"),
            _ => null,
        };
        if (geometry is not null)
        {
            if (Attribute(element, "fill-rule", "nonzero") == "evenodd" && geometry is PathGeometry path) path.FillRule = FillRule.EvenOdd;
            if (geometry is StreamGeometry stream) stream.FillRule = Attribute(element, "fill-rule", "nonzero") == "evenodd" ? FillRule.EvenOdd : FillRule.Nonzero;
            var pen = Brush(stroke) is { } brush ? new Pen(brush, Number(element, "stroke-width", 1))
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
            } : null;
            group.Children.Add(new GeometryDrawing(Brush(fill), pen, geometry));
        }
        if (element.Name.LocalName is not ("defs" or "clipPath" or "mask"))
            foreach (var child in element.Elements().Where(child => child.Name.LocalName is not ("defs" or "clipPath" or "mask")))
                group.Children.Add(ReadElement(child, fill, stroke));
        return group;
    }

    private static Geometry Points(string? source, bool closed)
    {
        var p = Numbers(source);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (p.Length >= 2)
        {
            context.BeginFigure(new Point(p[0], p[1]), closed, closed);
            for (var i = 2; i + 1 < p.Length; i += 2) context.LineTo(new Point(p[i], p[i + 1]), true, false);
        }
        return geometry;
    }

    private static string Attribute(XElement element, string name, string fallback)
    {
        var direct = (string?)element.Attribute(name);
        if (direct is not null) return direct;
        var style = Regex.Match((string?)element.Attribute("style") ?? "", @"(?:^|;)\s*" + Regex.Escape(name) + @"\s*:\s*([^;]+)");
        return style.Success ? style.Groups[1].Value.Trim() : fallback;
    }
    private static Brush? Brush(string value) => value == "none" ? null :
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private static double Number(XElement element, string name, double fallback = 0) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static double[] Numbers(string? text) => Regex.Matches(text ?? "", @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")
        .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
}
