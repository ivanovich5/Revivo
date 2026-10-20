# Distribución 0.9.17-r2

Estado: candidato para validación en otra computadora. La aplicación sigue siendo
0.9.17; r2 identifica la revisión del paquete y sus motores. No se ha activado la
descarga pública ni se ha firmado digitalmente el instalador.

## Componentes de esta entrega

- **TSK 4.15.0:** mmls, fls e icat compilados desde el archivo fuente oficial,
  sin modificaciones al código, con `build-tsk-raw.sh`. Se eliminan los lectores
  opcionales EWF/VHD/VMDK/AFF y sus DLL. REVIVO admite imágenes RAW y conserva
  FAT32, exFAT y NTFS. No se afirma compatibilidad con imágenes de máquinas virtuales.
- **PhotoRec y TestDisk 7.2:** ejecutables originales. TestDisk sí se utiliza
  para evaluar una copia local verificada; no se ejecuta sobre la USB original.
  Se conservan las DLL necesarias y `63/cygwin` (terminfo). Se excluyen QPhotoRec,
  Qt y utilidades que REVIVO no invoca.
- **Cygwin 3.3.3, libgcc 13.4.0 y libiconv 1.16:** DLL obtenidas de paquetes de
  compilación de Copr, acompañadas por los paquetes fuente de esas mismas
  compilaciones, incluidos parches y recetas. Sus huellas se registran en el
  manifiesto. Son sustituciones respecto al runtime original de CGSecurity.
- **libewf de PhotoRec:** la DLL conservada coincide byte por byte con un binario
  del paquete Copr cuyo archivo fuente y parches se incluyen.
- **JPEG, ncurses y zlib:** se conservan las DLL originales, con sus avisos y
  fuentes de versiones coincidentes. No se afirma igualdad binaria con las
  recompilaciones de Copr; sus licencias permisivas no se convierten en GPL
  por incluirlas en este paquete de documentación.
- **e2fsprogs/ntfsprogs:** se incluyen los paquetes fuente del entorno de
  CGSecurity y la receta oficial de TestDisk 7.2 que declara esas dependencias.
  e2fsprogs 1.45.3 coincide con la versión informada por el motor; este no
  expone una versión de ntfsprogs. Se registra esa limitación de procedencia.
- **PyMuPDF/MuPDF 1.27.1:** fuentes de ambos proyectos, receta oficial de ruedas
  Windows de esa versión y script `scripts/gh_release.py` del archivo fuente.
- **Pillow/Python:** fuentes, licencias y avisos compuestos conservados. Pillow
  incluye los avisos de sus bibliotecas nativas. Se añaden reconocimientos a
  FreeType y al Independent JPEG Group.
- **Runtime del TSK reconstruido:** avisos de MinGW-w64/winpthreads y GPLv3 con
  GCC Runtime Library Exception 3.1. La herramienta de compilación queda fijada
  por URL, versión y SHA-256; no se incluye el compilador dentro del instalador.

Las fuentes por fecha de commit de libvhdi/libvmdk y los runtimes descartados se
mantienen únicamente como investigación local. No forman parte de esta entrega
ni se presentan como fuentes correspondientes del motor nuevo.

## Verificación

El paquete reducido inicial falló por excluir TestDisk y terminfo; ambos se
reincorporaron. La selección corregida pasó 38/38 pruebas. Posteriormente el TSK
reconstruido y los runtimes sustituidos también pasaron 38/38, incluyendo FAT32,
MBR, NTFS, exFAT, recuperación PNG por firmas desde RAW, exportación con contenido
exacto, cancelación, errores simulados y evaluación de estructura.

Estas pruebas no equivalen a una certificación jurídica, a reproducibilidad
binaria de los proveedores ni a una prueba en una computadora limpia. Las
licencias originales y la procedencia de cada archivo están disponibles para
revisión; no se ocultan las diferencias de compilación de terceros.

## Reproducir

1. Obtener el archivo oficial `sleuthkit-4.15.0.tar.gz` y el toolchain fijado en
   `toolchain.json` del paquete de fuentes. Verificar las huellas del manifiesto.
2. Extraer TSK en un directorio nuevo. En Git Bash, añadir `mingw64/bin` del
   toolchain al PATH y ejecutar:

   ```bash
   bash installer/build-tsk-raw.sh /ruta/sleuthkit-4.15.0 /ruta/tsk-bin
   ```
3. Preparar PhotoRec/TestDisk con los paquetes y DLL registrados en el manifiesto.
   Los RPM fuente incluyen recetas y parches; no son instaladores Windows.
4. Crear una etapa nueva con:

   ```powershell
   python scripts/prepare_distribution.py C:/EtapaAnterior C:/EtapaNueva --tsk-bin C:/tsk-bin --photorec-bin C:/PhotoRec
   python scripts/audit_distribution.py C:/EtapaNueva C:/QA/inventario.json
   ```
5. Ejecutar las pruebas del núcleo y del flujo integrado antes de compilar Inno
   Setup. `BundleSources=0` solo se usa cuando se entrega también el ZIP de
   fuentes correspondiente. Conservar ambos archivos en el mismo lanzamiento.

## Archivos que deben distribuirse juntos

- Instalador `Revivo-0.9.17-r2-candidato-setup.exe`.
- `Revivo-0.9.17-r2-fuentes.zip`, con el código propio, fuentes de terceros,
  parches, recetas, avisos y manifiesto de procedencia.
- `SHA256SUMS.txt` para verificar las descargas.

La licencia propia es AGPL-3.0-only. Cada dependencia conserva su licencia.
El instalador muestra la licencia y deja los avisos en `platform-notices`.
Antes de habilitar la descarga pública, revisar la prueba en otra computadora
con estos mismos archivos y confirmar la identificación de ntfsprogs. La auditoría
de procedencia no se declara cerrada mientras esa comprobación esté pendiente.
