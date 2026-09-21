namespace SCDEFogOfWar
{
    internal static class NativeFogCoordinates
    {
        // Native fine positions address cell edges in eighths. Fog samples address
        // GameToWorld(cell)'s cell centre, so the middle of a cell is integer here.
        internal static float ToCellCentre(int fine, int cell)
        {
            return fine >= 0 && (fine >> 3) == cell
                ? fine / 8f - 0.5f
                : cell;
        }
    }
}
