# installer/Repair-InstallerVersionText.ps1
#
# Inno Setup writes ProductVersion text into a fixed-width version resource and
# pads it with spaces ("1.2.811-beta.1" followed by 36 spaces). Shipped clients up
# to 1.2.709 compare that text exactly in their own copy of UpdateOnExit.ps1, so a
# padded installer can never pass their in-app full-installer check. Replace the
# padding with NULs in place: the file keeps its size and layout (Inno locates its
# setup data by offset), and Windows reads the string up to the first NUL.
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$ProductVersion
)
$ErrorActionPreference = 'Stop'
if ($ProductVersion -notmatch '\A[0-9A-Za-z.+-]{1,49}\z') { throw "Unexpected product version '$ProductVersion'." }

$bytes = [IO.File]::ReadAllBytes($Path)
function Read-UInt16([int]$at) { [BitConverter]::ToUInt16($bytes, $at) }
function Read-UInt32([int]$at) { [BitConverter]::ToUInt32($bytes, $at) }

# Search only the PE resource section; the setup data after it is compressed.
if ($bytes.Length -lt 0x40 -or (Read-UInt16 0) -ne 0x5A4D) { throw 'Not a PE file.' }
$pe = [int](Read-UInt32 0x3C)
if ($pe -le 0 -or $pe + 24 -gt $bytes.Length -or (Read-UInt32 $pe) -ne 0x4550) { throw 'Not a PE file.' }
$sections = Read-UInt16 ($pe + 6)
$table = $pe + 24 + (Read-UInt16 ($pe + 20))
$start = -1; $end = -1
for ($s = 0; $s -lt $sections; $s++) {
    $header = $table + 40 * $s
    if ($header + 40 -gt $bytes.Length) { throw 'Truncated PE section table.' }
    $name = [Text.Encoding]::ASCII.GetString($bytes, $header, 8).TrimEnd([char]0)
    if ($name -ceq '.rsrc') {
        $start = [int](Read-UInt32 ($header + 20)); $end = $start + [int](Read-UInt32 ($header + 16))
    }
}
if ($start -lt 0 -or $end -gt $bytes.Length -or $end -le $start) { throw 'The installer has no resource section.' }

$unicode = [Text.Encoding]::Unicode
$key = $unicode.GetBytes("ProductVersion`0")
$patched = 0; $alreadyExact = 0
for ($i = $start; $i -le $end - $key.Length; $i += 2) {
    $match = $true
    for ($j = 0; $j -lt $key.Length; $j++) { if ($bytes[$i + $j] -ne $key[$j]) { $match = $false; break } }
    if (-not $match) { continue }
    # VS_VERSIONINFO String: wLength, wValueLength (characters incl. NUL), wType, key, 32-bit aligned value.
    $string = $i - 6
    $valueChars = Read-UInt16 ($string + 2)
    $value = $i + $key.Length
    while (($value - $string) % 4) { $value++ }
    if ($valueChars -lt 1 -or $value + 2 * $valueChars -gt $end) { throw 'Malformed ProductVersion resource.' }
    $text = $unicode.GetString($bytes, $value, 2 * $valueChars).TrimEnd([char]0)
    $trimmed = $text.TrimEnd(' ')
    if ($trimmed -cne $ProductVersion) { throw "ProductVersion is '$trimmed', expected '$ProductVersion'." }
    if ($text -ceq $trimmed) { $alreadyExact++; continue }
    for ($c = $trimmed.Length; $c -lt $valueChars; $c++) { $bytes[$value + 2 * $c] = 0; $bytes[$value + 2 * $c + 1] = 0 }
    $patched++
}
if ($patched + $alreadyExact -lt 1) { throw 'The installer has no ProductVersion string.' }
if ($patched -gt 0) { [IO.File]::WriteAllBytes($Path, $bytes) }

$read = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path).ProductVersion
if ($read -cne $ProductVersion) { throw "ProductVersion reads '$read' after repair, expected '$ProductVersion'." }
