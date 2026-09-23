using System;
using System.Diagnostics;
using SCDEFogOfWar;

internal static class RadarUnitMarkersTests
{
    private static int assertions;
    private static void Check(bool ok, string name)
    {
        assertions++;
        if (!ok) throw new Exception(name);
    }
    private static byte[] Background(int size)
    {
        byte[] pixels = new byte[size * size * 4 + 32];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 99;
        return pixels;
    }
    private static void Pixel(byte[] pixels, int size, int x, int y, int b, int g, int r, string name)
    {
        int i = (y * size + x) * 4;
        Check(pixels[i] == b && pixels[i + 1] == g && pixels[i + 2] == r && pixels[i + 3] == 255, name);
    }
    public static void Main()
    {
        const int n = 25;
        int[] indices = new int[n * n];
        float[] visible = { 1f, 0f };
        var markers = new RadarUnitMarkers();
        var pixels = Background(n);
        markers.Begin(n);
        markers.Add(.5f, .5f, true, false);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 12, 12, 255, 200, 50, "friendly bright blue BGRA");
        Pixel(pixels, n, 13, 13, 255, 200, 50, "3x3 fill");
        Pixel(pixels, n, 14, 14, 0, 0, 0, "black border");
        Check(pixels[(12 * n + 15) * 4] == 99, "outside unchanged");
        markers.Begin(n); pixels = Background(n);
        markers.Add(.5f, .5f, false, false);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Check(Array.TrueForAll(pixels, b => b == 99), "unseen enemy never stamps even if edge is visible");
        markers.Add(.5f, .5f, false, true);
        indices[12 * n + 14] = 1;
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 12, 12, 48, 48, 255, "enemy red");
        Check(pixels[(12 * n + 14) * 4] == 99, "enemy outline clipped by fog");
        markers.Begin(n); pixels = Background(n);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Check(Array.TrueForAll(pixels, b => b == 99), "new frame has no dead or moved ghosts");
        markers.Add(0, 0, true, true); markers.Add(1, 1, false, true);
        markers.Add(-.01f, .5f, true, true); markers.Add(float.NaN, .5f, true, true);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 0, 0, 255, 200, 50, "first edge");
        Pixel(pixels, n, n - 1, n - 1, 48, 48, 255, "last edge");
        for (int i = n * n * 4; i < pixels.Length; i++) Check(pixels[i] == 99, "capacity beyond active image untouched");
        markers.Begin(n); pixels = Background(n);
        markers.Add(.5f, .5f, true, true);
        markers.Add(.5f, .5f, false, true);
        indices[12 * n + 12] = 1;
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 12, 12, 255, 200, 50, "friendly survives overlapping hidden enemy");
        Pixel(pixels, n, 13, 12, 48, 48, 255, "visible enemy priority in overlap");
        markers.Clear(); pixels = Background(n);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Check(Array.TrueForAll(pixels, b => b == 99), "map reset clears markers");

        markers.Begin(n); pixels = Background(n);
        markers.Add(.25f, .5f, true, true, true);
        markers.Add(.75f, .5f, true, true, false);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 6, 12, 255, 255, 255, "selected own unit white");
        Pixel(pixels, n, 8, 12, 0, 0, 0, "selected black outline");
        Pixel(pixels, n, 18, 12, 255, 200, 50, "unselected blue");
        markers.Begin(n); pixels = Background(n);
        markers.Add(.25f, .5f, true, true, false);
        markers.Add(.75f, .5f, true, true, true);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 6, 12, 255, 200, 50, "previous selection returns to blue");
        Pixel(pixels, n, 18, 12, 255, 255, 255, "new selection white");
        markers.Begin(n); pixels = Background(n);
        markers.Add(.75f, .5f, true, true, false);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 18, 12, 255, 200, 50, "deselect clears white fill");
        markers.Begin(n); pixels = Background(n);
        markers.Add(.5f, .5f, true, true, true);
        markers.Add(.5f, .5f, true, true, false);
        markers.Add(.5f, .5f, false, true, false);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 12, 12, 255, 255, 255, "selected friendly survives fog edge");
        Pixel(pixels, n, 13, 12, 255, 255, 255, "selected white has priority in a crowd");
        markers.Begin(n); pixels = Background(n);
        markers.Add(.5f, .5f, false, false, true);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Check(Array.TrueForAll(pixels, b => b == 99), "selection cannot reveal hidden enemy");
        markers.Add(.5f, .5f, false, true, true);
        markers.Paint(pixels, n, n, indices, visible, .35f);
        Pixel(pixels, n, 13, 12, 48, 48, 255, "enemy never gets selected white");

        // Round-trip through the same isometric affine projection at 4 rotations
        // and 3 map sizes. No screen-coordinate Y flip is involved.
        foreach (int size in new[] { 296, 396, 796 })
        for (int rotation = 0; rotation < 4; rotation++)
        {
            float ux = size, uy = size * .5f, vx = -size, vy = size * .5f;
            for (int r = 0; r < rotation; r++)
            { float temp = ux; ux = -vx; vx = temp; temp = uy; uy = -vy; vy = temp; }
            float minX = Math.Min(0, Math.Min(ux, Math.Min(vx, ux + vx)));
            float maxX = Math.Max(0, Math.Max(ux, Math.Max(vx, ux + vx)));
            float minY = Math.Min(0, Math.Min(uy, Math.Min(vy, uy + vy)));
            float maxY = Math.Max(0, Math.Max(uy, Math.Max(vy, uy + vy)));
            var projection = new RadarMarkerProjection {
                StartX = 13, StartY = 17, SpanX = size - 1, SpanY = size - 1,
                P00X = 0, P00Y = 0,
                UX = ux, UY = uy, VX = vx, VY = vy,
                MinX = minX + (maxX - minX) * .25f, MinY = minY + (maxY - minY) * .25f,
                Width = (maxX - minX) * .5f, Height = (maxY - minY) * .5f
            };
            foreach (float px in new[] { .1f, .5f, .9f })
            foreach (float py in new[] { .1f, .5f, .9f })
            {
                float wx = projection.MinX + px * projection.Width;
                float wy = projection.MinY + py * projection.Height;
                float determinant = ux * vy - uy * vx;
                float u = (wx * vy - wy * vx) / determinant;
                float v = (ux * wy - uy * wx) / determinant;
                float x, y;
                Check(projection.TryProject(13 + u * (size - 1), 17 + v * (size - 1), out x, out y), "project valid");
                Check(Math.Abs(x - px) < .00001f && Math.Abs(y - py) < .00001f, "projection roundtrip");
            }
            int[] fog = new int[size * size];
            byte[] buffer = Background(size);
            markers.Begin(124); markers.Add(.5f, .5f, true, true);
            markers.Paint(buffer, size, size, fog, visible, .35f);
            Pixel(buffer, size, size / 2, size / 2, 255, 200, 50, "map size independent centre");
        }
        // Repeated dense groups use the small marker mask, no per-unit texture
        // stamping. Timing is a managed microbenchmark, not an in-game FPS claim.
        int[] stressFog = new int[796 * 796]; byte[] stressPixels = Background(796);
        Stopwatch watch = Stopwatch.StartNew();
        for (int run = 0; run < 20; run++)
        {
            markers.Begin(124);
            for (int i = 0; i < 10000; i++) markers.Add((i % 121) / 120f, (i / 121 % 121) / 120f, i % 2 == 0, true);
            markers.Paint(stressPixels, 796, 796, stressFog, visible, .35f);
        }
        Console.WriteLine("RADAR_UNIT_MARKERS_OK assertions=" + assertions + " dense10000AverageMs=" + watch.Elapsed.TotalMilliseconds / 20);
    }
}
