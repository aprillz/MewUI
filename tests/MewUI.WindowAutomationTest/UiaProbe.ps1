# Out-of-process UI Automation client for UiaProviderTests. It reads and operates a window's automation tree the way
# a screen reader does and prints one line per element: ControlType|Name|AutomationId|Patterns|IsEnabled.
param(
    [Parameter(Mandatory = $true)][string]$Command,
    [Parameter(Mandatory = $true)][long]$Window,
    [string]$Target = '',
    [string]$Text = '',
    [int]$Count = 1,
    [int]$TimeoutMilliseconds = 15000
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Window)
$processId = $root.Current.ProcessId

function Get-Description([System.Windows.Automation.AutomationElement]$Element) {
    $current = $Element.Current
    $patterns = ($Element.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers\.Pattern$', '' } | Sort-Object) -join ','
    $type = $current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
    return '{0}|{1}|{2}|{3}|{4}' -f $type, $current.Name, $current.AutomationId, $patterns, $current.IsEnabled
}

function Write-Tree([System.Windows.Automation.AutomationElement]$Element, [int]$Depth) {
    Write-Output (('  ' * $Depth) + (Get-Description $Element))
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($Element)
    while ($null -ne $child) {
        Write-Tree $child ($Depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}

# '#id' finds by AutomationId, anything else by Name; the search covers the window and its popup windows.
function Find-Element([string]$Key) {
    if ($Key.StartsWith('#')) {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Key.Substring(1))
    }
    else {
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Key)
    }

    $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $found) {
        foreach ($surface in (Get-ProcessWindows)) {
            $found = $surface.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($null -ne $found) {
                break
            }
        }
    }

    if ($null -eq $found) {
        throw "no element '$Key'"
    }

    return $found
}

function Get-ProcessWindows() {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Get-Pattern([System.Windows.Automation.AutomationElement]$Element, $Pattern) {
    return $Element.GetCurrentPattern($Pattern)
}

switch ($Command) {
    'tree' {
        Write-Tree $root 0
    }
    'windows' {
        foreach ($surface in (Get-ProcessWindows)) {
            if ($surface.Current.NativeWindowHandle -ne $Window) {
                Write-Output ('window ' + $surface.Current.NativeWindowHandle)
                Write-Tree $surface 1
            }
        }
    }
    'invoke' {
        (Get-Pattern (Find-Element $Target) ([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        Write-Output 'ok'
    }
    'setvalue' {
        (Get-Pattern (Find-Element $Target) ([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($Text)
        Write-Output 'ok'
    }
    'value' {
        Write-Output (Get-Pattern (Find-Element $Target) ([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
    }
    'toggle' {
        $toggle = Get-Pattern (Find-Element $Target) ([System.Windows.Automation.TogglePattern]::Pattern)
        $toggle.Toggle()
        Write-Output $toggle.Current.ToggleState
    }
    'expand' {
        (Get-Pattern (Find-Element $Target) ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
        Write-Output 'ok'
    }
    'select' {
        (Get-Pattern (Find-Element $Target) ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        Write-Output 'ok'
    }
    'rangevalue' {
        $range = Get-Pattern (Find-Element $Target) ([System.Windows.Automation.RangeValuePattern]::Pattern)
        $range.SetValue([double]$Text)
        Write-Output $range.Current.Value
    }
    'textinfo' {
        $pattern = (Find-Element $Target).GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
        Write-Output $pattern.DocumentRange.GetText(-1)
        Write-Output (($pattern.GetSelection() | ForEach-Object { $_.GetText(-1) }) -join '|')
        $range = $pattern.DocumentRange.Clone()
        $range.MoveEndpointByRange([System.Windows.Automation.Text.TextPatternRangeEndpoint]::End, $range, [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start)
        [void]$range.Move([System.Windows.Automation.Text.TextUnit]::Word, 1)
        $range.ExpandToEnclosingUnit([System.Windows.Automation.Text.TextUnit]::Word)
        Write-Output $range.GetText(-1).Trim()
    }
    'frompoint' {
        $coordinates = $Target.Split(',')
        $point = New-Object System.Windows.Point([double]$coordinates[0], [double]$coordinates[1])
        Write-Output (Get-Description ([System.Windows.Automation.AutomationElement]::FromPoint($point)))
    }
    'focus' {
        Add-Type -ReferencedAssemblies System, UIAutomationClient, UIAutomationTypes, WindowsBase -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Windows.Automation;

public static class FocusRecorder
{
    public static readonly BlockingCollection<string> Events = new BlockingCollection<string>();
    private static int _processId;
    private static AutomationFocusChangedEventHandler _handler;

    public static void Start(int processId)
    {
        _processId = processId;
        _handler = new AutomationFocusChangedEventHandler(OnFocusChanged);
        Automation.AddAutomationFocusChangedEventHandler(_handler);
    }

    private static void OnFocusChanged(object sender, AutomationFocusChangedEventArgs args)
    {
        var element = sender as AutomationElement;
        try
        {
            if (element != null && element.Current.ProcessId == _processId)
            {
                Events.Add(element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "") + "|" + element.Current.Name);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }
}
'@
        [FocusRecorder]::Start($processId)
        Write-Output 'ready'
        [Console]::Out.Flush()
        $received = 0
        $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
        while ($received -lt $Count -and [DateTime]::UtcNow -lt $deadline) {
            $line = $null
            if ([FocusRecorder]::Events.TryTake([ref]$line, 200)) {
                Write-Output ('focus ' + $line)
                [Console]::Out.Flush()
                $received++
            }
        }
    }
    default {
        throw "unknown command '$Command'"
    }
}
