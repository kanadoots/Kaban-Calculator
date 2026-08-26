using AlchemyCalculator.Models;

namespace AlchemyCalculator.Services;

public sealed class CalculatorEngine
{
    private readonly LibraryData _data;
    private readonly Dictionary<string, Recipe> _recipes;
    private readonly Dictionary<string, int> _tiers;

    public CalculatorEngine(LibraryData data)
    {
        _data = data;
        _recipes = data.Recipes.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _tiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in data.Recipes) _tiers[recipe.Name] = recipe.Tier;
        foreach (var raw in data.RawIngredients) _tiers[raw.Name] = raw.Tier;
        foreach (var ingredient in data.Recipes.SelectMany(x => x.Ingredients))
            if (!_tiers.ContainsKey(ingredient.Name)) _tiers[ingredient.Name] = 1;
    }

    public int GetTier(string name) => _tiers.TryGetValue(name, out var tier) ? tier : 1;
    public IReadOnlyList<string> AllItems =>
        _tiers.Keys.OrderByDescending(GetTier).ThenBy(x => x).ToList();

    public Dictionary<int, Dictionary<string, long>> Calculate(string target, long quantity)
    {
        var totals = Enumerable.Range(1, 7).ToDictionary(x => x, _ => new Dictionary<string, long>());
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Recurse(string item, long amount)
        {
            if (!_recipes.TryGetValue(item, out var recipe)) return;
            if (!visiting.Add(item))
                throw new InvalidOperationException($"Circular recipe detected at '{item}'.");

            foreach (var ingredient in recipe.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient.Name) || ingredient.Quantity <= 0) continue;
                var needed = checked(ingredient.Quantity * amount);
                var tier = GetTier(ingredient.Name);
                totals[tier][ingredient.Name] = totals[tier].GetValueOrDefault(ingredient.Name) + needed;
                Recurse(ingredient.Name, needed);
            }
            visiting.Remove(item);
        }

        Recurse(target, quantity);
        return totals;
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in _data.Recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.Name)) errors.Add("A recipe has an empty name.");
            if (!names.Add(recipe.Name)) errors.Add($"Duplicate recipe name: {recipe.Name}");
            if (recipe.Tier < 2 || recipe.Tier > 7) errors.Add($"Recipe '{recipe.Name}' must use Tier 2–7.");
            if (recipe.Ingredients.Count == 0) errors.Add($"Recipe '{recipe.Name}' has no ingredients.");
        }
        return errors;
    }
}