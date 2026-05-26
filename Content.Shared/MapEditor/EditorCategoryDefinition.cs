using Robust.Shared.Prototypes;

namespace Content.Shared.MapEditor;

[Prototype("editorCategory")]
public sealed partial class EditorCategoryDefinition : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("priority")]
    public int Priority = 0;

    [DataField("entries")]
    public List<EntProtoId> Entries = new();

    [DataField("name")]
    public string Name = "UntitledCategory";
}
