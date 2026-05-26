using Robust.Shared.Map;

namespace Content.MapEditor.Tools;

public struct PaintTarget
{
    public PaintTargetType Type { get; init; }

    public Tile Tile { get; init; }

    public string? EntityPrototype { get; init; }
}

public enum PaintTargetType
{
    Tile,
    Entity,
}
