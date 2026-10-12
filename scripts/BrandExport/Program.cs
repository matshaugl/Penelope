using System.Drawing;
using System.Drawing.Imaging;
using Penelope.Host.Tray;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: BrandExport <penelope.svg> <icon-folder> <web-public-folder>");
    return 1;
}

var svg = File.ReadAllText(args[0]);
var spec = BrandMark.Parse(svg);
Directory.CreateDirectory(args[1]);
Directory.CreateDirectory(args[2]);

var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var images = sizes.Select(size => (size, png: Png(spec, size))).ToArray();
var generated = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "generated");
Directory.CreateDirectory(generated);
foreach (var image in images)
{
    File.WriteAllBytes(Path.Combine(generated, $"penelope-{image.size}.png"), image.png);
}

WriteIco(Path.Combine(args[1], "penelope.ico"), images);
WriteIco(Path.Combine(args[2], "favicon.ico"), images.Where(image => image.size is 16 or 32 or 48).ToArray());
File.WriteAllText(Path.Combine(args[2], "favicon.svg"), svg.Trim() + Environment.NewLine);
return 0;

static byte[] Png(BrandMark.Spec spec, int size)
{
    using var bitmap = BrandMark.Draw(spec, size, dot: null);
    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static void WriteIco(string path, IReadOnlyList<(int size, byte[] png)> images)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);
    var offset = 6 + (16 * images.Count);
    foreach (var image in images)
    {
        writer.Write((byte)(image.size >= 256 ? 0 : image.size));
        writer.Write((byte)(image.size >= 256 ? 0 : image.size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(image.png.Length);
        writer.Write(offset);
        offset += image.png.Length;
    }

    foreach (var image in images)
    {
        writer.Write(image.png);
    }
}
