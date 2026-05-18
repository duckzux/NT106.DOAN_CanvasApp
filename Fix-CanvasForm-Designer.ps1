# =============================================================================
#  Fix-CanvasForm-Designer.ps1
#
#  Purpose
#  -------
#  Patches CanvasApp.Client/Forms/CanvasForm.Designer.cs so the form no
#  longer pulls toolbar icons out of the corrupt CanvasForm.resx.
#  Specifically:
#
#    1. Every line of the form
#         this.btnXyz.Image = ((System.Drawing.Image)(resources.GetObject(...)));
#       is rewritten to
#         this.btnXyz.Image = null; // icon loaded at runtime by ApplyToolbarIcons()
#
#    2. Every ToolStrip button that had DisplayStyle = Image is switched to
#       ImageAndText, so when no PNG is present in Resources\Icons\ the
#       Vietnamese text label ("Bút vẽ", "Tẩy", ...) is still visible.
#
#    3. The .resources read for "$this.AutoScaleDimensions" / "$this.Icon"
#       etc. is left alone — those entries are not in the corrupt blob.
#
#  Usage
#  -----
#    cd <repo root>
#    powershell -ExecutionPolicy Bypass -File Fix-CanvasForm-Designer.ps1
#
#  The script is idempotent: running it twice is safe.
#  A timestamped .bak file is written next to the original.
# =============================================================================

$ErrorActionPreference = 'Stop'

$designer = Join-Path (Get-Location) 'CanvasApp.Client\Forms\CanvasForm.Designer.cs'

if (-not (Test-Path $designer)) {
    Write-Error "Could not find $designer. Run this script from the repo root (the folder that contains CanvasApp.sln)."
    exit 1
}

# ----- backup --------------------------------------------------------------
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = "$designer.$stamp.bak"
Copy-Item $designer $backup -Force
Write-Host "Backup written: $backup"

# ----- load ----------------------------------------------------------------
$text = Get-Content -Raw -Encoding UTF8 $designer

$before = $text

# 1) Replace any:  this.<name>.Image = ((System.Drawing.Image)(resources.GetObject("<name>.Image")));
#    with:        this.<name>.Image = null; // icon loaded at runtime by ApplyToolbarIcons()
$pattern1 = '(this\.\w+\.Image)\s*=\s*\(\(System\.Drawing\.Image\)\(resources\.GetObject\("[^"]*"\)\)\);'
$replacement1 = '$1 = null; // icon loaded at runtime by ApplyToolbarIcons()'
$text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern1, $replacement1)

# 2) Switch ToolStripItemDisplayStyle.Image -> ImageAndText so text fallback shows.
#    Use word-boundary so we don't accidentally rewrite something like ImageAndText.
$pattern2 = 'System\.Windows\.Forms\.ToolStripItemDisplayStyle\.Image\b(?!AndText)'
$replacement2 = 'System.Windows.Forms.ToolStripItemDisplayStyle.ImageAndText'
$text = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern2, $replacement2)

# 3) Widen toolbar buttons from 43x34 to 110x34 so Vietnamese labels fit
#    when icons are missing. (Safe even if you later add icons back — the
#    toolbar dock = Left expands to fit anyway.)
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
    Write-Host "Patched: $designer"
}

# ----- summary -------------------------------------------------------------
$imgNull   = ([regex]::Matches($text, 'ApplyToolbarIcons\(\)')).Count
$dispText  = ([regex]::Matches($text, 'ToolStripItemDisplayStyle\.ImageAndText')).Count

Write-Host ""
Write-Host "Summary:"
Write-Host "  Image = null replacements:        $imgNull"
Write-Host "  ImageAndText display style count: $dispText"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Drop CanvasForm.resx (clean version) into CanvasApp.Client\Forms\ "
Write-Host "     (overwrite the corrupt one)."
Write-Host "  2. Drop CanvasForm.Icons.cs into CanvasApp.Client\Forms\ ."
Write-Host "  3. In CanvasForm.cs, add ApplyToolbarIcons(); right after"
Write-Host "     InitializeComponent(); in the constructor."
Write-Host "  4. (Optional) put 16x16 PNGs into CanvasApp.Client\Resources\Icons\ :"
Write-Host "     pen.png, eraser.png, rectangle.png, circle.png, line.png,"
Write-Host "     arrow.png, text.png, color.png, undo.png, redo.png, clear.png,"
Write-Host "     export.png, import_bg.png, fill.png"
Write-Host "  5. Build the solution. The GDI+ / 'Corrupt .resources file' errors"
Write-Host "     are gone."
