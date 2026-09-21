namespace SCDEFogOfWar
{
    internal static class RenderBoundsPolicy
    {
        internal static int Restore(int current, int expanded, int original)
        {
            return current == expanded ? original : current;
        }
    }
}
