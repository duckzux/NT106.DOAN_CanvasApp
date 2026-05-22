# =============================================================================
#  Revert-CanvasForm-Patch.ps1
#
#  Khoi phuc CanvasForm.Designer.cs tu file .bak gan nhat
#  (do Fix-CanvasForm-Designer.ps1 tao ra). Sau khi rollback,
#  Designer.cs lai goi resources.GetObject(...) nhu ban dau.
#
#  Su dung:
#     powershell -ExecutionPolicy Bypass -File .\Revert-CanvasForm-Patch.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'

function Find-DesignerDir {
    $candidates = @()
    if ($PSScriptRoot) {
        $candidates += (Join-Path $PSScriptRoot 'CanvasApp.Client\Forms')
    }
    $candidates += (Join-Path (Get-Location).Path 'CanvasApp.Client\Forms')

    foreach ($d in $candidates) {
        if ($d -and (Test-Path $d)) { return (Resolve-Path $d).Path }
    }
    return $null
}

$formsDir = Find-DesignerDir
if (-not $formsDir) {
    Write-Host "ERROR: Khong tim thay thu muc CanvasApp.Client\Forms." -ForegroundColor Red
    exit 1
}

$designer = Join-Path $formsDir 'CanvasForm.Designer.cs'
if (-not (Test-Path $designer)) {
    Write-Host "ERROR: Khong tim thay $designer." -ForegroundColor Red
    exit 1
}

# Tim file .bak moi nhat
$backups = Get-ChildItem -Path $formsDir -Filter 'CanvasForm.Designer.cs.*.bak' |
           Sort-Object LastWriteTime -Descending

if ($backups.Count -eq 0) {
    Write-Host "ERROR: Khong tim thay file .bak nao trong $formsDir." -ForegroundColor Red
    Write-Host "Co the script Fix-CanvasForm-Designer.ps1 chua chay, hoac file .bak da bi xoa."
    exit 1
}

$latest = $backups[0]
Write-Host "Tim thay $($backups.Count) ban backup. Dung ban moi nhat:"
Write-Host "  $($latest.FullName)"
Write-Host "  (modified: $($latest.LastWriteTime))"
Write-Host ""

# Backup hien tai truoc khi ghi de (de quay lui them lan nua neu can)
$stamp        = Get-Date -Format 'yyyyMMdd_HHmmss'
$preRollback  = "$designer.preRollback.$stamp.bak"
Copy-Item $designer $preRollback -Force
Write-Host "Backup ban hien tai: $preRollback"

# Restore
Copy-Item $latest.FullName $designer -Force
Write-Host ""
Write-Host "Da khoi phuc: $designer" -ForegroundColor Green
Write-Host ""
Write-Host "Buoc tiep theo:"
Write-Host "  1. Mo CanvasForm.cs, xoa dong  ApplyToolbarIcons();  trong constructor"
Write-Host "     (neu da them truoc do)."
Write-Host "  2. Xoa file CanvasForm.Icons.cs neu khong dung nua:"
Write-Host "     - Trong Solution Explorer: chuot phai -> Delete"
Write-Host "  3. Dam bao da chay Repair-Resx.ps1 de xoa blob hong khoi resx."
Write-Host "  4. Build -> Clean Solution -> Rebuild Solution."
