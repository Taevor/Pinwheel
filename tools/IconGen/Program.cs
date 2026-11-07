using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

if (args.Length >= 2 && args[0] == "preview")
{
    string previewDir = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(previewDir);

    const int previewSize = 512;
    using var bitmap = new Bitmap(previewSize, previewSize, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);
        DrawIcon(g, previewSize);
    }

    string path = Path.Combine(previewDir, "Pinwheel-Fluent-Preview.png");
    bitmap.Save(path, ImageFormat.Png);
    Console.WriteLine($"预览图已生成: {path}");
    return 0;
}

if (args.Length < 1)
{
    Console.Error.WriteLine("用法: IconGen <输出目录>");
    return 1;
}

string outDir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outDir);

int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var entries = new List<(int size, byte[] png)>();

foreach (int size in sizes)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);
        DrawIcon(g, size);
    }

    using var ms = new MemoryStream();
    bitmap.Save(ms, ImageFormat.Png);
    byte[] png = ms.ToArray();
    entries.Add((size, png));
    File.WriteAllBytes(Path.Combine(outDir, $"Pinwheel-{size}.png"), png);
}

string icoPath = Path.Combine(outDir, "Pinwheel.ico");
using (var fs = File.Create(icoPath))
using (var bw = new BinaryWriter(fs))
{
    bw.Write((ushort)0); // reserved
    bw.Write((ushort)1); // type: icon
    bw.Write((ushort)entries.Count);

    int offset = 6 + 16 * entries.Count;
    foreach (var (size, png) in entries)
    {
        byte dimension = size >= 256 ? (byte)0 : (byte)size;
        bw.Write(dimension);
        bw.Write(dimension);
        bw.Write((byte)0); // color count
        bw.Write((byte)0); // reserved
        bw.Write((ushort)1); // planes
        bw.Write((ushort)32); // bits per pixel
        bw.Write((uint)png.Length);
        bw.Write((uint)offset);
        offset += png.Length;
    }

    foreach (var (_, png) in entries)
    {
        bw.Write(png);
    }
}

Console.WriteLine($"已生成 {entries.Count} 个尺寸和 {icoPath}");
return 0;

static void DrawIcon(Graphics g, int size)
{
    float s = size;
    float cx = s / 2f;
    float cy = s / 2f;

    // 1. 纯圆形 Fluent 底板参数
    // 留出边距用于渲染环境柔和阴影，避免贴边裁剪
    float discR = s * 0.445f;
    var discRect = new RectangleF(cx - discR, cy - discR, discR * 2, discR * 2);

    // 2. 绘制多层环境柔和外阴影 (Ambient Soft Shadow)
    if (size >= 32)
    {
        float shadowOffset = s * 0.026f;
        for (int i = 3; i >= 1; i--)
        {
            float blur = i * (s * 0.012f);
            var shadowRect = new RectangleF(
                cx - discR - blur * 0.3f,
                cy - discR + shadowOffset + blur * 0.6f,
                discR * 2 + blur * 0.6f,
                discR * 2 + blur * 0.6f);

            int alpha = (int)(22 / i);
            using var shadowBrush = new SolidBrush(Color.FromArgb(alpha, 2, 22, 58));
            g.FillEllipse(shadowBrush, shadowRect);
        }
    }

    // 3. 绘制 Fluent 渐变圆形底板 (Fluent Vibrant Blue Gradient)
    using (var discPath = new GraphicsPath())
    {
        discPath.AddEllipse(discRect);

        using var gradientBrush = new LinearGradientBrush(
            new PointF(discRect.Left, discRect.Top),
            new PointF(discRect.Right, discRect.Bottom),
            Color.FromArgb(0x38, 0xC4, 0xFF),
            Color.FromArgb(0x06, 0x48, 0x9E))
        {
            InterpolationColors = new ColorBlend(3)
            {
                Colors = new[]
                {
                    Color.FromArgb(0x3E, 0xC8, 0xFF), // 左上角：明澈天青蓝
                    Color.FromArgb(0x12, 0x7E, 0xDE), // 中部：经典 Fluent 微软蓝
                    Color.FromArgb(0x06, 0x42, 0x96)  // 右下角：科技沉稳深蓝
                },
                Positions = new[] { 0.0f, 0.46f, 1.0f }
            }
        };

        g.FillPath(gradientBrush, discPath);

        // 4. 边缘亚克力高光描边 (Subtle Specular Rim)
        if (size >= 24)
        {
            float penWidth = Math.Max(1.0f, s * 0.012f);
            using var rimBrush = new LinearGradientBrush(
                new PointF(discRect.Left, discRect.Top),
                new PointF(discRect.Right, discRect.Bottom),
                Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
            using var rimPen = new Pen(rimBrush, penWidth);
            g.DrawPath(rimPen, discPath);
        }
    }

    // 5. 单圈 8 扇区轮盘几何参数（前后处处等宽的平行缝隙）
    float outerR = discR * 0.81f;     // 轮盘外半径：舒展大气
    float innerR = discR * 0.28f;     // 轮盘内半径：优雅向心
    float haloR = discR * 0.20f;      // 中心微光光晕
    float centerDotR = discR * 0.10f; // 中心小白点核心

    // 缝隙宽度：前后恒定一致（等宽平行切片）
    float gapWidth = Math.Max(1.2f, s * 0.026f);
    float spanDeg = 45f;

    // 6. 轮盘微投影（增强悬浮立体感）
    if (size >= 48)
    {
        float shadowY = s * 0.015f;
        using var wheelShadowBrush = new SolidBrush(Color.FromArgb(0x34, 0x02, 0x1E, 0x48));
        for (int i = 0; i < 8; i++)
        {
            float midDeg = -90f + i * spanDeg;
            using var slice = CreateParallelGapSlice(cx, cy + shadowY, innerR, outerR, midDeg, spanDeg, gapWidth);
            g.FillPath(wheelShadowBrush, slice);
        }
    }

    // 7. 绘制单圈 8 扇区纯白轮盘（前后完全等宽的平行间隙）
    using (var sliceBrush = new SolidBrush(Color.FromArgb(0xF8, 0xFF, 0xFF, 0xFF)))
    {
        for (int i = 0; i < 8; i++)
        {
            float midDeg = -90f + i * spanDeg;
            using var slice = CreateParallelGapSlice(cx, cy, innerR, outerR, midDeg, spanDeg, gapWidth);
            g.FillPath(sliceBrush, slice);
        }
    }

    // 8. 中心微光光晕与小白点核心
    using (var haloBrush = new SolidBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)))
    {
        g.FillEllipse(haloBrush, cx - haloR, cy - haloR, haloR * 2, haloR * 2);
    }
    using (var coreBrush = new SolidBrush(Color.White))
    {
        g.FillEllipse(coreBrush, cx - centerDotR, cy - centerDotR, centerDotR * 2, centerDotR * 2);
    }
}

