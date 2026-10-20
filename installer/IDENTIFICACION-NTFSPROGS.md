# Identificación de libntfs cerrada — REVIVO-USB 1.0

Se identifica **libntfs de ntfsprogs 2.0.0**, paquete de compilación
`cygwin64-ntfsprogs-2.0.0-0.el7.centos`, en TestDisk y PhotoRec 7.2.
Proyecto original: Linux-NTFS. Licencia: **GPL-2.0-or-later**, indicada en
las cabeceras del código y acompañada por COPYING en el paquete fuente.

## Evidencia

1. Los dos EXE incluidos en REVIVO-USB 1.0 coinciden byte por byte con
   `https://www.cgsecurity.org/testdisk-7.2.win64.zip`, descargado nuevamente.
   Sus SHA-256 están en `evidence/ntfsprogs-2.0.0/official-binaries.json`.
2. La receta oficial de TestDisk v7.2 declara `cygwin64-ntfsprogs`:
   https://github.com/cgsecurity/testdisk/blob/v7.2/cygwin-testdisk-copr.spec
3. El archivo estático libntfs.a del paquete binario de la compilación Copr
   00443037 contiene 21 secciones de código examinadas. **20 coinciden en cada
   EXE: 91,274 bytes de código comparados por ejecutable**, excluyendo campos
   de reubicación del enlazador y hasta 15 bytes de relleno final por sección.
   No se comparan las direcciones relocalizadas ni se afirma igualdad de todo
   el archivo de biblioteca con el EXE. `security.o` no tiene coincidencia en
   esta prueba; no se utiliza como evidencia de identificación. La identificación
   se apoya en los otros 20 módulos, no en una cadena de versión ni solo en la receta.
4. El RPM fuente de esa misma compilación incluye `ntfsprogs-2.0.0.tar.gz`,
   `cygwin-ntfsprogs.spec`, `ntfsprogs_compat.patch` y `ntfsprogs_gcc_struct.patch`.
   Su SHA-256 es `76f02034d2b82766505b6a0f291fa86d01968b6a1bf24ed39ff7b0af989b0755`.
   Se comprobó que el archivo dentro de REVIVO-USB-1.0-fuentes.zip coincide.

Paquetes binario y fuente, publicados por el entorno de compilación de grenier:
https://copr-be.cloud.fedoraproject.org/results/grenier/cygwin-testdisk/epel-7-x86_64/00443037-cygwin-ntfsprogs/

## Repetir la comparación

Descargar el RPM binario fijado en `download.json`, comprobar SHA-256 y extraer
su archivo `libntfs.a` (por ejemplo con rpmfile). Ejecutar:

```text
python scripts/verify_ntfsprogs_binary.py libntfs.a RUTA_A_TESTDISK resultado.json
```

El script no ejecuta los motores ni lee dispositivos. El resultado conserva
cada objeto, longitud comparada y posición dentro del EXE. Los archivos JSON
adjuntos registran las huellas de los EXE concretos incluidos en 1.0.

## Alcance del cierre

Se cierra el pendiente específico de identificación y fuentes de **ntfsprogs**
registrado en EMPAQUETADO-R2.md y REVIVO-USB-1.0.md. Esos documentos conservan
el historial previo; este informe lo actualiza. No se modifican ejecutables.
Las fuentes, parches y licencia ya están en la entrega: no hace falta reinstalar.

Esto no es una certificación jurídica global ni demuestra reconstrucción binaria
reproducible de todas las dependencias. Tampoco sustituye las pruebas físicas,
la prueba en otra computadora o la comprobación de la entrega pública de fuentes
junto al instalador. Mantener el paquete de fuentes y avisos en el lanzamiento.
