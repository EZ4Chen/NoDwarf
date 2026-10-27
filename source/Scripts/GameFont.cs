using Godot;

namespace NoDwarf;

internal static class GameFont
{
    // Ship the same CJK glyphs on every platform instead of relying on OS fonts.
    private static FontFile? _regular;
    public static FontFile Regular => _regular ??= GD.Load<FontFile>(
        "res://Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf");
}
