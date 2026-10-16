# Desarrollo

## Windows

Requiere SDK .NET 8 y Windows para la interfaz WPF. Desde la raíz:

```powershell
dotnet build src/Rescate.Desktop/Rescate.Desktop.csproj -c Release
dotnet publish src/Rescate.Desktop/Rescate.Desktop.csproj -c Release -r win-x64 --self-contained true -o stage/app
dotnet publish src/Rescate.Acquire/Rescate.Acquire.csproj -c Release -r win-x64 --self-contained true -o stage/app-copia-092
dotnet publish src/Rescate.Reuse/Rescate.Reuse.csproj -c Release -r win-x64 --self-contained true -o stage/app-reutilizar
```

Para validación y vistas: Python 3.14, Pillow 12.1.1 y PyMuPDF 1.27.1.

```powershell
python -m pip install -r requirements-dev.txt
python -m unittest discover -s tests -p "test_*.py"
```

Las pruebas Python usan carpetas temporales y datos sintéticos. La suite .NET completa requiere los motores y las imágenes generadas; no la ejecutes sobre una memoria real. `tests/make_fixtures.py` genera las imágenes FAT de ejemplo; la generación exFAT requiere además FATtools (véase THIRD_PARTY.md).

## Empaquetado

`scripts/prepare-engines.ps1` descarga versiones fijadas y verifica hashes. Consulta sus licencias antes de redistribuirlas. La etapa `stage/` debe incluir `engines/`, `scripts/`, un Python embebido con las dependencias de vista previa, avisos, fuentes correspondientes y la guía del instalador. No existe todavía un ensamblado completo automatizado desde un equipo limpio.

El instalador `installer/revivo.iss` usa `../stage` y escribe en `release/`. Puede compilarse con Inno Setup cuando la etapa esté completa; no basta con compilar WPF. No se afirma reproducibilidad bit a bit.

## Web

```powershell
python -m http.server 8765 --directory web
```

La web estática funciona sin compilación. El formulario necesita que el mismo dominio dirija `/api/feedback` al servicio descrito en [feedback/README.md](feedback/README.md). En un servidor estático de prueba el envío no funcionará.
