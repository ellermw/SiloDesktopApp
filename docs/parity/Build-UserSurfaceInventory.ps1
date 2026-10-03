param(
    [string]$ServerRepository = 'D:\SiloPlayer\.codex-tmp\silo-server-current',
    [string]$Reference = '8e2e840474a085c6df6571a5a2850f7eb996810c',
    [string]$DesktopRepository = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$Output = "$PSScriptRoot\2026-10-01-user-surface-inventory.json"
)
$ErrorActionPreference = 'Stop'

function Read-Reference([string]$Path) {
    $lines = & git -C $ServerRepository show "${Reference}:$Path"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read reference file $Path" }
    return $lines
}

# This is a discovery index, not a claim that each control has been reviewed.
# JSX/C# expressions and runtime-generated plugin controls need manual expansion.
$app = (Read-Reference 'web/src/App.tsx') -join "`n"
$app = [regex]::Replace($app, '(?s)\{\/\* Admin area.*?(?=\{\/\* Account credentials)', '')
$routes = @([regex]::Matches($app, '<Route\s+[^>]*?path=("[^"]+"|\{[^}]+\})') | ForEach-Object {
    $_.Groups[1].Value.Trim('"')
})
$tree = & git -C $ServerRepository ls-tree -r --name-only $Reference web/src
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory official reference' }
$webFiles = @($tree | Where-Object {
    $_ -match '^web/src/(pages|components)/.*\.tsx$' -and
    $_ -notmatch '(?i)(/admin|/setup-wizard/|/SetupWizard\.tsx$|\.test\.|\.spec\.)'
})
$web = @()
foreach ($path in $webFiles) {
    $lines = @(Read-Reference $path)
    $controls = @()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '<(button|Button|Input|input|textarea|select|Select|SelectItem|SelectTrigger|Switch|Checkbox|Slider|RadioGroup|RadioGroupItem|Toggle|ToggleGroupItem|TabsTrigger|DropdownMenuTrigger|DropdownMenuItem|DropdownMenuCheckboxItem|DropdownMenuRadioItem|Dialog|DialogTrigger|AlertDialog|AlertDialogTrigger|Popover|PopoverTrigger|ContextMenuItem|ContextMenuTrigger|TooltipTrigger|AccordionTrigger|SheetTrigger|CollapsibleTrigger|CommandItem|PaginationLink|a|Link|ViewTransitionLink)(?=[\s>/])') {
            $controls += [ordered]@{ line = $index + 1; kind = $Matches[1]; source = $lines[$index].Trim() }
        }
    }
    $web += [ordered]@{ path = $path; controls = $controls; status = 'discovered; manual source/render review required' }
}
$nativeFiles = @(Get-ChildItem "$DesktopRepository\src\SiloPlayer" -Recurse -File | Where-Object {
    $_.FullName -match '\\(Views|Controls)\\|\\MainWindow\.xaml' -and
    $_.FullName -notmatch '\\Admin\\|\\(obj|bin)\\' -and
    $_.Name -match '\.(xaml|cs)$'
})
$native = @()
foreach ($file in $nativeFiles) {
    $lines = @(Get-Content -LiteralPath $file.FullName)
    $controls = @()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '<(Button|HyperlinkButton|DropDownButton|SplitButton|AppBarButton|ToggleButton|RadioButton|Slider|NumberBox|ComboBox|ComboBoxItem|CheckBox|ToggleSwitch|TextBox|PasswordBox|AutoSuggestBox|MenuFlyout|Flyout|MenuFlyoutItem|MenuFlyoutSubItem|RadioMenuFlyoutItem|ToggleMenuFlyoutItem|ContentDialog|NavigationViewItem)(?=[\s>/])' -or
            $lines[$index] -match '\bnew\s+(Button|HyperlinkButton|DropDownButton|SplitButton|AppBarButton|ToggleButton|RadioButton|Slider|NumberBox|ComboBox|ComboBoxItem|CheckBox|ToggleSwitch|TextBox|PasswordBox|AutoSuggestBox|MenuFlyout|Flyout|MenuFlyoutItem|MenuFlyoutSubItem|RadioMenuFlyoutItem|ToggleMenuFlyoutItem|ContentDialog)\b') {
            $controls += [ordered]@{ line = $index + 1; kind = $Matches[1]; source = $lines[$index].Trim() }
        }
    }
    $native += [ordered]@{ path = $file.FullName.Substring($DesktopRepository.Length + 1).Replace('\', '/'); controls = $controls; status = 'discovered; manual source/render review required' }
}
$result = [ordered]@{
    reference = $Reference
    referenceOrigin = 'https://github.com/Silo-Server/silo-server'
    desktop = $DesktopRepository
    candidate = '1.2.0, uncommitted working tree'
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    classification = 'Discovery only. Counts are matched source lines (at most one entry per line), not unique controls or completed comparisons. Multiline/dynamic/generated controls require manual expansion. Conditional/admin-only shared controls require manual scope checks. Routes include redirects and wildcards. Lua/WebView controls and generated plugins require separate inventory.'
    routeExpressions = $routes
    web = $web
    native = $native
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Output -Encoding utf8
[ordered]@{
    routeExpressions = $routes.Count
    webFiles = $web.Count
    webControlLineMatches = @($web | ForEach-Object { $_.controls }).Count
    nativeFiles = $native.Count
    nativeControlLineMatches = @($native | ForEach-Object { $_.controls }).Count
    output = $Output
} | ConvertTo-Json
