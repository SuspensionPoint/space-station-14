namespace Content.Server.MapEditor;

[RegisterComponent]
public sealed partial class EditorMetadataComponent : Component
{
    [DataField("category", required:false)]
    public string Category { get; set; } = string.Empty;
    [DataField("subcategory", required:false)]
    public string Subcategory { get; set; } = string.Empty;
    [DataField("priority", required:false)]
    public int Priority { get; set; } = 0;
}
