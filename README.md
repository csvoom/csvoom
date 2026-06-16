# CSVoom

CSVoom is a multiplatform desktop application for opening, browsing, searching, and filtering CSV files. It is designed to handle large files efficiently by loading data in ranges, keeping the UI responsive.

Built with **.NET 10**, **C# 14**, and **Avalonia UI**.

## Features

- **Large File Support**: Loads bounded row ranges instead of the entire file into memory.
- **Compressed File Support**: Directly open `.gz` (GZIP) compressed CSV files.
- **Advanced Search**: Search all columns or a specific column using plain text or Regular Expressions.
- **Filtering**: Quick filtering of currently loaded rows.
- **Column Management**: Hide/unhide columns by name or spreadsheet-style letter (A, B, C...).
- **Row Preservation**: Synthetic row-number column that tracks the original source file row numbers.
- **Command Bar**: CLI-like interaction for power users.
- **Export**: Export filtered or selected row ranges to new CSV files.
- **Comparison**: Compare two CSV files and highlight differences.

## Installation & Build

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build from source
```bash
dotnet build
```

### Run the application
```bash
dotnet run --project src/CSVoom.csproj
```

### Run tests
```bash
dotnet test
```

## Usage

1. Open CSVoom.
2. Click **Open CSV** to select a `.csv` or `.gz` file.
3. Use the **Command Bar** at the top to interact with the data.

### Command Reference

| Command | Description | Example |
| :--- | :--- | :--- |
| `load` | Loads a specific range of rows. | `load 1:10000` |
| `find` | Searches for text/regex in all or specific columns. | `find London`, `find /regex/ A` |
| `hide` | Hides one or more columns. | `hide A`, `hide city:email` |
| `unhide` | Restores hidden columns. | `unhide all` |

*Note: Use quotes for paths or values containing spaces.*

## Configuration

CSVoom can be configured via `app.config`. Key settings include:
- `AutoLoadRows`: Default number of rows to load.
- `FirstRowIsHeader`: Whether to treat the first line as a header.
- `Theme`: UI theme (Light/Dark).
- `RegexSearch`: Enable/disable regex detection in the command bar.

## Project Structure

- `src/app/`: Core logic, CSV parsing, and commands.
- `src/ui/`: Avalonia UI views and ViewModels.
- `test/`: Unit tests for the parser and core logic.

## Limitations

- CSV fields with embedded newlines are not currently supported.
- Only comma, semicolon, or tab delimiters are automatically detected.
