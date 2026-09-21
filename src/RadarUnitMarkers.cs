using System;

namespace SCDEFogOfWar
{
    // Same affine projection/crop as the fog index map; coordinates are texture
    // rows, not Noesis top-origin mouse coordinates. No extra Y flip belongs here.
    internal struct RadarMarkerProjection
    {
        internal float StartX, StartY, SpanX, SpanY;
        internal float P00X, P00Y, UX, UY, VX, VY;
        internal float MinX, MinY, Width, Height;

        internal bool TryProject(float logicX, float logicY, out float x, out float y)
        {
            x = y = 0;
            if (SpanX <= 0 || SpanY <= 0 || Width <= 0 || Height <= 0) return false;
            float u = (logicX - StartX) / SpanX;
            float v = (logicY - StartY) / SpanY;
            x = (P00X + u * UX + v * VX - MinX) / Width;
            y = (P00Y + u * UY + v * VY - MinY) / Height;
            return x >= 0 && x <= 1 && y >= 0 && y <= 1;
        }
    }

    // Rasterize at the displayed radar size first, so a dense group costs a few
    // small stamps, not N large texture-space squares. Bit unions keep outlines
    // from erasing other units' fills and preserve friendly pixels at fog edges.
    internal sealed class RadarUnitMarkers
    {
        private byte[] _mask;
        private int _size;

        internal void Begin(int displaySize)
        {
            int size = Math.Max(1, Math.Min(512, displaySize));
            if (_mask == null || _size != size)
            {
                _size = size;
                _mask = new byte[size * size];
            }
            else Array.Clear(_mask, 0, _mask.Length);
        }

        internal void Clear()
        {
            _mask = null;
            _size = 0;
        }

        internal void Add(float x, float y, bool friendly, bool currentlyVisible)
        {
            if (_mask == null || !(x >= 0 && x <= 1 && y >= 0 && y <= 1) ||
                (!friendly && !currentlyVisible)) return;
            int cx = (int)Math.Round(x * (_size - 1));
            int cy = (int)Math.Round(y * (_size - 1));
            for (int dy = -2; dy <= 2; dy++)
            {
                int row = cy + dy;
                if (row < 0 || row >= _size) continue;
                for (int dx = -2; dx <= 2; dx++)
                {
                    int col = cx + dx;
                    if (col < 0 || col >= _size) continue;
                    bool fill = Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1;
                    _mask[row * _size + col] |= (byte)(friendly ? (fill ? 4 : 1) : (fill ? 8 : 2));
                }
            }
        }

        internal void Paint(byte[] bgra, int width, int height, int[] fogIndices,
            float[] visible, float threshold)
        {
            if (_mask == null) return;
            for (int row = 0; row < _size; row++)
            {
                int y0 = (row * height + _size - 1) / _size;
                int y1 = ((row + 1) * height + _size - 1) / _size;
                for (int col = 0; col < _size; col++)
                {
                    int flags = _mask[row * _size + col];
                    if (flags == 0) continue;
                    int x0 = (col * width + _size - 1) / _size;
                    int x1 = ((col + 1) * width + _size - 1) / _size;
                    for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int pixel = y * width + x;
                        int fog = fogIndices[pixel];
                        if (fog < 0 || fog >= visible.Length) continue;
                        // An enemy centre AND each painted fragment need current
                        // vision. Explored history must never reveal enemy motion.
                        int allowed = visible[fog] >= threshold ? flags : flags & 5;
                        if (allowed == 0) continue;
                        int offset = pixel * 4;
                        bool enemyFill = (allowed & 8) != 0;
                        bool friendFill = !enemyFill && (allowed & 4) != 0;
                        // Bright blue #32C8FF, red #FF3030, opaque black outline.
                        bgra[offset] = (byte)(enemyFill ? 48 : friendFill ? 255 : 0);
                        bgra[offset + 1] = (byte)(enemyFill ? 48 : friendFill ? 200 : 0);
                        bgra[offset + 2] = (byte)(enemyFill ? 255 : friendFill ? 50 : 0);
                        bgra[offset + 3] = 255;
                    }
                }
            }
        }
    }
}