/// <summary>
/// 构造前后处处等宽（平行直线边界）的扇区几何路径
/// </summary>
static GraphicsPath CreateParallelGapSlice(float cx, float cy, float r1, float r2, float midDeg, float spanDeg, float gapWidth)
{
    float halfGap = gapWidth / 2f;
    float bStart = midDeg - spanDeg / 2f;
    float bEnd = midDeg + spanDeg / 2f;

    var (pOutStart, aOutStart) = PointOnStraight(cx, cy, bStart, +halfGap, r2);
    var (pOutEnd, aOutEnd) = PointOnStraight(cx, cy, bEnd, -halfGap, r2);
    var (pInStart, aInStart) = PointOnStraight(cx, cy, bStart, +halfGap, r1);
    var (pInEnd, aInEnd) = PointOnStraight(cx, cy, bEnd, -halfGap, r1);

    // 顺时针从外侧起点扫到外侧终点
    float outerSweep = (aOutEnd - aOutStart + 360f) % 360f;
    // 逆时针从内侧终点扫到内侧起点
    float innerSweep = (aInEnd - aInStart + 360f) % 360f;

    var path = new GraphicsPath();
    // 1. 外圆弧
    path.AddArc(cx - r2, cy - r2, r2 * 2, r2 * 2, aOutStart, outerSweep);
    // 2. 右侧绝对平行的笔直边
    path.AddLine(pOutEnd, pInEnd);
    // 3. 内圆弧（逆向扫回）
    path.AddArc(cx - r1, cy - r1, r1 * 2, r1 * 2, aInEnd, -innerSweep);
    // 4. 左侧绝对平行的笔直边
    path.AddLine(pInStart, pOutStart);
    path.CloseFigure();
    return path;
}

static (PointF pt, float deg) PointOnStraight(float cx, float cy, float boundaryDeg, float d, float r)
{
    double rad = boundaryDeg * Math.PI / 180.0;
    double cos = Math.Cos(rad);
    double sin = Math.Sin(rad);
    double t = Math.Sqrt(Math.Max(0.0, r * r - d * d));
    float x = (float)(cx + t * cos - d * sin);
    float y = (float)(cy + t * sin + d * cos);
    float deg = (float)(Math.Atan2(y - cy, x - cx) * 180.0 / Math.PI);
    deg = (deg + 360f) % 360f;
    return (new PointF(x, y), deg);
}
