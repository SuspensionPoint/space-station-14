using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.MapEditor.UI;

public sealed class EditorCategory
{
    public string CategoryName = default!;

    public List<EditorSubcategory> Subcategories = new();
    public List<EntityPrototype> EntityPrototypes = new();

    public EditorCategory(string categoryName)
    {
        CategoryName = categoryName;
    }
}

public sealed class EditorSubcategory
{
    public string Id = default!;
    public string DisplayName = default!;
    public int Priority;

    public List<EntityPrototype> EntityPrototypes = new();
}
