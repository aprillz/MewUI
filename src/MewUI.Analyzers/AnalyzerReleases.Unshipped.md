; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------------
MEW1101 | MewUI.Markup | Hidden | Object initializer can be a MewUI fluent chain
MEW1105 | MewUI.Markup | Hidden | Configuration statements can be one MewUI fluent chain
MEW1106 | MewUI.Markup | Hidden | Configuration statement can be a MewUI fluent call
MEW1201 | MewUI.Binding | Warning | Binding path segment does not observe a notifying owner
MEW1202 | MewUI.Binding | Error | ThenNotifying getter must be a single member access
MEW1203 | MewUI.Binding | Error | Dotted binding getter requires the binding path generator
