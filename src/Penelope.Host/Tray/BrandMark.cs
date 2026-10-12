using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Xml.Linq;

namespace Penelope.Host.Tray;

static class BrandMark
{
    internal sealed record Spec(
        float Tile,
        float Radius,
        Color TileColor,
        float TextX,
        float TextY,
        float FontSize,
        Color TextColor,
        string Letter,
        string FontFamily,
        float DotX,
        float DotY,
        float DotRadius);

    public static string SourceSvg()
    {
        using var stream = typeof(BrandMark).Assembly.GetManifestResourceStream("Penelope.Host.penelope.svg");
        if (stream is null)
        {
            throw new InvalidOperationException("The Penelope logo is not embedded in the host.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Spec Parse(string svg)
    {
        var document = XDocument.Parse(svg);
        if (document.Descendants().Any(element => element.Name.LocalName == "circle"))
        {
            throw new InvalidDataException("The Penelope logo source must not include the status dot.");
        }

        var root = document.Root ?? throw new InvalidDataException("The Penelope logo is empty.");
        var rect = Element(document, "rect");
        var text = Element(document, "text");
        var viewBox = (string?)root.Attribute("viewBox") ?? "0 0 128 128";
        var tile = float.Parse(viewBox.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2], CultureInfo.InvariantCulture);
        return new Spec(
            tile,
            Attribute(rect, "rx"),
            Hex((string?)rect.Attribute("fill") ?? "#111827"),
            Attribute(text, "x"),
            Attribute(text, "y"),
            Attribute(text, "font-size"),
            Hex((string?)text.Attribute("fill") ?? "#ffffff"),
            (text.Value ?? "").Trim(),
            PickFont((string?)text.Attribute("font-family") ?? "Segoe UI"),
            Attribute(root, "data-status-cx"),
            Attribute(root, "data-status-cy"),
            Attribute(root, "data-status-r"));
    }

    public static Bitmap Draw(Spec spec, int size, Color? dot)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        var scale = size / spec.Tile;
        using var tile = new SolidBrush(spec.TileColor);
        FillRoundRect(graphics, tile, new RectangleF(0, 0, size - 1, size - 1), spec.Radius * scale);
        using var font = new Font(spec.FontFamily, spec.FontSize * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(spec.TextColor);
        var em = font.FontFamily.GetEmHeight(FontStyle.Bold);
        var ascent = font.FontFamily.GetCellAscent(FontStyle.Bold);
        var y = spec.TextY * scale - font.Size * ascent / em;
        graphics.DrawString(spec.Letter, font, ink, spec.TextX * scale, y, StringFormat.GenericTypographic);
        if (dot is Color color)
        {
            var radius = spec.DotRadius * scale;
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, spec.DotX * scale - radius, spec.DotY * scale - radius, radius * 2, radius * 2);
        }

        return bitmap;
    }

    private static XElement Element(XDocument document, string name)
    {
        return document.Descendants().FirstOrDefault(element => element.Name.LocalName == name)
            ?? throw new InvalidDataException($"The Penelope logo is missing <{name}>.");
    }

    private static float Attribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidDataException($"The Penelope logo is missing {name}.");
        }

        return number;
    }

    private static Color Hex(string value)
    {
        var rgb = int.Parse(value.Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    private static string PickFont(string stack)
    {
        foreach (var part in stack.Split(','))
        {
            var name = part.Trim().Trim('"').Trim('\'');
            if (name.Length == 0 || name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) || name.Equals("serif", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var font = new Font(name, 12, FontStyle.Bold, GraphicsUnit.Pixel);
                if (font.FontFamily.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return "Segoe UI";
    }

    private static void FillRoundRect(Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
