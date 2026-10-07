#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property OutputType=Exe
#:property Nullable=enable
#:property PublishAot=false
#:property PublishTrimmed=false

// 生成公开发布用的原创素材（不含任何游戏官方美术）：
//   background.jpg —— 水墨远山 + 金色圆月 + 祥云，1920×620，深色（界面上还会再盖一层暗色遮罩）
//   app.ico        —— 圆形徽章：金箍棒 + 祥云，16~256 共 7 个尺寸（PNG 编码的 ICO）
// 用法：dotnet run tools/art/make-art.cs -- <输出目录>
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

string outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
var t = new Thread(() =>
{
    SaveJpeg(RenderBackground(1920, 620), Path.Combine(outDir, "background.jpg"));
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];
    var pngs = sizes.Select(s => EncodePng(RenderIcon(s))).ToList();
    File.WriteAllBytes(Path.Combine(outDir, "app.ico"), BuildIco(sizes, pngs));
    File.WriteAllBytes(Path.Combine(outDir, "icon-256.png"), pngs[^1]);
    Console.WriteLine($"written to {Path.GetFullPath(outDir)}");
});
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();

static RenderTargetBitmap Render(int w, int h, Action<DrawingContext> draw)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
        draw(dc);
    var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(visual);
    return bmp;
}

static Color C(byte r, byte g, byte b, byte a = 255) => Color.FromArgb(a, r, g, b);

static BitmapSource RenderBackground(int w, int h) => Render(w, h, dc =>
{
    // 底色：上冷下暗
    dc.DrawRectangle(new LinearGradientBrush(C(0x1C, 0x20, 0x2A), C(0x09, 0x09, 0x0C), 90), null, new Rect(0, 0, w, h));
    // 右侧暖色天光
    var glow = new RadialGradientBrush(C(0x7A, 0x52, 0x1E, 0x90), C(0x7A, 0x52, 0x1E, 0)) { Center = new Point(0.76, 0.36), GradientOrigin = new Point(0.76, 0.36), RadiusX = 0.42, RadiusY = 0.9 };
    dc.DrawRectangle(glow, null, new Rect(0, 0, w, h));

    // 圆月
    var moonCenter = new Point(1470, 215);
    dc.DrawEllipse(new RadialGradientBrush(C(0xF6, 0xD9, 0x8E, 0xD0), C(0xD9, 0x9A, 0x3A, 0x60)) { RadiusX = 0.5, RadiusY = 0.5 },
        null, moonCenter, 128, 128);
    dc.DrawEllipse(null, new Pen(new SolidColorBrush(C(0xF6, 0xD9, 0x8E, 0x50)), 2), moonCenter, 150, 150);
    dc.DrawEllipse(new RadialGradientBrush(C(0xF6, 0xD9, 0x8E, 0x30), C(0xF6, 0xD9, 0x8E, 0)) { RadiusX = 0.5, RadiusY = 0.5 },
        null, moonCenter, 320, 320);

    // 祥云（月前）
    DrawCloud(dc, new Point(1300, 300), 1.6, C(0xE8, 0xB8, 0x60, 0x70));
    DrawCloud(dc, new Point(1640, 340), 1.2, C(0xE8, 0xB8, 0x60, 0x55));

    // 远山：从远到近四层，越近越深越不透明
    var layers = new (double Base, double Amp, int Seed, Color Fill)[]
    {
        (330, 110, 3, C(0x5A, 0x63, 0x72, 0x55)),
        (390, 120, 7, C(0x3A, 0x41, 0x4E, 0x88)),
        (450, 110, 11, C(0x22, 0x26, 0x2E, 0xC0)),
        (525, 90, 19, C(0x10, 0x11, 0x15, 0xF0)),
    };
    foreach (var (baseY, amp, seed, fill) in layers)
    {
        DrawRidge(dc, w, h, baseY, amp, seed, fill);
        // 山脚的雾
        var mist = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        mist.GradientStops.Add(new GradientStop(C(0xC8, 0xCC, 0xD4, 0), 0));
        mist.GradientStops.Add(new GradientStop(C(0xC8, 0xCC, 0xD4, 0x1C), 0.5));
        mist.GradientStops.Add(new GradientStop(C(0xC8, 0xCC, 0xD4, 0), 1));
        dc.DrawRectangle(mist, null, new Rect(0, baseY - 30, w, 110));
    }
    // 远处的塔（第二层山脊上）
    DrawPagoda(dc, new Point(1180, RidgeY(1180, 390, 120, 7) + 4), 0.9, C(0x2E, 0x34, 0x3E, 0xC0));
});

