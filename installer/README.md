# Instalador y firma

`AnalisisVibraciones.iss` arma el instalador con **Inno Setup 6**: instala en *Archivos de programa* (o solo para el usuario, sin permisos de administrador), crea accesos en el menú Inicio y el escritorio, asocia los proyectos `.vaproj` (doble clic los abre en la pestaña de balanceo) y, opcionalmente, agrega la regla del firewall para el servidor Modbus TCP. Al desinstalar se conservan los datos del usuario (`%LOCALAPPDATA%\AnalisisVibraciones`).

## En el CI
Cada PR genera el instalador como artefacto («AnalisisVibraciones-instalador»). Al empujar una etiqueta `v*` se publican en Releases el instalador y el zip portable.

## A mano (Windows)
```powershell
winget install JRSoftware.InnoSetup
dotnet publish src\VibrationAnalysis.UI\VibrationAnalysis.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\AnalisisVibraciones
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=3.0.0 installer\AnalisisVibraciones.iss   # o "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
# Resultado: dist\AnalisisVibraciones-3.0.0-instalador.exe
```

## Firma digital (opcional)
Sin firma, Windows SmartScreen muestra «Windows protegió su PC» la primera vez (se sigue con *Más información → Ejecutar de todas formas*). Para evitarlo hace falta un certificado de firma de código (OV o EV) de una autoridad reconocida.

Con el certificado en un `.pfx`, cargar dos **secrets** en GitHub (*Settings → Secrets and variables → Actions*):

| Secret | Valor |
|---|---|
| `CERT_PFX_BASE64` | el `.pfx` en Base64: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx")) \| Set-Clipboard` |
| `CERT_PASSWORD` | la contraseña del `.pfx` |

Con esos secrets, el CI firma `AnalisisVibraciones.exe` y el instalador (SHA-256 con sello de tiempo). Sin ellos, todo se genera igual pero sin firmar. Los certificados EV en token USB o en la nube (Azure Trusted Signing) requieren adaptar el paso de firma.
