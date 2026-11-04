using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// Draws the RoDock app icon (an "R" standing on a dock bar, with a green "online" dot) and packs it into RoDock.ico.
///   dotnet run --project RoDock.Tests -- icon DIR   -> DIR/RoDock.ico and DIR/RoDock-256.png
static class IconMaker
{
    static BitmapSource Render(int px)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(px / 256.0, px / 256.0));
            var tile = new Rect(8, 8, 240, 240);
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(0x55, 0x7B, 0xA5), Color.FromRgb(0x16, 0x2A, 0x44), 55), null, tile, 56, 56);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 3), new Rect(10.5, 10.5, 235, 235), 54, 54);

            var white = new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFF));
            // "RoD": three letters drawn as round-capped strokes in a 192 x 112 box, centred above the dock bar.
            var pen = new Pen(white, 20) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.PushTransform(new TransformGroup { Children = [new TranslateTransform(-96, -120), new ScaleTransform(0.91, 0.91), new TranslateTransform(128, 114)] });
            dc.DrawGeometry(null, pen, Geometry.Parse("M10,166 L10,74 L30,74 A21,21 0 0 1 30,116 L10,116 M28,116 L52,166")); // R
            dc.DrawEllipse(null, pen, new Point(94, 146), 16, 16); // o
            dc.DrawGeometry(null, pen, Geometry.Parse("M136,74 L152,74 A30,46 0 0 1 152,166 L136,166 Z")); // D
            dc.Pop();

            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x8C, 0xC4, 0xFF)), null, new Rect(64, 198, 128, 16), 8, 8);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)), new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x4B, 0x73)), 6), new Point(214, 46), 14, 14);
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    static byte[] Png(BitmapSource s)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(s));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var t = new Thread(() =>
        {
            int[] sizes = [16, 24, 32, 48, 64, 128, 256];
            var pngs = sizes.Select(s => Png(Render(s))).ToList();
            File.WriteAllBytes(Path.Combine(outDir, "RoDock-256.png"), pngs[^1]);

            using var w = new BinaryWriter(File.Create(Path.Combine(outDir, "RoDock.ico")));
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); // ICONDIR: icon type, image count
            var offset = 6 + 16 * sizes.Length;
            for (var i = 0; i < sizes.Length; i++)
            {
                var b = (byte)(sizes[i] == 256 ? 0 : sizes[i]); // 0 means 256
                w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p); // PNG-compressed entries (Windows Vista and later)
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }
}