static double RidgeY(double x, double baseY, double amp, int seed)
{
    double y = 0;
    var rnd = new Random(seed);
    for (int k = 1; k <= 5; k++)
    {
        double freq = (0.0015 + rnd.NextDouble() * 0.004) * k;
        double phase = rnd.NextDouble() * Math.PI * 2;
        y += Math.Sin(x * freq + phase) / k;
    }
    // 右侧山更高（人物 / 月亮所在的一侧更有层次），左侧压低给文字留空间
    double rightBias = 0.6 + 0.6 * Math.Clamp(x / 1920.0, 0, 1);
    return baseY - amp * rightBias * (0.55 + 0.45 * y);
}

static void DrawRidge(DrawingContext dc, int w, int h, double baseY, double amp, int seed, Color fill)
{
    var geo = new StreamGeometry();
    using (var g = geo.Open())
    {
        g.BeginFigure(new Point(0, h), true, true);
        for (int x = 0; x <= w; x += 8)
            g.LineTo(new Point(x, RidgeY(x, baseY, amp, seed)), true, true);
        g.LineTo(new Point(w, h), true, false);
    }
    geo.Freeze();
    var brush = new LinearGradientBrush(fill, C(fill.R, fill.G, fill.B, (byte)Math.Min(255, fill.A + 40)), 90);
    dc.DrawGeometry(brush, null, geo);
}

