# CSVoom v1.2.0

CSVoom is a multiplatform desktop application for opening, browsing, searching, and filtering CSV files. It is designed to handle large files efficiently by loading data in ranges, keeping the UI responsive.

Built with **.NET 10**, **C# 14**, and **Avalonia UI**.

## Features

- **Large File Support**: Loads bounded row ranges instead of the entire file into memory.
- **Compressed File Support**: Directly open `.gz` (GZIP) compressed CSV files.
- **Advanced Search**: Search all columns or a specific column using plain text or Regular Expressions.
- **Filtering**: Quick filtering of currently loaded rows.
- **Column Management**: Hide/unhide columns by name or spreadsheet-style letter (A, B, C...).
- **Row Preservation**: Synthetic row-number column that tracks the original source file row numbers.
- **Interactive Panels**: Menus and overlay panels for navigation, searching, and filtering.
- **Export**: Export filtered or selected row ranges to new CSV files.
- **Comparison**: Compare two CSV files and highlight differences. Improved numeric matching now ignores non-numeric formatting characters (e.g., currency symbols, commas).

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
3. Use the **Navigate**, **Find**, and **Visibility** menus at the top to interact with the data.

## Interactive Tools

CSVoom provides several panels for interacting with your data:

- **Navigate**: Quickly jump to a specific row and column.
- **Find**: Search for text or regular expressions in specific columns. Supports multiple criteria.
- **Visibility**: Hide or unhide ranges of columns by name.
- **Comparer**: Open a dedicated view to compare two CSV files.
- **History**: Access previous search results.

### Find Syntax
The **Find** panel supports:
- **Plain Text**: Simple substring matching.
- **Regex**: Enclose search term in slashes (e.g., `/^London/`).
- **Numeric Comparisons**: Use operators like `<`, `>`, `=`, `<=`, `>=` for numeric columns.

## Configuration

CSVoom can be configured via `app.config`. Key settings include:
- `AutoLoadRows`: Default number of rows to load.
- `FirstRowIsHeader`: Whether to treat the first line as a header.
- `Theme`: UI theme (Light/Dark).
- `RegexSearch`: Enable/disable regex detection.

## Project Structure

- `src/app/`: Core logic and CSV parsing.
- `src/ui/`: Avalonia UI views and ViewModels.
- `test/`: Unit tests for the parser and core logic.

## Limitations

- CSV fields with embedded newlines are not currently supported.
- Only comma, semicolon, or tab delimiters are automatically detected.
- The scrollbar may behave erratically