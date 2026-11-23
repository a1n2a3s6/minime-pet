using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniMe.Engine;

public enum SpriteFrame
{
    Idle = 0,
    Walk1 = 1,
    Walk2 = 2,
    Troll = 3,
    HitDizzy = 4,
    Dragged = 5,
    Sitting = 6,
    Sleeping = 7
}

public sealed record ProcessedSpriteSheet(BitmapSource CleanedSheet, BitmapSource[] Frames);

public static class SpriteProcessor
{
    public const int Columns = 4;
    public const int Rows = 2;
    public const int FrameCount = Columns * Rows;

    public static readonly string[] FrameLabels =
    [
        "0: Idle",
        "1: Walk 1",
        "2: Walk 2",
        "3: Troll / Laugh",
        "4: Hit / Dizzy",
        "5: Dragged",
        "6: Sitting",
        "7: Sleeping"
    ];

    /// <summary>
    /// Loads a PNG/JPG sprite sheet, removes #00FF00 green screen via HSV + green-dominance
    /// chroma keying with edge spill suppression, and slices it into a 4x2 (8-frame) array.
    /// </summary>
    public static ProcessedSpriteSheet LoadAndProcessSpriteSheet(string filePath, double tolerance = 0.30)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Sprite sheet file not found.", filePath);
        }

        double clampedTolerance = Math.Clamp(tolerance, 0.10, 0.60);

        var rawBitmap = new BitmapImage();
        rawBitmap.BeginInit();
        rawBitmap.UriSource = new Uri(Path.GetFullPath(filePath), UriKind.Absolute);
        rawBitmap.CacheOption = BitmapCacheOption.OnLoad;
        rawBitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        rawBitmap.EndInit();
        rawBitmap.Freeze();

        var bgra = new FormatConvertedBitmap(rawBitmap, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        bgra.CopyPixels(pixels, stride, 0);

        ApplyChromaKeyAndSpillSuppression(pixels, clampedTolerance);

        var cleaned = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        cleaned.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        cleaned.Freeze();

        BitmapSource[] frames = SliceGrid(cleaned);
        return new ProcessedSpriteSheet(cleaned, frames);
    }

    /// <summary>
    /// Slices an already-cleaned sprite sheet PNG (e.g. saved in %AppData%/MiniMe/skins) into 8 frames.
    /// </summary>
    public static BitmapSource[] LoadSavedCleanedSkin(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(Path.GetFullPath(filePath), UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        bitmap.Freeze();

        return SliceGrid(bitmap);
    }

    public static void SaveCleanedSpriteSheet(BitmapSource cleanedSheet, string destinationPath)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cleanedSheet));
        using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void ApplyChromaKeyAndSpillSuppression(byte[] bgraPixels, double tolerance)
    {
        // Map tolerance (0.10..0.60) to HSV and green-dominance thresholds
        double hueHalfWindow = 18.0 + (tolerance * 62.0);        // ~24° to ~55° around 120°
        double minSaturation = Math.Max(0.18, 0.55 - (tolerance * 0.58));
        double minValue = Math.Max(0.16, 0.44 - (tolerance * 0.42));
        double domHard = Math.Max(0.06, 0.28 - (tolerance * 0.34));
        double domSoft = domHard * 0.38;

        for (int i = 0; i < bgraPixels.Length; i += 4)
        {
            byte origA = bgraPixels[i + 3];
            if (origA == 0)
            {
                continue;
            }

            double b = bgraPixels[i] / 255.0;
            double g = bgraPixels[i + 1] / 255.0;
            double r = bgraPixels[i + 2] / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double v = max;
            double s = max > 1e-5 ? delta / max : 0.0;
            double h = 0.0;

            if (delta > 1e-5)
            {
                if (Math.Abs(max - r) < 1e-6)
                {
                    h = 60.0 * (((g - b) / delta) % 6.0);
                }
                else if (Math.Abs(max - g) < 1e-6)
                {
                    h = 60.0 * (((b - r) / delta) + 2.0);
                }
                else
                {
                    h = 60.0 * (((r - g) / delta) + 4.0);
                }

                if (h < 0.0)
                {
                    h += 360.0;
                }
            }

            double maxRB = Math.Max(r, b);
            double greenDominance = g - maxRB;
            double hueDist = Math.Abs(h - 120.0);

            // Detect chroma-key green screen pixel
            if (hueDist <= hueHalfWindow && s >= minSaturation && v >= minValue && greenDominance > domSoft)
            {
                double hueFactor = 1.0 - (hueDist / hueHalfWindow);
                double domFactor = Math.Clamp((greenDominance - domSoft) / Math.Max(0.01, domHard - domSoft), 0.0, 1.0);
                double keyStrength = Math.Clamp(domFactor * (0.45 + 0.55 * hueFactor), 0.0, 1.0);

                if (keyStrength >= 0.88)
                {
                    bgraPixels[i] = 0;
                    bgraPixels[i + 1] = 0;
                    bgraPixels[i + 2] = 0;
                    bgraPixels[i + 3] = 0;
                    continue;
                }

                // Soft feathered edge alpha
                double newAlpha = (origA / 255.0) * (1.0 - keyStrength);
                bgraPixels[i + 3] = (byte)Math.Clamp((int)Math.Round(newAlpha * 255.0), 0, 255);
            }

            // Green spill suppression along edges / semi-green fringe
            if (greenDominance > 0.02 && hueDist <= hueHalfWindow + 24.0 && s > 0.14)
            {
                double spillWeight = Math.Clamp(1.0 - (hueDist / (hueHalfWindow + 24.0)), 0.0, 1.0);
                double suppressedG = g - (greenDominance * (0.65 + 0.35 * spillWeight));
                double finalG = Math.Min(g, Math.Max(maxRB, suppressedG));
                bgraPixels[i + 1] = (byte)Math.Clamp((int)Math.Round(finalG * 255.0), 0, 255);
            }
        }
    }

    private static BitmapSource[] SliceGrid(BitmapSource sheet)
    {
        int cellW = Math.Max(1, sheet.PixelWidth / Columns);
        int cellH = Math.Max(1, sheet.PixelHeight / Rows);
        var frames = new BitmapSource[FrameCount];

        int index = 0;
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int x = col * cellW;
                int y = row * cellH;
                int w = Math.Min(cellW, sheet.PixelWidth - x);
                int h = Math.Min(cellH, sheet.PixelHeight - y);

                var cropped = new CroppedBitmap(sheet, new Int32Rect(x, y, w, h));
                cropped.Freeze();
                frames[index++] = cropped;
            }
        }

        return frames;
    }

    /// <summary>
    /// Generates an expressive 8-frame cartoon Chibi character programmatically via DrawingVisual
    /// so MiniMe works immediately on first launch without any external assets.
    /// </summary>
    public static BitmapSource[] GenerateProceduralFallbackFrames(int frameSize = 144)
    {
        var frames = new BitmapSource[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            frames[i] = RenderChibiFrame((SpriteFrame)i, frameSize);
        }
        return frames;
    }

    private static BitmapSource RenderChibiFrame(SpriteFrame frame, int size)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            double s = size / 144.0;
            dc.PushTransform(new ScaleTransform(s, s));

            // Palette: crisp dark-ink outline, warm amber hoodie, cyan trim, soft skin
            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(26, 30, 42)), 3.2)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var thinPen = new Pen(new SolidColorBrush(Color.FromRgb(26, 30, 42)), 2.2)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            var skinBrush = new SolidColorBrush(Color.FromRgb(255, 224, 196));
            var cheekBrush = new SolidColorBrush(Color.FromArgb(145, 255, 118, 134));
            var hairBrush = new SolidColorBrush(Color.FromRgb(38, 46, 66));
            var hoodieBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));   // Warm Amber
            var hoodieShade = new SolidColorBrush(Color.FromRgb(217, 119, 6));
            var accentCyan = new SolidColorBrush(Color.FromRgb(56, 189, 248));    // Crisp Cyan
            var shoeBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            var shadowBrush = new SolidColorBrush(Color.FromArgb(65, 10, 14, 24));
            var whiteBrush = Brushes.White;

            double bobY = frame switch
            {
                SpriteFrame.Walk1 => -4,
                SpriteFrame.Walk2 => -4,
                SpriteFrame.Troll => -3,
                SpriteFrame.HitDizzy => -2,
                SpriteFrame.Dragged => 4,
                SpriteFrame.Sitting => 8,
                SpriteFrame.Sleeping => 14,
                _ => 0
            };

            // Ground contact shadow (hidden when dragged)
            if (frame != SpriteFrame.Dragged)
            {
                double shadowW = frame == SpriteFrame.Sleeping ? 42 : 30;
                double shadowY = frame == SpriteFrame.Sitting ? 128 : 132;
                dc.DrawEllipse(shadowBrush, null, new Point(72, shadowY), shadowW, 7);
            }

            if (frame == SpriteFrame.Sleeping)
            {
                DrawSleepingPose(dc, outlinePen, thinPen, skinBrush, cheekBrush, hairBrush, hoodieBrush, accentCyan);
                dc.Pop();
                return BakeVisual(visual, size);
            }

            // Legs / Shoes
            if (frame == SpriteFrame.Sitting)
            {
                // Legs dangling forward
                dc.DrawRoundedRectangle(shoeBrush, outlinePen, new Rect(46, 108, 18, 14), 6, 6);
                dc.DrawRoundedRectangle(shoeBrush, outlinePen, new Rect(80, 108, 18, 14), 6, 6);
                dc.DrawEllipse(accentCyan, null, new Point(55, 115), 3.5, 2.5);
                dc.DrawEllipse(accentCyan, null, new Point(89, 115), 3.5, 2.5);
            }
            else if (frame == SpriteFrame.Dragged)
            {
                // Dangling kicked legs
                dc.DrawRoundedRectangle(shoeBrush, outlinePen, new Rect(50, 114, 15, 18), 6, 6);
                dc.DrawRoundedRectangle(shoeBrush, outlinePen, new Rect(79, 110, 15, 18), 6, 6);
            }
            else
            {
                double leftLegOffset = frame == SpriteFrame.Walk1 ? -7 : frame == SpriteFrame.Walk2 ? 6 : 0;
                double rightLegOffset = frame == SpriteFrame.Walk1 ? 6 : frame == SpriteFrame.Walk2 ? -7 : 0;
                double leftLegLift = frame == SpriteFrame.Walk1 ? -4 : 0;
                double rightLegLift = frame == SpriteFrame.Walk2 ? -4 : 0;

                dc.DrawRoundedRectangle(shoeBrush, outlinePen, new Rect(50 + leftLegOffset, 112 + leftLegLift, 16, 17), 6, 6);
                dc.DrawRoundedRectangle(shoeBrush, outlinePen, new Rect(78 + rightLegOffset, 112 + rightLegLift, 16, 17), 6, 6);
            }

            // Body (Chibi hoodie torso)
            double bodyTop = 76 + bobY;
            dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(46, bodyTop, 52, 42), 16, 16);
            // Hoodie pocket
            dc.DrawRoundedRectangle(hoodieShade, thinPen, new Rect(58, bodyTop + 21, 28, 14), 5, 5);
            // Cyan hoodie drawstrings
            dc.DrawLine(new Pen(accentCyan, 2.6) { EndLineCap = PenLineCap.Round }, new Point(66, bodyTop + 5), new Point(64, bodyTop + 17));
            dc.DrawLine(new Pen(accentCyan, 2.6) { EndLineCap = PenLineCap.Round }, new Point(78, bodyTop + 5), new Point(80, bodyTop + 17));

            // Arms
            if (frame == SpriteFrame.Dragged)
            {
                // Arms raised/flailing
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(28, bodyTop - 10, 20, 14), 7, 7);
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(96, bodyTop - 10, 20, 14), 7, 7);
                dc.DrawEllipse(skinBrush, thinPen, new Point(29, bodyTop - 3), 5.5, 5.5);
                dc.DrawEllipse(skinBrush, thinPen, new Point(115, bodyTop - 3), 5.5, 5.5);
            }
            else if (frame == SpriteFrame.Troll)
            {
                // One hand on hip, one pointing/waving mischievously
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(34, bodyTop + 12, 16, 14), 6, 6);
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(94, bodyTop - 2, 22, 13), 6, 6);
                dc.DrawEllipse(skinBrush, thinPen, new Point(117, bodyTop + 4), 5.5, 5.5);
            }
            else if (frame == SpriteFrame.HitDizzy)
            {
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(30, bodyTop + 4, 18, 13), 6, 6);
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(96, bodyTop + 4, 18, 13), 6, 6);
            }
            else
            {
                double armSwing = frame == SpriteFrame.Walk1 ? 5 : frame == SpriteFrame.Walk2 ? -5 : 0;
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(34, bodyTop + 10 + armSwing, 15, 22), 7, 7);
                dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(95, bodyTop + 10 - armSwing, 15, 22), 7, 7);
            }

            // Head (large cute Chibi proportions)
            double headCenterY = 50 + bobY;
            dc.DrawEllipse(skinBrush, outlinePen, new Point(72, headCenterY), 33, 30);

            // Hair cap + ahoge tuft
            var hairGeo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(39, headCenterY - 2), IsClosed = true };
            fig.Segments.Add(new BezierSegment(new Point(38, headCenterY - 34), new Point(106, headCenterY - 34), new Point(105, headCenterY - 2), true));
            fig.Segments.Add(new QuadraticBezierSegment(new Point(94, headCenterY - 14), new Point(84, headCenterY - 6), true));
            fig.Segments.Add(new QuadraticBezierSegment(new Point(74, headCenterY - 17), new Point(62, headCenterY - 7), true));
            fig.Segments.Add(new QuadraticBezierSegment(new Point(50, headCenterY - 15), new Point(39, headCenterY - 2), true));
            hairGeo.Figures.Add(fig);
            dc.DrawGeometry(hairBrush, outlinePen, hairGeo);

            // Signature bouncy hair tuft on top
            var tuftGeo = new PathGeometry();
            var tuftFig = new PathFigure { StartPoint = new Point(70, headCenterY - 28), IsClosed = false };
            tuftFig.Segments.Add(new QuadraticBezierSegment(new Point(76, headCenterY - 44), new Point(88, headCenterY - 35), true));
            tuftGeo.Figures.Add(tuftFig);
            dc.DrawGeometry(null, new Pen(hairBrush, 4.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, tuftGeo);

            // Cute cyan headphones accent on sides of head
            dc.DrawRoundedRectangle(accentCyan, thinPen, new Rect(35, headCenterY - 6, 8, 16), 4, 4);
            dc.DrawRoundedRectangle(accentCyan, thinPen, new Rect(101, headCenterY - 6, 8, 16), 4, 4);

            // Cheeks
            dc.DrawEllipse(cheekBrush, null, new Point(51, headCenterY + 11), 6, 3.5);
            dc.DrawEllipse(cheekBrush, null, new Point(93, headCenterY + 11), 6, 3.5);

            // Facial expression per frame
            DrawFaceExpression(dc, frame, headCenterY, thinPen, whiteBrush, hairBrush);

            dc.Pop();
        }

        return BakeVisual(visual, size);
    }

    private static void DrawFaceExpression(
        DrawingContext dc,
        SpriteFrame frame,
        double headY,
        Pen pen,
        Brush whiteBrush,
        Brush darkBrush)
    {
        double eyeY = headY + 3;
        switch (frame)
        {
            case SpriteFrame.HitDizzy:
                // X_X dizzy eyes
                dc.DrawLine(pen, new Point(53, eyeY - 5), new Point(63, eyeY + 5));
                dc.DrawLine(pen, new Point(63, eyeY - 5), new Point(53, eyeY + 5));
                dc.DrawLine(pen, new Point(81, eyeY - 5), new Point(91, eyeY + 5));
                dc.DrawLine(pen, new Point(91, eyeY - 5), new Point(81, eyeY + 5));
                // Wavy dizzy mouth
                var dizzyMouth = new PathGeometry();
                var dmFig = new PathFigure { StartPoint = new Point(64, headY + 16), IsClosed = false };
                dmFig.Segments.Add(new QuadraticBezierSegment(new Point(68, headY + 12), new Point(72, headY + 16), true));
                dmFig.Segments.Add(new QuadraticBezierSegment(new Point(76, headY + 20), new Point(80, headY + 16), true));
                dizzyMouth.Figures.Add(dmFig);
                dc.DrawGeometry(null, pen, dizzyMouth);
                break;

            case SpriteFrame.Troll:
                // Smug squinting eyes > <
                dc.DrawLine(pen, new Point(52, eyeY - 4), new Point(63, eyeY));
                dc.DrawLine(pen, new Point(63, eyeY), new Point(52, eyeY + 4));
                dc.DrawLine(pen, new Point(92, eyeY - 4), new Point(81, eyeY));
                dc.DrawLine(pen, new Point(81, eyeY), new Point(92, eyeY + 4));
                // Mischievous wide grin
                var grin = new PathGeometry();
                var gFig = new PathFigure { StartPoint = new Point(60, headY + 13), IsClosed = true };
                gFig.Segments.Add(new QuadraticBezierSegment(new Point(72, headY + 25), new Point(84, headY + 13), true));
                grin.Figures.Add(gFig);
                dc.DrawGeometry(whiteBrush, pen, grin);
                break;

            case SpriteFrame.Dragged:
                // Wide panicked O_O eyes
                dc.DrawEllipse(whiteBrush, pen, new Point(58, eyeY), 6.5, 7);
                dc.DrawEllipse(whiteBrush, pen, new Point(86, eyeY), 6.5, 7);
                dc.DrawEllipse(darkBrush, null, new Point(58, eyeY), 2.5, 2.5);
                dc.DrawEllipse(darkBrush, null, new Point(86, eyeY), 2.5, 2.5);
                // Surprised O mouth
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(239, 68, 68)), pen, new Point(72, headY + 16), 4.5, 5.5);
                break;

            default:
                // Bright expressive anime-chibi eyes
                dc.DrawEllipse(darkBrush, null, new Point(58, eyeY), 5.5, 6.8);
                dc.DrawEllipse(darkBrush, null, new Point(86, eyeY), 5.5, 6.8);
                dc.DrawEllipse(whiteBrush, null, new Point(56.2, eyeY - 2.3), 2.2, 2.2);
                dc.DrawEllipse(whiteBrush, null, new Point(84.2, eyeY - 2.3), 2.2, 2.2);
                dc.DrawEllipse(whiteBrush, null, new Point(59.8, eyeY + 2.2), 1.1, 1.1);
                dc.DrawEllipse(whiteBrush, null, new Point(87.8, eyeY + 2.2), 1.1, 1.1);

                // Cute cat/chibi smile
                var smile = new PathGeometry();
                var sFig = new PathFigure { StartPoint = new Point(65, headY + 14), IsClosed = false };
                sFig.Segments.Add(new QuadraticBezierSegment(new Point(72, headY + 20), new Point(79, headY + 14), true));
                smile.Figures.Add(sFig);
                dc.DrawGeometry(null, pen, smile);
                break;
        }
    }

    private static void DrawSleepingPose(
        DrawingContext dc,
        Pen outlinePen,
        Pen thinPen,
        Brush skinBrush,
        Brush cheekBrush,
        Brush hairBrush,
        Brush hoodieBrush,
        Brush accentCyan)
    {
        // Curled cozy body
        dc.DrawRoundedRectangle(hoodieBrush, outlinePen, new Rect(36, 90, 74, 38), 19, 19);
        // Resting head tilted on cushion/arms
        dc.DrawEllipse(skinBrush, outlinePen, new Point(64, 80), 30, 26);

        var hairGeo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(34, 78), IsClosed = true };
        fig.Segments.Add(new BezierSegment(new Point(34, 48), new Point(94, 48), new Point(94, 78), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(64, 66), new Point(34, 78), true));
        hairGeo.Figures.Add(fig);
        dc.DrawGeometry(hairBrush, outlinePen, hairGeo);

        dc.DrawEllipse(cheekBrush, null, new Point(46, 89), 5.5, 3);
        dc.DrawEllipse(cheekBrush, null, new Point(82, 89), 5.5, 3);

        // Closed peaceful eyes (u u)
        var leftEye = new PathGeometry();
        var lFig = new PathFigure { StartPoint = new Point(48, 82), IsClosed = false };
        lFig.Segments.Add(new QuadraticBezierSegment(new Point(54, 88), new Point(60, 82), true));
        leftEye.Figures.Add(lFig);
        dc.DrawGeometry(null, thinPen, leftEye);

        var rightEye = new PathGeometry();
        var rFig = new PathFigure { StartPoint = new Point(68, 82), IsClosed = false };
        rFig.Segments.Add(new QuadraticBezierSegment(new Point(74, 88), new Point(80, 82), true));
        rightEye.Figures.Add(rFig);
        dc.DrawGeometry(null, thinPen, rightEye);

        // Tiny sleep bubble
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(140, 186, 230, 253)), new Pen(accentCyan, 1.8), new Point(72, 95), 6, 6);

        // Floating Zzz text
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var z1 = new FormattedText("Z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 14, accentCyan, 1.0);
        var z2 = new FormattedText("z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11, accentCyan, 1.0);
        dc.DrawText(z1, new Point(98, 44));
        dc.DrawText(z2, new Point(111, 34));
    }

    private static BitmapSource BakeVisual(DrawingVisual visual, int size)
    {
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
