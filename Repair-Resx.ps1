# =============================================================================
#  Repair-Resx.ps1
#
#  Duyet tat ca cac <data ... mimetype="application/x-microsoft.net.object
#  .bytearray.base64"> trong mot file .resx, thu decode base64 va load
#  bang System.Drawing.Image. Cai nao THAT SU hong (giong GDI+ se gap
#  luc build) thi XOA hoan toan ra khoi resx. Cac icon hop le giu nguyen.
#
#  Ket qua: build pass, embedded icons giu lai, chi 1 nut thieu icon.
#
#  Su dung:
#     # Sua CanvasForm.resx tai cho (mac dinh)
#     powershell -ExecutionPolicy Bypass -File .\Repair-Resx.ps1
#
#     # Hoac chi ro path:
#     powershell -ExecutionPolicy Bypass -File .\Repair-Resx.ps1 `
#                -ResxPath .\CanvasApp.Client\Forms\CanvasForm.resx
#
#     # Chi liet ke cai nao hong, KHONG sua file:
#     powershell -ExecutionPolicy Bypass -File .\Repair-Resx.ps1 -DryRun
# =============================================================================

param(
    [string] $ResxPath,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# ---------- find resx ----------
function Find-Resx {
    $candidates = @()
    if ($PSScriptRoot) {
        $candidates += (Join-Path $PSScriptRoot 'CanvasApp.Client\Forms\CanvasForm.resx')
    }
    $candidates += (Join-Path (Get-Location).Path 'CanvasApp.Client\Forms\CanvasForm.resx')
    foreach ($p in $candidates) {
        if ($p -and (Test-Path $p)) { return (Resolve-Path $p).Path }
    }
    return $null
}

if (-not $ResxPath) { $ResxPath = Find-Resx }
if (-not $ResxPath -or -not (Test-Path $ResxPath)) {
    Write-Host "ERROR: Khong tim thay file .resx. Truyen tham so -ResxPath." -ForegroundColor Red
    Write-Host ""
    Write-Host "Neu file resx cu da bi ghi de boi ban clean, khoi phuc tu git:"
    Write-Host "  git show HEAD:CanvasApp.Client/Forms/CanvasForm.resx > .\CanvasApp.Client\Forms\CanvasForm.resx"
    exit 1
}
$ResxPath = (Resolve-Path $ResxPath).Path
Write-Host "Resx: $ResxPath"
Write-Host ""

# ---------- check first whether this resx even HAS embedded images ----------
$rawText = Get-Content -Raw -Encoding UTF8 $ResxPath
if ($rawText -notmatch 'bytearray\.base64') {
    Write-Host "File resx nay KHONG co embedded image nao." -ForegroundColor Yellow
    Write-Host "Co the ban dang nham vao ban resx 'clean' minh tao truoc do."
    Write-Host "Neu can ban cu, khoi phuc tu git:"
    Write-Host "  git show HEAD:CanvasApp.Client/Forms/CanvasForm.resx > .\CanvasApp.Client\Forms\CanvasForm.resx"
    exit 1
}

# ---------- parse + scan ----------
[xml]$xml = $rawText

$good     = @()
$bad      = @()

foreach ($d in @($xml.root.data)) {
    if ($d.mimetype -ne 'application/x-microsoft.net.object.bytearray.base64') { continue }
    $name = $d.name
    $valueNode = $d.SelectSingleNode('value')
    $b64 = if ($valueNode) { $valueNode.InnerText } else { $d.'#text' }
    $clean = ($b64 -replace '\s', '')

    if (-not $clean) {
        Write-Host "  BAD   $name  (value rong)" -ForegroundColor Red
        $bad += $d
        continue
    }

    $stream = $null
    $bmp    = $null
    try {
        $bytes  = [System.Convert]::FromBase64String($clean)
        if ($bytes.Length -lt 8) { throw "data qua ngan ($($bytes.Length) bytes)" }
        $stream = New-Object System.IO.MemoryStream(,$bytes)
        $bmp    = [System.Drawing.Image]::FromStream($stream)
        # Cham vao 1 property de force GDI+ thuc su decode
        $w = $bmp.Width
        Write-Host "  OK    $name  ($($bytes.Length) bytes, ${w}x$($bmp.Height))" -ForegroundColor Green
        $good += $d
    }
    catch {
        Write-Host "  BAD   $name  -> $_" -ForegroundColor Red
        $bad += $d
    }
    finally {
        if ($bmp)    { $bmp.Dispose() }
        if ($stream) { $stream.Dispose() }
    }
}

Write-Host ""
Write-Host "Tom tat: $($good.Count) icon hop le, $($bad.Count) bi hong."

if ($bad.Count -eq 0) {
    Write-Host ""
    Write-Host "Khong co blob nao bi hong. Khong can sua gi ca." -ForegroundColor Green
    Write-Host "Neu build van loi 'GDI+', co the do nguyen nhan khac"
    Write-Host "(file permission, line ending, antivirus...). Thu Clean Solution"
    Write-Host "roi Rebuild."
    exit 0
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DryRun: chi liet ke, khong sua. Chay lai khong co -DryRun de xoa."
    exit 0
}

# ---------- remove bad entries ----------
foreach ($d in $bad) {
    $xml.root.RemoveChild($d) | Out-Null
}

# backup
$stamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = "$ResxPath.beforerepair.$stamp.bak"
Copy-Item $ResxPath $backup -Force
Write-Host ""
Write-Host "Backup: $backup"

# save (giu format gan giong VS - utf-8 bom)
$utf8Bom = New-Object System.Text.UTF8Encoding $true
$sw = New-Object System.IO.StringWriter
$xmlSettings = New-Object System.Xml.XmlWriterSettings
$xmlSettings.Indent = $true
$xmlSettings.Encoding = $utf8Bom
$xmlSettings.OmitXmlDeclaration = $false

$xmlWriter = [System.Xml.XmlWriter]::Create($ResxPath, $xmlSettings)
$xml.Save($xmlWriter)
$xmlWriter.Dispose()

Write-Host "Da xoa $($bad.Count) blob hong ra khoi: $ResxPath" -ForegroundColor Green
Write-Host ""
Write-Host "Cac key bi xoa:"
foreach ($d in $bad) { Write-Host "  - $($d.name)" }
Write-Host ""
Write-Host "Buoc tiep theo:"
Write-Host "  1. Restore Designer.cs ve ban goc (uses resources.GetObject):"
Write-Host "       chay Revert-CanvasForm-Patch.ps1"
Write-Host "     hoac thu cong:"
Write-Host "       Copy-Item .\CanvasApp.Client\Forms\CanvasForm.Designer.cs.<timestamp>.bak ``"
Write-Host "                 .\CanvasApp.Client\Forms\CanvasForm.Designer.cs -Force"
Write-Host "  2. (Tuy chon) Xoa file CanvasForm.Icons.cs neu khong dung partial class nua."
Write-Host "  3. (Tuy chon) Xoa dong  ApplyToolbarIcons();  trong constructor CanvasForm.cs"
Write-Host "  4. Build -> Clean Solution -> Rebuild Solution."
Write-Host ""
Write-Host "Cac nut con lai van co icon. Rieng nut tuong ung voi blob bi xoa"
Write-Host "se hien thi blank (hoac text neu DisplayStyle = ImageAndText)."
