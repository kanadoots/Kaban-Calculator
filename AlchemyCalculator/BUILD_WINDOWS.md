# Alchemy Calculator — WPF build

This is the Windows rewrite using C# and WPF. It includes:

- Recursive tier breakdown calculations
- Persistent recipe library stored as `library.json` beside the executable
- Recipe creation, deletion, renaming, tier classification, and ingredient editing
- Raw ingredient creation, deletion, renaming, and tier editing
- Automatic reference updates when a recipe or raw ingredient is renamed
- Dark desktop interface with calculator, recipe library, and raw ingredient tabs

## Build

Build on Windows with the .NET 8 SDK:

```powershell
dotnet publish .\AlchemyCalculator\AlchemyCalculator.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish
```

The standalone application will be in `publish\AlchemyCalculator.exe`.
The editable library will be created beside it as `publish\library.json` on
first launch. Keep the EXE in a writable folder if users need to edit and save
the library.