using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Egorik.App;

internal static class TrayIconFactory
{
    public static WindowIcon Create()
    {
        const int iconSize = 32;
        var bitmap = new WriteableBitmap(
            new PixelSize(iconSize, iconSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var frameBuffer = bitmap.Lock();
        var pixels = new byte[frameBuffer.RowBytes * iconSize];

        for (var y = 0; y < iconSize; y++)
        {
            for (var x = 0; x < iconSize; x++)
            {
                var offset = (y * frameBuffer.RowBytes) + (x * 4);
                if (!IsInsideRoundedSquare(x, y, iconSize))
                {
                    continue;
                }

                var gradient = (x + y) / (double)((iconSize - 1) * 2);
                pixels[offset] = (byte)(94 + (82 * gradient));
                pixels[offset + 1] = (byte)(55 + (45 * gradient));
                pixels[offset + 2] = (byte)(49 + (73 * gradient));
                pixels[offset + 3] = byte.MaxValue;

                if (IsInsideSparkle(x, y, iconSize))
                {
                    pixels[offset] = byte.MaxValue;
                    pixels[offset + 1] = (byte)(225 + (20 * gradient));
                    pixels[offset + 2] = (byte)(205 + (40 * gradient));
                }
            }
        }

        Marshal.Copy(pixels, 0, frameBuffer.Address, pixels.Length);
        return new WindowIcon(bitmap);
    }

    private static bool IsInsideRoundedSquare(int x, int y, int size)
    {
        const double inset = 2;
        const double radius = 7;
        var maximum = size - 1 - inset;
        if (x < inset || y < inset || x > maximum || y > maximum)
        {
            return false;
        }

        var cornerX = Math.Clamp(x, inset + radius, maximum - radius);
        var cornerY = Math.Clamp(y, inset + radius, maximum - radius);
        var deltaX = x - cornerX;
        var deltaY = y - cornerY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }

    private static bool IsInsideSparkle(int x, int y, int size)
    {
        var center = (size - 1) / 2d;
        var deltaX = x - center;
        var deltaY = y - center;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        var angle = Math.Atan2(deltaY, deltaX);
        var pointStrength = Math.Pow(Math.Abs(Math.Cos(2 * angle)), 7);
        var radius = 2.6 + (9.5 * pointStrength);
        return distance <= radius;
    }
}
