namespace BrowserWall.Core.Health;

/// <summary>Color of one sample point.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// "Flat" check: samples a <see cref="GridSize"/>×<see cref="GridSize"/> grid of points;
/// if red, green and blue each vary by no more than <see cref="Tolerance"/>, the window is flat. Takes colors only; never touches titles, URLs or image files.
/// </summary>
public static class FlatCheck
{
    public const int GridSize = 10;

    public const int Tolerance = 8;

    /// <summary>Sample points: column i at x = (2i+1)·width / 20, rows likewise (the center of each small cell). Empty when width or height is not above 0.</summary>
    public static IReadOnlyList<(int X, int Y)> SamplePoints(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Array.Empty<(int, int)>();
        }

        var points = new List<(int X, int Y)>(GridSize * GridSize);
        for (int row = 0; row < GridSize; row++)
        {
            int y = (int)((2L * row + 1) * height / (2 * GridSize));
            for (int col = 0; col < GridSize; col++)
            {
                int x = (int)((2L * col + 1) * width / (2 * GridSize));
                points.Add((x, y));
            }
        }

        return points;
    }

    /// <summary>
    /// True when, across all samples, "max − min" of red, green and blue each is at most <see cref="Tolerance"/>;
    /// <paramref name="color"/> is the first sample (for the diagnostics log). No samples → false.
    /// </summary>
    public static bool IsUniform(IReadOnlyList<Rgb> samples, out Rgb color)
    {
        ArgumentNullException.ThrowIfNull(samples);
        color = default;
        if (samples.Count == 0)
        {
            return false;
        }

        byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        foreach (Rgb s in samples)
        {
            minR = Math.Min(minR, s.R);
            minG = Math.Min(minG, s.G);
            minB = Math.Min(minB, s.B);
            maxR = Math.Max(maxR, s.R);
            maxG = Math.Max(maxG, s.G);
            maxB = Math.Max(maxB, s.B);
        }

        color = samples[0];
        return maxR - minR <= Tolerance && maxG - minG <= Tolerance && maxB - minB <= Tolerance;
    }
}
