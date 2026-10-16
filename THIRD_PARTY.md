# Dependencias y procedencia

## Motores utilizados por la aplicación

**The Sleuth Kit 4.15.0**

- Repositorio: https://github.com/sleuthkit/sleuthkit
- Release fijada: https://github.com/sleuthkit/sleuthkit/releases/tag/sleuthkit-4.15.0
- Paquete Windows: `sleuthkit-4.15.0-win32.zip`.
- SHA-256 del paquete, comprobado contra el digest publicado en la API de GitHub: `c2ebab8105b893d97bd8ce35b88e01985e2a106efc97f03adf95840a631b20ce`.
- Se usan `mmls.exe`, `fls.exe` e `icat.exe` como procesos, sin modificaciones.
- Licencias originales conservadas dentro del paquete. El proyecto contiene componentes con licencias CPL, IPL y otras: consultar `engines/sleuthkit-4.15.0-win32/licenses`.
- Archivo de fuentes original: `third-party-source/sleuthkit-4.15.0.tar.gz`; SHA-256 `3a8c1e7d18a9b81f3e5e8aa78313974aceaafc6e051d636bc92cd7168286eca9`.

**PhotoRec / TestDisk 7.2**

- Repositorio: https://github.com/cgsecurity/testdisk
- Descarga oficial: https://www.cgsecurity.org/wiki/TestDisk_Download
- Paquete: https://www.cgsecurity.org/testdisk-7.2.win64.zip
- SHA-256 registrado localmente: `e97e203ce77b6b1a3a37d01beccf069dc6c4632b579ffbb82ae739cdda229f38`. Es una huella local de la descarga HTTPS; no se verificó contra una firma independiente del autor.
- Se usa solamente `photorec_win.exe`, sin modificarlo. No se invocan las funciones de reparación de TestDisk.
- PhotoRec se distribuye bajo GPL v2 o posterior; se conserva `COPYING` y la documentación del paquete, además de sus dependencias originales.
- Fuentes originales: https://www.cgsecurity.org/testdisk-7.2.tar.bz2, incluidas en `third-party-source`; SHA-256 `f8343be20cb4001c5d91a2e3bcd918398f00ae6d8310894a5a9f2feb813c283f`.
- Antes de publicar un instalador comercial o redistribuir una selección diferente de binarios, revisar las obligaciones de distribución de todos sus componentes y bibliotecas. Esta entrega conserva los paquetes extraídos y las fuentes de los proyectos principales.

## Solo para pruebas

- **DiscUtils.Ntfs 0.16.13**, NuGet, del proyecto https://github.com/DiscUtils/DiscUtils. Licencia MIT. Genera una imagen NTFS sintética; no forma parte del ejecutable de recuperación.
- **FATtools**, https://github.com/maxpat78/FATtools, commit `aa29362350ce1cf6488f18ac302951ddb2aa1b88`. GPL; se usa desde una copia de desarrollo para crear una imagen exFAT sintética. No se empaqueta como motor de recuperación.

## Interfaz y plataforma

La aplicación y el coordinador son código nuevo. No se copiaron código, iconos ni recursos de Wondershare Recoverit. .NET y WPF proceden de Microsoft/.NET Foundation; el ejecutable portable incluye el runtime y su publicación genera los avisos de plataforma correspondientes.

El código propio de Revivo se ofrece bajo AGPL-3.0-only por autorización de su propietario del 13 de octubre de 2026. Este repositorio publica el código propio; no incluye los paquetes binarios ni los archivos comprimidos de terceros. Las dependencias conservan sus licencias. PyMuPDF 1.27.1 se utiliza por la vía AGPL; Python 3.14.3 y Pillow 12.1.1 incluyen sus licencias en el runtime. Silkscreen usa OFL 1.1. Consulte installer/AUDITORIA-LICENCIAS.md; la revisión de fuentes de DLL no está completa.

Los nombres third-party-source y engines describen la etapa de empaquetado local, no archivos incluidos en este repositorio.
