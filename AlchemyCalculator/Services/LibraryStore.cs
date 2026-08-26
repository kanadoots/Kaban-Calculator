using System.IO;
using System.Text.Json;
using AlchemyCalculator.Models;

namespace AlchemyCalculator.Services;

public sealed class LibraryStore
{
    // Keep the editable library beside the published executable so a portable
    // folder contains both the app and the user's saved library.
    private readonly string _userFile = Path.Combine(AppContext.BaseDirectory, "library.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public LibraryData Data { get; private set; } = new();
    public string FilePath => _userFile;

    public void Load()
    {
        if (File.Exists(_userFile))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(_userFile), JsonOptions);
                if (loaded is not null)
                {
                    loaded.Recipes ??= [];
                    loaded.RawIngredients ??= [];
                    if (loaded.Recipes.Count > 0 || loaded.RawIngredients.Count > 0)
                    {
                        Data = loaded;
                        return;
                    }
                }
            }
            catch (JsonException)
            {
                // Rebuild from the bundled defaults if a partial/empty file was
                // left behind by an interrupted first launch.
            }
        }

        LoadDefaults();
        try
        {
            Save();
        }
        catch (UnauthorizedAccessException)
        {
            // The app can still open with bundled defaults. A later save will
            // show the user that the executable folder must be writable.
        }
        catch (IOException)
        {
            // Defer the actionable save error to the UI's save operation.
        }
    }

    private void LoadDefaults()
    {
        var defaultFile = Path.Combine(AppContext.BaseDirectory, "Assets", "default-library.json");
        using var stream = File.OpenRead(defaultFile);
        Data = JsonSerializer.Deserialize<LibraryData>(stream, JsonOptions) ?? new LibraryData();
        Data.Recipes ??= [];
        Data.RawIngredients ??= [];
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_userFile)!);
        File.WriteAllText(_userFile, JsonSerializer.Serialize(Data, JsonOptions));
    }
}