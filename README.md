# Project Documentation: CSVoom
## Overview
is a multiplatform desktop application for opening, unpacking, browsing, searching, filtering, and inspecting CSV data. **CSVoom**
The application is built with:
- **.NET 10**
- **C# 14**
- **Avalonia UI**
- **xUnit** for tests

CSVoom is designed for working with potentially large CSV files by loading only a bounded range of rows into the UI at a time instead of loading an entire file into memory.
## Features
### CSV viewing
CSVoom can open and display CSV files in a desktop grid interface.
Supported file types:
- `.csv`
- `.gz`

GZIP files are decompressed while reading.
### Large-file friendly loading
The application limits the number of rows displayed at once.
By default, CSVoom displays up to:``` text
10,000 rows
```

This helps keep the UI responsive when working with large files.
Row numbers
CSVoom adds a synthetic row-number column to the grid.
The row number corresponds to the source file row number, including the header row as row 1.
Command bar
The UI includes a command input for quickly interacting with the loaded file.
Supported commands include:``` text
load
find
filter
hide
unhide
```

Column addressing
Columns can be referenced by either:
CSV header name
Spreadsheet-style column letters, such as A, B, C
Row-number column using 1
 
Project Structure``` text
CSVoom/
├── CSVoom.sln
├── README.md
├── src/
│   ├── CSVoom.csproj
│   ├── app/
│   │   └── Parser.cs
│   └── ui/
│       ├── App.axaml
│       ├── App.axaml.cs
│       ├── MainWindow.axaml
│       ├── MainWindow.axaml.cs
│       └── Program.cs
└── test/
    ├── test.csproj
    ├── xunit.runner.json
    └── app/
        └── ParserTests.cs
```

 
Main Components
Application project
Location:``` text
src/CSVoom.csproj
```

The application project is an executable Avalonia desktop application targeting:``` xml
<TargetFramework>net10.0</TargetFramework>
```

Main package dependencies include:
Avalonia
Avalonia.Desktop
Avalonia.Controls.DataGrid
Avalonia.Themes.Fluent
Avalonia.Fonts.Inter
AvaloniaUI.DiagnosticsSupport
Release builds are configured for single-file, self-contained publishing.
 
Parser
Location:``` text
src/app/Parser.cs
```

The parser is responsible for reading CSV data from disk and exposing rows to the UI.
Responsibilities
The parser handles:
Reading headers
Reading row ranges
Reading matching rows
Finding the first matching value
Reading plain CSV files
Reading GZIP-compressed CSV files
Parsing CSV lines with quoted values
Tracking source row numbers
Supported input formats``` text
.csv
.gz
```

Important public members
Headers
Stores the currently loaded CSV headers.``` csharp
public IReadOnlyList<string> Headers { get; private set; }
```

ReadHeadersAsync
Reads the first row of a CSV file and stores it as the header list.``` csharp
Task<IReadOnlyList<string>> ReadHeadersAsync(string filePath)
```

ReadRangeAsync
Reads a bounded range of rows from the file.``` csharp
Task<ObservableCollection<Dictionary<string, string>>> ReadRangeAsync(
    string filePath,
    int startRow,
    int endRow,
    int maxRows)
```

Rows are returned as dictionaries keyed by column header.
Each row also includes a synthetic row-number value using the internal row-number key.
ReadMatchingRowsAsync
Reads rows that match a caller-provided predicate.``` csharp
Task<ObservableCollection<Dictionary<string, string>>> ReadMatchingRowsAsync(
    string filePath,
    Func<Dictionary<string, string>, bool> predicate,
    int maxRows)
```

FindFirstAsync
Finds the first matching value in the file.``` csharp
Task<(Dictionary<string, string> Row, string Header, int RowNumber)?> FindFirstAsync(
    string filePath,
    string searchText,
    string? searchHeader = null)
```

Search is case-insensitive.
If searchHeader is provided, only that column is searched.
 
User Interface
Location:``` text
src/ui/
```

The UI is implemented with Avalonia.
Main window
The main window contains:
Application title
Open CSV button
Unhide all columns button
Command text box
Run button
Status text
Data grid for CSV contents
Primary UI file``` text
src/ui/MainWindow.axaml
```

UI behavior file``` text
src/ui/MainWindow.axaml.cs
```

The main window coordinates:
File picking
Header loading
Grid column creation
Row range loading
Command execution
Search result navigation
Filtering
Hiding and unhiding columns
 
Command Reference
Commands can be typed into the command box and executed by pressing Enter or clicking Run.
 
load
Loads a specific range of rows into the grid.
Syntax``` text
load start:end
```

Example``` text
load 1:10000
```

Loads rows 1 through 10000.
Notes
Row numbers are 1-based.
The application caps the visible row count to the configured maximum.
If the requested range is too large, only the maximum visible row count is loaded.
 
find
Searches for text in the CSV file and scrolls the grid to the first match.
Syntax``` text
find text
```

or:``` text
find text column
```

Examples``` text
find London
```

Searches all columns for London.``` text
find London city
```

Searches only the city column.``` text
find 42 1
```

Searches row numbers for 42.``` text
find error A
```

Searches column A for error.
Notes
Search is case-insensitive.
If a match is found outside the currently visible range, CSVoom loads a range around the matched row.
The matching row is selected and scrolled into view.
 
