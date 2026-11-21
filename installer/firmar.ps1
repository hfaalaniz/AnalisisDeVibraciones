# Firma Authenticode con el certificado de los secrets CERT_PFX_BASE64 / CERT_PASSWORD (SHA-256 + sello de tiempo).
param([Parameter(Mandatory)][string[]]$Archivos)
$ErrorActionPreference = 'Stop'
$pfx = Join-Path $env:RUNNER_TEMP 'firma.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:CERT_PFX_BASE64))
try {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'No se encontró signtool.exe (Windows SDK).' }
    foreach ($a in $Archivos) {
        & $signtool.FullName sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $pfx /p $env:CERT_PASSWORD $a
        if ($LASTEXITCODE -ne 0) { throw "Falló la firma de $a" }
        & $signtool.FullName verify /pa $a
    }
}
finally { Remove-Item $pfx -Force -ErrorAction SilentlyContinue }
