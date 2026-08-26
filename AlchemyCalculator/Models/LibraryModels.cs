namespace AlchemyCalculator.Models;

public sealed class Ingredient
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
}

public sealed class Recipe
{
    public string Name { get; set; } = "";
    public int Tier { get; set; } = 1;
    public List<Ingredient> Ingredients { get; set; } = [];
    public string Notes { get; set; } = "";
}

public sealed class RawIngredient
{
    public string Name { get; set; } = "";
    public int Tier { get; set; } = 1;
    public string Notes { get; set; } = "";
}

public sealed class LibraryData
{
    public List<Recipe> Recipes { get; set; } = [];
    public List<RawIngredient> RawIngredients { get; set; } = [];
}

public sealed class BreakdownRow
{
    public string TierLabel { get; init; } = "";
    public string ItemName { get; init; } = "";
    public long Quantity { get; init; }
    public string QuantityLabel { get; init; } = "";
    public int Level { get; init; }
    public bool IsHeader { get; init; }
    public bool IsMissing { get; init; }
    public string DisplayName => IsMissing ? $"❗ {ItemName}" : ItemName;
}