filter
Filters the currently loaded rows in the grid.
Syntax``` text
filter text
```

``` text
filter columnName
```

``` text
filter clear
```

Examples``` text
filter London
```

Shows currently loaded rows containing London.``` text
filter email
```

If email is a column header, shows rows where that column is not empty and not \N.``` text
filter clear
```

Clears the current filter.
Notes
Filtering applies to the rows currently loaded into the grid, not necessarily the whole file.
 
hide
Hides one column or a range of columns.
Syntax``` text
hide column
```

``` text
hide startColumn:endColumn
```

Examples``` text
hide A
```

Hides column A.``` text
hide A:F
```

Hides columns A through F.``` text
hide city
```

Hides the column named city.``` text
hide firstName:lastName
```

Hides all columns between firstName and lastName.
 
unhide
Restores hidden columns.
Syntax``` text
unhide all
```

Example``` text
unhide all
```

Makes all columns visible again.
 
Development Guide
Prerequisites
Install the .NET SDK compatible with the project target framework:``` text
.NET 10 SDK
```

 
Restore dependencies
From the repository root:``` bash
dotnet restore
```

 
Build the solution``` bash
dotnet build
```

 
Run the application
Run the application project:``` bash
dotnet run --project src/CSVoom.csproj
```

 
Run tests``` bash
dotnet test
```

 
Publish
The application project contains release publishing settings for a self-contained single-file build.
Example:``` bash
dotnet publish src/CSVoom.csproj -c Release
```

The exact output location depends on the runtime and publish settings used by the .NET SDK.
 
Testing
Tests are located in:``` text
test/app/ParserTests.cs
```

The test project uses:
xUnit
Microsoft.NET.Test.Sdk
xunit.runner.visualstudio
The parser tests cover:
Header reading
Row range reading
Maximum row capping
Empty files
Predicate-based matching
First-match search
Searching in a requested column
Missing search columns
No-match search results
GZIP input reading
Source file row-number preservation
 
Data Handling Notes
CSV parsing
CSVoom supports common CSV behavior, including:
Comma-separated fields
Quoted fields
Escaped quotes inside quoted fields
Example CSV value:``` csv
"name","description"
"Widget","A ""quoted"" value"
```

GZIP support
Files with a .gz extension are read through a GZIP stream.
This allows opening compressed CSV files without manually extracting them first.
Memory usage
CSVoom is intentionally range-based.
Instead of reading the whole CSV into memory, it reads:
The header row
A selected range of rows
Rows needed for search or matching operations
This makes the application more suitable for large CSV files than a full-file in-memory viewer.
 
Known Limitations
Filtering applies only to the rows currently loaded into the grid.
CSV parsing is line-based, so CSV fields containing embedded newlines are not currently supported.
Only comma-delimited CSV files are supported.
Only .csv and .gz file extensions are recognized.
Search returns the first match only.
unhide currently supports all; ranged unhide syntax is shown in the usage message but is not implemented.
 
Suggested README Replacement``` markdown
# CSVoom

CSVoom is a multiplatform desktop application for opening, unpacking, browsing, searching, and filtering CSV files.

It is built with .NET 10, C# 14, and Avalonia UI.

## Features

- Open `.csv` and `.gz` files
- View CSV data in a desktop data grid
- Load bounded row ranges for large-file workflows
- Search all columns or a selected column
- Filter currently loaded rows
- Hide columns by name or spreadsheet-style letter
- Restore hidden columns
- Preserve source file row numbers

## Requirements

- .NET 10 SDK

## Build
```

bash dotnet build``` 

## Run
```

bash dotnet run --project src/CSVoom.csproj``` 

## Test
```

bash dotnet test``` 

## Usage

Open the application and click **Open CSV** to select a `.csv` or `.gz` file.

After a file is loaded, use the command box to interact with the data.

### Commands

#### Load rows
```

text load 1:10000``` 

Loads rows `1` through `10000`.

#### Find text
```

text find London``` 

Searches all columns for `London`.
```

text find London city``` 

Searches only the `city` column.

Columns can be referenced by header name or by spreadsheet-style letter, such as `A`, `B`, or `C`.

#### Filter rows
```

text filter London``` 

Shows currently loaded rows containing `London`.
```

text filter columnName``` 

If `columnName` is a header, shows rows where that column is not empty and not `\N`.
```

text filter clear``` 

Clears the active filter.

#### Hide columns
```

text hide A``` 

Hides column `A`.
```

text hide A:F``` 

Hides columns `A` through `F`.
```

text hide city``` 

Hides the column named `city`.

#### Unhide columns
```

text unhide all``` 

Makes all columns visible.

## Project Structure
```

text CSVoom/ ├── src/ │ ├── app/ │ │ └── Parser.cs │ └── ui/ │ ├── App.axaml │ ├── MainWindow.axaml │ ├── MainWindow.axaml.cs │ └── Program.cs └── test/ └── app/ └── ParserTests.cs``` 

## Notes

CSVoom loads a limited number of rows at a time to keep the UI responsive with large files.

Known limitations:

- Filtering applies only to currently loaded rows.
- CSV fields with embedded newlines are not currently supported.
- Only comma-delimited files are supported.
- Search returns the first match only.
```