/// <summary>祥云：几段同心的卷曲弧线 + 一条拖尾。</summary>
static void DrawCloud(DrawingContext dc, Point at, double scale, Color color)
{
    var pen = new Pen(new SolidColorBrush(color), 3 * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    void Curl(double cx, double cy, double r, bool right)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            // 由外向内的螺旋
            int dir = right ? 1 : -1;
            g.BeginFigure(new Point(cx + dir * r, cy), false, false);
            for (double a = 0; a <= Math.PI * 3.2; a += 0.08)
            {
                double rr = r * (1 - a / (Math.PI * 4));
                g.LineTo(new Point(cx + dir * rr * Math.Cos(a), cy - rr * Math.Sin(a)), true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
    double s = scale;
    Curl(at.X, at.Y, 22 * s, false);
    Curl(at.X + 40 * s, at.Y - 14 * s, 28 * s, true);
    Curl(at.X + 84 * s, at.Y, 20 * s, true);
    dc.DrawLine(pen, new Point(at.X - 22 * s, at.Y + 18 * s), new Point(at.X + 150 * s, at.Y + 18 * s));
    dc.DrawLine(pen, new Point(at.X + 30 * s, at.Y + 30 * s), new Point(at.X + 190 * s, at.Y + 30 * s));
}

static void DrawPagoda(DrawingContext dc, Point foot, double s, Color color)
{
    var brush = new SolidColorBrush(color);
    double y = foot.Y;
    double width = 34 * s;
    for (int level = 0; level < 5; level++)
    {
        double bodyH = 12 * s;
        dc.DrawRectangle(brush, null, new Rect(foot.X - width * 0.35, y - bodyH, width * 0.7, bodyH));
        // 飞檐
        var roof = new StreamGeometry();
        using (var g = roof.Open())
        {
            g.BeginFigure(new Point(foot.X - width * 0.62, y - bodyH + 1), true, true);
            g.LineTo(new Point(foot.X + width * 0.62, y - bodyH + 1), true, false);
            g.LineTo(new Point(foot.X + width * 0.38, y - bodyH - 5 * s), true, false);
            g.LineTo(new Point(foot.X - width * 0.38, y - bodyH - 5 * s), true, false);
        }
        roof.Freeze();
        dc.DrawGeometry(brush, null, roof);
        y -= bodyH + 5 * s;
        width *= 0.84;
    }
    dc.DrawRectangle(brush, null, new Rect(foot.X - 1.5 * s, y - 14 * s, 3 * s, 14 * s));
}

/// <summary>图标：深色圆形底 + 金边，斜放的金箍棒（红身金箍）+ 一朵金色祥云。按 256 设计后缩放。</summary>
static BitmapSource RenderIcon(int size) => Render(size, size, dc =>
{
    double k = size / 256.0;
    dc.PushTransform(new ScaleTransform(k, k));
    var center = new Point(128, 128);
    dc.DrawEllipse(new RadialGradientBrush(C(0x4A, 0x24, 0x12), C(0x14, 0x0B, 0x08)) { GradientOrigin = new Point(0.4, 0.35) },
        null, center, 122, 122);
    var gold = new LinearGradientBrush(C(0xFA, 0xDE, 0x8A), C(0xB3, 0x76, 0x22), 60);
    dc.DrawEllipse(null, new Pen(gold, size <= 32 ? 16 : 10), center, 116, 116);

    // 金箍棒：从左下到右上
    dc.PushTransform(new RotateTransform(-45, 128, 128));
    var body = new LinearGradientBrush(C(0xC2, 0x2E, 0x22), C(0x6E, 0x10, 0x0C), 90);
    double len = 196, thick = size <= 32 ? 30 : 22;
    dc.DrawRoundedRectangle(body, null, new Rect(128 - len / 2, 128 - thick / 2, len, thick), thick / 2, thick / 2);
    var band = new LinearGradientBrush(C(0xFF, 0xE3, 0x8F), C(0xC0, 0x84, 0x2A), 90);
    double bandW = 34;
    dc.DrawRoundedRectangle(band, null, new Rect(128 - len / 2, 128 - thick / 2 - 2, bandW, thick + 4), 6, 6);
    dc.DrawRoundedRectangle(band, null, new Rect(128 + len / 2 - bandW, 128 - thick / 2 - 2, bandW, thick + 4), 6, 6);
    dc.Pop();

    // 祥云（右下，压在棒上）
    if (size >= 48)
    {
        var cloudFill = new LinearGradientBrush(C(0xFF, 0xE8, 0xA8), C(0xD8, 0x9C, 0x3C), 90);
        var outline = new Pen(new SolidColorBrush(C(0x2A, 0x14, 0x0A)), 4);
        var cloud = new GeometryGroup { FillRule = FillRule.Nonzero };
        cloud.Children.Add(new EllipseGeometry(new Point(150, 178), 24, 20));
        cloud.Children.Add(new EllipseGeometry(new Point(178, 166), 26, 24));
        cloud.Children.Add(new EllipseGeometry(new Point(204, 182), 20, 17));
        cloud.Children.Add(new RectangleGeometry(new Rect(140, 180, 72, 18), 9, 9));
        var merged = Geometry.Combine(Geometry.Combine(cloud.Children[0], cloud.Children[1], GeometryCombineMode.Union, null),
            Geometry.Combine(cloud.Children[2], cloud.Children[3], GeometryCombineMode.Union, null), GeometryCombineMode.Union, null);
        dc.DrawGeometry(cloudFill, outline, merged);
        var swirl = new Pen(new SolidColorBrush(C(0x8A, 0x52, 0x1A)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawEllipse(null, swirl, new Point(178, 168), 10, 9);
        dc.DrawEllipse(null, swirl, new Point(152, 180), 7, 6);
    }
    dc.Pop();
});

static void SaveJpeg(BitmapSource bmp, string path)
{
    var enc = new JpegBitmapEncoder { QualityLevel = 90 };
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var fs = File.Create(path);
    enc.Save(fs);
}

static byte[] EncodePng(BitmapSource bmp)
{
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}

/// <summary>ICO 文件：ICONDIR + 每个尺寸一条 ICONDIRENTRY + PNG 数据。</summary>
static byte[] BuildIco(int[] sizes, List<byte[]> pngs)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((ushort)0);
    w.Write((ushort)1);
    w.Write((ushort)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(pngs[i].Length);
        w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs)
        w.Write(p);
    return ms.ToArray();
}
