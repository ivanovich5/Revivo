# Auditoría de distribución — 13 de octubre de 2026

Estado: preparación local; no es una certificación jurídica ni autorización pública.

Actualización: véase [validación del 19 de octubre](VALIDACION-0917.md), con
pruebas del paquete 0.9.17, dependencias identificadas y pendientes concretos.
La [revisión de empaquetado r2](EMPAQUETADO-R2.md) documenta las fuentes
obtenidas, la selección de motores y las pruebas adicionales; la revisión
de fuentes correspondientes todavía no está cerrada.

| Componente | Evidencia y acción | Pendiente |
| --- | --- | --- |
| Código propio Revivo | AGPL-3.0-only autorizada por el propietario | Revisar compatibilidad y completar fuentes reproducibles |
| Pixel Game | EULA local FFP limita uso personal | Reemplazada por Silkscreen en el recurso de la compilación 0.9.5; versiones anteriores todavía la contienen |
| Silkscreen | SIL OFL 1.1, fuente de Google Fonts | Licencia incluida en platform-notices/Silkscreen-OFL.txt |
| PyMuPDF 1.27.1 / MuPDF | METADATA y COPYING del paquete: AGPL o comercial | Vía AGPL elegida; completar código correspondiente de dependencias nativas; no basta incluir COPYING |
| Pillow 12.1.1 | LICENSE en dist-info, incluye avisos de bibliotecas | Conservar avisos y revisar bibliotecas nativas incluidas |
| Python 3.14.3 | LICENSE.txt del runtime embebido | Conservar licencia compuesta y avisos |
| .NET 8 / WPF | Licencias MIT y avisos de plataforma existentes | Se corrige instalador para incluir platform-notices |
| PhotoRec/TestDisk 7.2 | COPYING GPL v2+ y archivo fuente principal | Revisar fuentes correspondientes de DLL del paquete Windows; el tar principal solo no demuestra cobertura completa |
| Sleuth Kit 4.15.0 | IPL/CPL y licencias por componente; fuente principal disponible | Revisar bibliotecas nativas y utilidades adicionales del paquete |
| Inno Setup 6.7.3 | license.txt instalado permite uso comercial; el autor solicita compra comercial en su web | Mantener avisos; no confundir solicitud de compra con prohibición de la licencia incluida |

## Fuentes oficiales revisadas
- https://pymupdf.io/licensing
- https://github.com/pymupdf/PyMuPDF
- https://www.cgsecurity.org/wiki/TestDisk
- https://www.sleuthkit.org/sleuthkit/licenses.php
- https://github.com/jrsoftware/issrc/blob/main/license.txt
- https://jrsoftware.org/isorder.php
- https://github.com/google/fonts/tree/main/ofl/silkscreen

El inventario SHA-256 del paquete permite vincular esta revisión con los archivos
concretos. No se asume que una licencia del programa principal cubra todas sus DLL.
La descarga pública permanece desactivada hasta resolver fuentes y pruebas limpias.
