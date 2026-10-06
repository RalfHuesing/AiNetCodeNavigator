# Assembly export CLI

`AiNetCodeNavigator.AssemblyExport.exe` is a separate offline command in the solution. It accepts an output directory and one or more DLL paths or quoted patterns:

```powershell
AiNetCodeNavigator.AssemblyExport.exe "C:\Dump" "C:\Vendor\*.dll" "C:\Other\Product.Core.dll"
```

The positional parser requires at least two arguments and preserves every source pattern for later expansion. The planned pattern contract supports `*` and `?` only in the final filename segment; directory wildcards and recursive `**` are unsupported. `--help` prints usage and exits with status `0`; missing arguments return `2`. The current executable returns `1` for a syntactically valid export request because export execution is still being implemented.
