using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AlchemyCalculator.Models;
using AlchemyCalculator.Services;

namespace AlchemyCalculator;

public partial class MainWindow : Window
{
    private readonly LibraryStore _store = new();
    private CalculatorEngine _engine = null!;
    private Recipe? _editingRecipe;
    private Recipe? _editingOriginalRecipe;
    private RawIngredient? _editingRaw;
    private bool _isFilteringTargets;
    private bool _highlightNeeded = true;
    private bool _highlightOutput = true;
    private readonly HashSet<string> _highlightedRecipes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _neededItems = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedBreakdownItem = "";
    private static readonly Brush SelectedRowBrush = new SolidColorBrush(Color.FromRgb(0x07, 0x58, 0x85));
    private static readonly Brush RelatedRowBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x2F, 0x00));
    private static readonly Brush NeededRowBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x5A, 0x3A));
    public ObservableCollection<string> IngredientOptions { get; } = [];

    private static readonly Dictionary<int, string> TierNames = new()
    {
        [7] = "T7: HARMONY DRAUGHT", [6] = "T6: DRAUGHTS", [5] = "T5: ELIXIRS",
        [4] = "T4: OILS", [3] = "T3: BLOODS", [2] = "T2: REAGENTS", [1] = "T1: RAW ING."
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _store.Load();
        _engine = new CalculatorEngine(_store.Data);
        TargetCombo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(TargetCombo_TextChanged));
        RecipeTierCombo.ItemsSource = Enumerable.Range(2, 6).Reverse().ToList();
        RefreshEverything();
        TargetCombo.Text = "Harmony Draught";
        Calculate_Click(this, new RoutedEventArgs());
    }

    private void RefreshEverything()
    {
        _engine = new CalculatorEngine(_store.Data);
        RefreshIngredientOptions();
        TargetCombo.ItemsSource = _engine.AllItems;
        RecipeList.ItemsSource = null;
        RecipeList.ItemsSource = _store.Data.Recipes.OrderBy(x => x.Name).ToList();
        RawList.ItemsSource = null;
        RawList.ItemsSource = _store.Data.RawIngredients.OrderBy(x => x.Name).ToList();
        StatusText.Text = $"{_store.Data.Recipes.Count} recipes · {_store.Data.RawIngredients.Count} raw ingredients";
        if (_editingRecipe is not null) UpdateMissingIngredientWarning();
        if (RecipeSearchBox is not null && !string.IsNullOrWhiteSpace(RecipeSearchBox.Text))
            RecipeSearchBox_TextChanged(RecipeSearchBox, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
        if (RawSearchBox is not null && !string.IsNullOrWhiteSpace(RawSearchBox.Text))
            RawSearchBox_TextChanged(RawSearchBox, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    }

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        var target = TargetCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show("Choose a target item.", "Invalid target", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!long.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0)
        {
            MessageBox.Show("Quantity must be a positive whole number.", "Invalid quantity", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var totals = _engine.Calculate(target, quantity);
            var rows = new ObservableCollection<BreakdownRow>();
            rows.Add(new BreakdownRow
            {
                TierLabel = TierNames.GetValueOrDefault(_engine.GetTier(target), "ITEM"),
                ItemName = target,
                Quantity = quantity,
                QuantityLabel = $"{quantity:N0}",
                IsMissing = !IsKnownItem(target),
                IsHeader = true
            });
            foreach (var tier in Enumerable.Range(1, 7).Reverse())
            {
                if (totals[tier].Count == 0) continue;
                rows.Add(new BreakdownRow { TierLabel = TierNames[tier], QuantityLabel = "-", Level = 1, IsHeader = true });
                foreach (var item in totals[tier].OrderBy(x => x.Key))
                    rows.Add(new BreakdownRow { ItemName = item.Key, Quantity = item.Value, QuantityLabel = $"{item.Value:N0}",
                        IsMissing = !IsKnownItem(item.Key), Level = 2 });
            }
            BreakdownGrid.ItemsSource = rows;
            SetHighlightState(target);
            if (rows.Count > 0) BreakdownGrid.SelectedIndex = 0;
            ApplyBreakdownHighlights();
            ShowDetails(target, quantity, totals);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot calculate", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowDetails(string itemName, long quantity, Dictionary<int, Dictionary<string, long>> totals)
    {
        var recipe = _store.Data.Recipes.FirstOrDefault(x => x.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        DetailItemName.Text = itemName;
        DetailItemName.Foreground = IsKnownItem(itemName) ? Brushes.White : Brushes.IndianRed;
        DetailTier.Text = $"Tier {_engine.GetTier(itemName)}";
        DetailTotalOutput.Text = $"Total output: {quantity:N0}x";
        RecipeIngredientsGrid.Children.RemoveRange(3, Math.Max(0, RecipeIngredientsGrid.Children.Count - 3));
        while (RecipeIngredientsGrid.RowDefinitions.Count > 1)
            RecipeIngredientsGrid.RowDefinitions.RemoveAt(RecipeIngredientsGrid.RowDefinitions.Count - 1);
        DetailBaseMaterial.Text = "";
        if (recipe is null)
        {
            DetailRecipeHeading.Text = "Base material";
            DetailBaseMaterial.Text = $"Total needed: {totals.SelectMany(x => x.Value).Where(x => x.Key == itemName).Sum(x => x.Value):N0}x";
        }
        else
        {
            DetailRecipeHeading.Text = $"Recipe ingredients  ·  ×{quantity:N0} crafts";
            foreach (var ingredient in recipe.Ingredients)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.Children.Add(new TextBlock { Text = ingredient.Name, TextWrapping = TextWrapping.Wrap });
                var perCraft = new TextBlock { Text = $"{ingredient.Quantity:N0}x", HorizontalAlignment = HorizontalAlignment.Right };
                var scaled = new TextBlock { Text = $"{ingredient.Quantity * quantity:N0}x", HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(perCraft, 1); Grid.SetColumn(scaled, 2);
                row.Children.Add(perCraft); row.Children.Add(scaled);
                RecipeIngredientsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(row, RecipeIngredientsGrid.RowDefinitions.Count - 1);
                RecipeIngredientsGrid.Children.Add(row);
            }
        }
        UpdateUsage(itemName, targetQuantity: quantity, totals);
    }

    private void UpdateUsage(string selectedItem, long targetQuantity, Dictionary<int, Dictionary<string, long>> totals)
    {
        var usage = new StringBuilder();
        usage.AppendLine($"INGREDIENT: {selectedItem}");
        usage.AppendLine(new string('=', 34));
        usage.AppendLine();
        var parents = new List<(string Name, int Ratio, long Crafts)>();
        foreach (var recipe in _store.Data.Recipes)
        {
            var matching = recipe.Ingredients.Where(x => x.Name.Equals(selectedItem, StringComparison.OrdinalIgnoreCase));
            foreach (var ingredient in matching)
            {
                var parentCrafts = recipe.Name.Equals(TargetCombo.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? targetQuantity
                    : totals.SelectMany(x => x.Value).Where(x => x.Key.Equals(recipe.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).FirstOrDefault();
                parents.Add((recipe.Name, ingredient.Quantity, parentCrafts));
            }
        }
        if (parents.Count == 0)
        {
            usage.AppendLine("Not consumed by another active recipe.");
        }
        else
        {
            var activeRecipeNames = new HashSet<string>(
                totals.SelectMany(x => x.Value.Keys), StringComparer.OrdinalIgnoreCase);
            parents = parents.Where(x => x.Name.Equals(TargetCombo.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                                         || activeRecipeNames.Contains(x.Name)).ToList();
            if (parents.Count == 0)
            {
                usage.AppendLine("Not consumed by another active recipe.");
            }
            else
            {
                usage.AppendLine($"Used in {parents.Count} recipe(s):");
                usage.AppendLine();
                foreach (var parent in parents)
                {
                    usage.AppendLine($"- Used in:          {parent.Name}");
                    usage.AppendLine($"  - Recipe Ratio:   {parent.Ratio,8:N0}x per craft");
                    usage.AppendLine($"  - Parent Craft Qty:{parent.Crafts,8:N0}x");
                    usage.AppendLine($"  - Total Consumed: {parent.Ratio * parent.Crafts,8:N0}x");
                    usage.AppendLine();
                }
            }
        }
        UsageDetails.Text = usage.ToString();
    }

    private void TargetCombo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isFilteringTargets || !IsLoaded) return;
        var query = TargetCombo.Text.Trim();
        var matches = _engine.AllItems.Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _isFilteringTargets = true;
        TargetCombo.ItemsSource = matches;
        TargetCombo.Text = query;
        TargetCombo.IsDropDownOpen = query.Length > 0 && matches.Count > 0;
        _isFilteringTargets = false;
    }

    private void RefreshIngredientOptions()
    {
        IngredientOptions.Clear();
        foreach (var item in _engine.AllItems)
            IngredientOptions.Add(item);
    }

    private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !string.IsNullOrWhiteSpace(TargetCombo.Text)) Calculate_Click(sender, e);
    }

    private void CalculateField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Calculate_Click(sender, e);
            e.Handled = true;
        }
    }

    private void InputHighlightButton_Click(object sender, RoutedEventArgs e)
    {
        _highlightNeeded = !_highlightNeeded;
        if (sender is Button btn)
        {
            btn.Content = $"Input Highlight: {(_highlightNeeded ? "ON" : "OFF")}";
        }
        ApplyBreakdownHighlights();
    }

    private void OutputHighlightButton_Click(object sender, RoutedEventArgs e)
    {
        _highlightOutput = !_highlightOutput;
        if (sender is Button btn)
        {
            btn.Content = $"Output Highlight: {(_highlightOutput ? "ON" : "OFF")}";
        }
        ApplyBreakdownHighlights();
    }

    private void BreakdownGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BreakdownGrid.SelectedItem is BreakdownRow row && !string.IsNullOrWhiteSpace(row.ItemName))
        {
            _selectedBreakdownItem = row.ItemName;
            SetHighlightState(row.ItemName);
            _highlightedRecipes.Clear();
            foreach (var recipe in _store.Data.Recipes)
                if (recipe.Ingredients.Any(x => x.Name.Equals(row.ItemName, StringComparison.OrdinalIgnoreCase)))
                    _highlightedRecipes.Add(recipe.Name);
            var qty = long.TryParse(QuantityBox.Text, out var parsed) ? parsed : 1;
            try { ShowDetails(row.ItemName, qty, _engine.Calculate(TargetCombo.Text.Trim(), qty)); } catch { }
            BreakdownGrid.UpdateLayout();
            Dispatcher.BeginInvoke(ApplyBreakdownHighlights, DispatcherPriority.Loaded);
        }
    }

    private void BreakdownGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not BreakdownRow row || string.IsNullOrWhiteSpace(row.ItemName)) return;
        var recipe = _store.Data.Recipes.FirstOrDefault(x => x.Name.Equals(row.ItemName, StringComparison.OrdinalIgnoreCase));
        var raw = _store.Data.RawIngredients.FirstOrDefault(x => x.Name.Equals(row.ItemName, StringComparison.OrdinalIgnoreCase));
        var notes = recipe?.Notes ?? raw?.Notes;
        SetBreakdownRowAppearance(e.Row, row);
        if (!string.IsNullOrWhiteSpace(notes))
            e.Row.ToolTip = notes;
    }

    private void IngredientGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is Ingredient ingredient)
            e.Row.Foreground = !string.IsNullOrWhiteSpace(ingredient.Name) && !IsKnownItem(ingredient.Name)
                ? Brushes.IndianRed : Brushes.White;
    }

    private void IngredientGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;

        string name = string.Empty;
        if (e.EditingElement is ComboBox combo)
            name = combo.Text.Trim();
        else if (e.EditingElement is TextBox textBox)
            name = textBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name)) return;

        if (!IsKnownItem(name))
        {
            e.Cancel = true;
            MessageBox.Show("Choose an existing recipe or raw ingredient.", "Missing ingredient/recipe",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyBreakdownHighlights()
    {
        for (var index = 0; index < BreakdownGrid.Items.Count; index++)
        {
            if (BreakdownGrid.ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow gridRow ||
                BreakdownGrid.Items[index] is not BreakdownRow row) continue;
            SetBreakdownRowAppearance(gridRow, row);
        }
    }

    private void SetBreakdownRowAppearance(DataGridRow rowContainer, BreakdownRow row)
    {
        var isSelected = row.ItemName.Equals(_selectedBreakdownItem, StringComparison.OrdinalIgnoreCase);
        var isNeeded = _highlightNeeded && _neededItems.Contains(row.ItemName);
        var isRelated = _highlightOutput && _highlightedRecipes.Contains(row.ItemName);
        var background = isSelected ? SelectedRowBrush :
            isNeeded ? NeededRowBrush : isRelated ? RelatedRowBrush : Brushes.Transparent;
        var foreground = row.IsMissing ? Brushes.IndianRed : Brushes.White;
        rowContainer.Background = background;
        rowContainer.Foreground = foreground;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(rowContainer); i++)
        {
            if (VisualTreeHelper.GetChild(rowContainer, i) is not DependencyObject child) continue;
            ApplyCellAppearance(child, background, foreground);
        }
    }

    private void SetHighlightState(string itemName)
    {
        _selectedBreakdownItem = itemName;
        _neededItems.Clear();
        var recipe = _store.Data.Recipes.FirstOrDefault(x =>
            x.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        if (recipe is not null)
            foreach (var ingredient in recipe.Ingredients.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
                _neededItems.Add(ingredient.Name);
    }

    private static void ApplyCellAppearance(DependencyObject element, Brush background, Brush foreground)
    {
        if (element is DataGridCell cell)
        {
            cell.Background = background;
            cell.Foreground = foreground;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            ApplyCellAppearance(VisualTreeHelper.GetChild(element, i), background, foreground);
    }

    private bool IsKnownItem(string name) =>
        _store.Data.Recipes.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
        _store.Data.RawIngredients.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void RecipeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecipeList.SelectedItem is not Recipe recipe) return;
        _editingOriginalRecipe = recipe;
        _editingRecipe = new Recipe
        {
            Name = recipe.Name,
            Tier = recipe.Tier,
            Notes = recipe.Notes,
            Ingredients = recipe.Ingredients
                .Select(x => new Ingredient { Name = x.Name, Quantity = x.Quantity }).ToList()
        };
        RecipeEditorPanel.IsEnabled = false;
        RecipeNotesPanel.IsEnabled = false;
        RecipeNameBox.Text = recipe.Name;
        RecipeTierCombo.SelectedItem = recipe.Tier;
        NotesBox.Text = recipe.Notes;
        EnsureIngredientRows(_editingRecipe);
        IngredientGrid.ItemsSource = null;
        IngredientGrid.ItemsSource = _editingRecipe.Ingredients;
        UpdateMissingIngredientWarning();
    }

    private void NewRecipe_Click(object sender, RoutedEventArgs e)
    {
        _editingOriginalRecipe = null;
        _editingRecipe = new Recipe { Name = "", Tier = 2, Ingredients = [] };
        EnsureIngredientRows(_editingRecipe);
        RecipeEditorPanel.IsEnabled = true;
        RecipeNotesPanel.IsEnabled = true;
        RecipeNameBox.Text = "";
        RecipeTierCombo.SelectedItem = 2;
        NotesBox.Text = "";
        IngredientGrid.ItemsSource = null;
        IngredientGrid.ItemsSource = _editingRecipe.Ingredients;
        UpdateMissingIngredientWarning();
        MainTabs.SelectedIndex = 1;
        RecipeNameBox.Focus();
    }

    private void EditRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRecipe is not null)
        {
            RecipeEditorPanel.IsEnabled = true;
            RecipeNotesPanel.IsEnabled = true;
            RecipeNameBox.Focus();
        }
    }

    private static void EnsureIngredientRows(Recipe recipe)
    {
        while (recipe.Ingredients.Count < 5)
            recipe.Ingredients.Add(new Ingredient { Quantity = 0 });
        if (recipe.Ingredients.Count > 5)
            recipe.Ingredients = recipe.Ingredients.Take(5).ToList();
    }

    private void UpdateMissingIngredientWarning()
    {
        var missing = _editingRecipe?.Ingredients.Any(x =>
            !string.IsNullOrWhiteSpace(x.Name) && !IsKnownItem(x.Name)) == true;
        MissingIngredientWarning.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddIngredient_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRecipe is null) return;
        if (_editingRecipe.Ingredients.Count >= 5)
        {
            MessageBox.Show("Each recipe can contain a maximum of 5 ingredients.", "Ingredient limit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _editingRecipe.Ingredients.Add(new Ingredient());
        IngredientGrid.ItemsSource = null;
        IngredientGrid.ItemsSource = _editingRecipe.Ingredients;
    }

    private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editingRecipe is not null && IsLoaded) _editingRecipe.Notes = NotesBox.Text;
    }

    private void SaveRecipe_Click(object sender, RoutedEventArgs e)
    {
        var name = RecipeNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || _editingRecipe is null) return;
        if (!RecipeEditorPanel.IsEnabled) return;
        if (RecipeTierCombo.SelectedItem is not int tier || tier is < 2 or > 7)
        {
            MessageBox.Show("Choose a recipe tier from 2 through 7.", "Invalid tier",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = _store.Data.Recipes.FirstOrDefault(x => !ReferenceEquals(x, _editingOriginalRecipe) &&
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            MessageBox.Show($"A recipe with the name '{name}' already exists. Please choose a unique name.",
                "Duplicate Recipe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var oldName = _editingRecipe.Name;
        _editingRecipe.Name = name;
        _editingRecipe.Tier = tier;
        _editingRecipe.Notes = NotesBox.Text.Trim();
        _editingRecipe.Ingredients = _editingRecipe.Ingredients
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Quantity > 0).ToList();

        var allowedItems = new HashSet<string>(IngredientOptions, StringComparer.OrdinalIgnoreCase);
        if (_editingRecipe.Ingredients.Any(x => !allowedItems.Contains(x.Name)))
        {
            MessageBox.Show("Every ingredient must be selected from the existing recipe or raw ingredient library.",
                "Invalid ingredient", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_editingRecipe.Ingredients.Count > 5)
        {
            MessageBox.Show("Each recipe can contain a maximum of 5 ingredients.", "Ingredient limit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_editingOriginalRecipe is not null)
        {
            _editingOriginalRecipe.Name = _editingRecipe.Name;
            _editingOriginalRecipe.Tier = _editingRecipe.Tier;
            _editingOriginalRecipe.Notes = _editingRecipe.Notes;
            _editingOriginalRecipe.Ingredients = _editingRecipe.Ingredients;
            _editingRecipe = _editingOriginalRecipe;
        }
        else if (!_store.Data.Recipes.Contains(_editingRecipe))
        {
            _store.Data.Recipes.Add(_editingRecipe);
        }

        if (!string.IsNullOrWhiteSpace(oldName) && !oldName.Equals(name, StringComparison.OrdinalIgnoreCase))
            foreach (var recipe in _store.Data.Recipes)
                foreach (var ingredient in recipe.Ingredients)
                    if (ingredient.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase)) ingredient.Name = name;

        if (!TrySaveLibrary()) return;
        RefreshEverything();
        RecipeEditorPanel.IsEnabled = false;
        RecipeNotesPanel.IsEnabled = false;
        MessageBox.Show("Recipe saved to the library.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRecipe is null) return;
        if (MessageBox.Show($"Delete '{_editingRecipe.Name}'?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (_editingOriginalRecipe is not null)
            _store.Data.Recipes.Remove(_editingOriginalRecipe);
        if (!TrySaveLibrary()) return;
        _editingRecipe = null;
        _editingOriginalRecipe = null;
        RefreshEverything();
        RecipeEditorPanel.IsEnabled = false;
        RecipeNotesPanel.IsEnabled = false;
    }

    private void RawList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RawList.SelectedItem is not RawIngredient raw) return;
        _editingRaw = raw;
        RawNameBox.Text = raw.Name;
        RawNotesBox.Text = raw.Notes;
    }

    private void NewRaw_Click(object sender, RoutedEventArgs e)
    {
        _editingRaw = new RawIngredient { Name = "New Raw Ingredient", Tier = 1 };
        RawNameBox.Text = _editingRaw.Name;
        RawNotesBox.Text = "";
        MainTabs.SelectedIndex = 2;
    }

    private void RawNotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editingRaw is not null && IsLoaded) _editingRaw.Notes = RawNotesBox.Text;
    }

    private void SaveRaw_Click(object sender, RoutedEventArgs e)
    {
        var name = RawNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || _editingRaw is null) return;
        if (_store.Data.RawIngredients.Any(x => !ReferenceEquals(x, _editingRaw) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A raw ingredient with that name already exists.", "Duplicate name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var oldName = _editingRaw.Name;
        _editingRaw.Name = name;
        _editingRaw.Tier = 1;
        _editingRaw.Notes = RawNotesBox.Text.Trim();
        if (!_store.Data.RawIngredients.Contains(_editingRaw)) _store.Data.RawIngredients.Add(_editingRaw);
        if (!oldName.Equals(name, StringComparison.OrdinalIgnoreCase))
            foreach (var ingredient in _store.Data.Recipes.SelectMany(x => x.Ingredients))
                if (ingredient.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase)) ingredient.Name = name;
        if (!TrySaveLibrary()) return;
        RefreshEverything();
        MessageBox.Show("Raw ingredient saved to the library.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteRaw_Click(object sender, RoutedEventArgs e)
    {
        if (_editingRaw is null) return;
        if (MessageBox.Show($"Delete '{_editingRaw.Name}'?", "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.Data.RawIngredients.Remove(_editingRaw);
        if (!TrySaveLibrary()) return;
        _editingRaw = null;
        RefreshEverything();
    }

    private void RecipeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = RecipeSearchBox.Text.Trim();
        RecipeList.ItemsSource = _store.Data.Recipes
            .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name).ToList();
    }

    private void RawSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = RawSearchBox.Text.Trim();
        RawList.ItemsSource = _store.Data.RawIngredients
            .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name).ToList();
    }

    private void RefreshLibrary_Click(object sender, RoutedEventArgs e)
    {
        RefreshEverything();
    }

    private static string CsvEscape(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        values.Add(value.ToString());
        return values;
    }

    private void ExportLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "alchemy-library.csv" };
        if (dialog.ShowDialog() != true) return;
        var lines = new List<string>
        {
            "Type,Name,Tier,Notes,Ingredient1,Quantity1,Ingredient2,Quantity2,Ingredient3,Quantity3,Ingredient4,Quantity4,Ingredient5,Quantity5"
        };
        foreach (var recipe in _store.Data.Recipes)
        {
            var fields = new List<string> { "Recipe", recipe.Name, recipe.Tier.ToString(), recipe.Notes };
            for (var i = 0; i < 5; i++)
            {
                var ingredient = recipe.Ingredients.ElementAtOrDefault(i);
                fields.Add(ingredient?.Name ?? "");
                fields.Add(ingredient is null || ingredient.Quantity <= 0 ? "" : ingredient.Quantity.ToString());
            }
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }
        foreach (var raw in _store.Data.RawIngredients)
            lines.Add(string.Join(",", new[] { "Raw", raw.Name, "1", raw.Notes, "", "", "", "", "", "", "", "", "", "" }.Select(CsvEscape)));
        File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
        MessageBox.Show("Complete library exported.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("Importing will replace all recipes and raw ingredients, including notes. Continue?",
            "Confirm library import", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var recipes = new List<Recipe>();
        var rawIngredients = new List<RawIngredient>();
        foreach (var line in File.ReadLines(dialog.FileName, Encoding.UTF8).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);
            if (fields.Count < 4) continue;
            var type = fields[0].Trim();
            if (type.Equals("Recipe", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(fields[2], out var tier) && tier is >= 2 and <= 7 &&
                !string.IsNullOrWhiteSpace(fields[1]))
            {
                var recipe = new Recipe { Name = fields[1], Tier = tier, Notes = fields[3] };
                for (var i = 0; i < 5; i++)
                {
                    var nameIndex = 4 + i * 2;
                    var quantityIndex = nameIndex + 1;
                    if (nameIndex < fields.Count && quantityIndex < fields.Count &&
                        !string.IsNullOrWhiteSpace(fields[nameIndex]) &&
                        int.TryParse(fields[quantityIndex], out var quantity) && quantity > 0)
                        recipe.Ingredients.Add(new Ingredient { Name = fields[nameIndex], Quantity = quantity });
                }
                recipes.Add(recipe);
            }
            else if (type.Equals("Raw", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(fields[1]))
            {
                rawIngredients.Add(new RawIngredient { Name = fields[1], Tier = 1, Notes = fields[3] });
            }
        }
        _store.Data.Recipes = recipes;
        _store.Data.RawIngredients = rawIngredients;
        _editingRecipe = null;
        _editingOriginalRecipe = null;
        _editingRaw = null;
        if (TrySaveLibrary())
        {
            RefreshEverything();
            MessageBox.Show("Complete library imported.", "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportRecipes_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "recipe-library.csv" };
        if (dialog.ShowDialog() != true) return;
        var lines = new List<string> { "Name,Tier,Notes,Ingredient1,Quantity1,Ingredient2,Quantity2,Ingredient3,Quantity3,Ingredient4,Quantity4,Ingredient5,Quantity5" };
        foreach (var recipe in _store.Data.Recipes)
        {
            var fields = new List<string> { recipe.Name, recipe.Tier.ToString(), recipe.Notes };
            for (var i = 0; i < 5; i++)
            {
                var ingredient = recipe.Ingredients.ElementAtOrDefault(i);
                fields.Add(ingredient?.Name ?? "");
                fields.Add(ingredient is null || ingredient.Quantity <= 0 ? "" : ingredient.Quantity.ToString());
            }
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }
        File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
        MessageBox.Show("Recipe library exported.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportRaw_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "raw-ingredients.csv" };
        if (dialog.ShowDialog() != true) return;
        var lines = new List<string> { "Name,Tier,Notes" };
        lines.AddRange(_store.Data.RawIngredients.Select(x =>
            string.Join(",", new[] { x.Name, x.Tier.ToString(), x.Notes }.Select(CsvEscape))));
        File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
        MessageBox.Show("Raw ingredient library exported.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportRecipes_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("Importing will replace the current recipe library. Continue?",
            "Confirm recipe import", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var imported = new List<Recipe>();
        foreach (var line in File.ReadLines(dialog.FileName, Encoding.UTF8).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);
            if (fields.Count < 3 || string.IsNullOrWhiteSpace(fields[0]) || !int.TryParse(fields[1], out var tier) || tier is < 2 or > 7) continue;
            var recipe = new Recipe { Name = fields[0], Tier = tier, Notes = fields[2] };
            for (var i = 0; i < 5; i++)
            {
                var nameIndex = 3 + i * 2;
                var quantityIndex = nameIndex + 1;
                if (nameIndex >= fields.Count || string.IsNullOrWhiteSpace(fields[nameIndex]) ||
                    quantityIndex >= fields.Count || !int.TryParse(fields[quantityIndex], out var quantity) || quantity <= 0) continue;
                recipe.Ingredients.Add(new Ingredient { Name = fields[nameIndex], Quantity = quantity });
            }
            imported.Add(recipe);
        }
        _store.Data.Recipes = imported;
        _editingRecipe = null;
        _editingOriginalRecipe = null;
        RecipeEditorPanel.IsEnabled = false;
        RecipeNotesPanel.IsEnabled = false;
        if (TrySaveLibrary()) { RefreshEverything(); MessageBox.Show("Recipe library imported.", "Import complete", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private void ImportRaw_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        if (MessageBox.Show("Importing will replace the current raw ingredient library. Continue?",
            "Confirm raw ingredient import", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var imported = new List<RawIngredient>();
        foreach (var line in File.ReadLines(dialog.FileName, Encoding.UTF8).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);
            if (fields.Count < 1 || string.IsNullOrWhiteSpace(fields[0])) continue;
            imported.Add(new RawIngredient { Name = fields[0], Tier = 1, Notes = fields.Count > 2 ? fields[2] : "" });
        }
        _store.Data.RawIngredients = imported;
        if (TrySaveLibrary()) { RefreshEverything(); MessageBox.Show("Raw ingredient library imported.", "Import complete", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private bool TrySaveLibrary()
    {
        try
        {
            _store.Save();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"The library folder is not writable:\n{_store.FilePath}\n\nMove the EXE to a writable folder and try again.",
                "Could not save library",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Could not save the library:\n{ex.Message}", "Could not save library",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}