# =============================================================================
#  Fix-CanvasForm-Designer.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'

# Đã sửa lại đúng tên file chứa giao diện: CanvasForm.Designer.cs
Write-Host "Searching for CanvasForm.Designer.cs..."
$foundFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'CanvasForm.Designer.cs' -Recurse

if ($foundFiles.Count -eq 0) {
    Write-Error "Could not find CanvasForm.Designer.cs anywhere inside $PSScriptRoot."
    exit 1
}

$designer = $foundFiles[0].FullName
Write-Host "Found file at: $designer"

# ----- backup --------------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = "$designer.$stamp.bak"
Copy-Item -LiteralPath $designer -Destination $backup -Force
Write-Host "Backup written: $backup"

# ----- load ----------------------------------------------------------------
$text = Get-Content -LiteralPath $designer -Raw -Encoding UTF8

$before = $text

# 1) Replace any:  this.<name>.Image = ((System.Drawing.Image)(resources.GetObject("<name>.Image")));
#    with:         this.<name>.Image = null; // icon loaded at runtime by ApplyToolbarIcons()
$pattern1 = '(this\.\w+\.Image)\s*=\s*\(\(System\.Drawing\.Image\)\(resources\.GetObject\("[^"]*"\)\)\);'
$replacement1 = '$1 = null; // icon loaded at runtime by ApplyToolbarIcons()'
$text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern1, $replacement1)

# 2) Switch ToolStripItemDisplayStyle.Image -> ImageAndText so text fallback shows.
#    Use word-boundary so we don't accidentally rewrite something like ImageAndText.
$pattern2 = 'System\.Windows\.Forms\.ToolStripItemDisplayStyle\.Image\b(?!AndText)'
$replacement2 = 'System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText'
$text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern2, $replacement2)

# 3) Widen toolbar buttons from 43x34 to 110x34 so Vietnamese labels fit
#    when icons are missing. 
$pattern3 = 'new System\.Drawing\.Size\(43, 34\)'
$replacement3 = 'new System.Drawing.Size(110, 34)'
$text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern3, $replacement3)

# 4) Slightly widen the toolStrip1 strip itself (50 -> 130) so the
#    new button width has room. Only touch the very specific Size line.
$pattern4 = '(this\.toolStrip1\.Size\s*=\s*new System\.Drawing\.Size\()50(,\s*\d+\);)'
$replacement4 = '${1}130${2}'
$text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern4, $replacement4)

if ($text -eq $before) {
    Write-Host "No changes were needed (file already patched?). Backup kept anyway."
} else {
    # Preserve original BOM/encoding (UTF-8 with BOM is what VS writes).
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($designer, $text, $utf8Bom)
    Write-Host "Patched successfully!"
}

# ----- summary -------------------------------------------------------------
$imgNull   = ([regex]::Matches($text, 'ApplyToolbarIcons\(\)')).Count
$dispText  = ([regex]::Matches($text, 'ToolStripItemDisplayStyle\.ImageAndText')).Count

Write-Host ""
Write-Host "Summary:"
Write-Host "  Image = null replacements:        $imgNull"
Write-Host "  ImageAndText display style count: $dispText"
Write-Host ""