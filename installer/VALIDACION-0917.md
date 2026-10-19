# Revisión de distribución 0.9.17 — 19 de octubre de 2026

Estado: pruebas locales superadas; descarga pública del instalador pendiente.
Esta revisión no equivale a una prueba en una computadora limpia ni a una
certificación de licencias.

## Pruebas ejecutadas

| Prueba | Resultado |
| --- | --- |
| Suite Python del repositorio | 27 pruebas superadas |
| Paquete 0.9.17: imagen FAT32 sintética | 4 archivos encontrados y exportados, catálogo generado |
| Guardado por lote | 2 archivos; comprobaciones de progreso, nombres y contenido superadas |
| Inicio con sesión nueva | Superado |
| Estimación de tiempo: inicio, avance, pausa, error y final | Superado |
| Distribución de ventana a tamaños efectivos de 100/125/150/200 % | Superado; renderizado WPF, no cambio real de DPI de Windows |
| Compilación de instalador aislado | Superada con la misma definición y contenido de la etapa 0.9.17 |
| Instalación aislada y apertura | Código de salida 0 |
| Recuperación desde la instalación aislada | 4 archivos de la imagen sintética; código de salida 0 |
| Desinstalación aislada | Código de salida 0 |

El instalador de prueba usa un identificador, nombre y destino distintos para
evitar sustituir la instalación habitual. No se probó una actualización en este
ciclo. No se accedió a una USB física. La máquina de pruebas tiene herramientas
de desarrollo: estos resultados no prueban independencia de ese entorno.

## Inventario y hallazgos

`scripts/audit_distribution.py` genera SHA-256 y tamaños de los archivos de una
etapa de distribución, además de importaciones PE directas. No ejecuta los
binarios. El inventario de esta etapa contiene 1,093 archivos, 611,153,302 bytes
(excluye `__pycache__`). No es el tamaño del instalador comprimido.

Las tablas PE no detectan carga dinámica, dependencias diferidas ni bibliotecas
enlazadas estáticamente. No deben usarse solas para decidir qué DLL eliminar.

- `photorec_win.exe` importa Cygwin, libewf, libgcc, iconv, JPEG, ncurses y zlib.
- `mmls.exe`, `fls.exe` e `icat.exe` importan libewf, libvhdi, libvmdk y runtime
  de Microsoft; fls e icat también importan zlib.
- La distribución TestDisk completa incluye QPhotoRec/Qt y otras utilidades
  que REVIVO no invoca. Reducir el paquete requiere revisar carga dinámica y
  repetir recuperación por firmas; todavía no se han eliminado.
- El archivo `pymupdf-1.27.1.tar.gz` contiene las fuentes de PyMuPDF pero no
  las de MuPDF. El runtime declara MuPDF 1.27.1.
- Se obtuvo el archivo oficial `mupdf-1.27.1-source.tar.gz`, con código del motor
  y directorio thirdparty, para preparar la siguiente distribución. SHA-256:
  `5a1fce7373048a3e8985d6616ee37caaa1885ab90c1b0b3956bdb53d6ecd22af`.
  La huella es local, no una firma independiente. Aún debe vincularse la receta
  de compilación y configuración con el binario distribuido.
- El instalador 0.9.17 existente no está firmado digitalmente.

## Pendientes concretos para la descarga pública

1. Completar la correspondencia de versiones, fuentes y avisos de Cygwin y las
   bibliotecas nativas de PhotoRec/TSK. Revisar también las de Pillow y MuPDF.
2. Añadir las fuentes de MuPDF y las restantes dependencias a una nueva entrega,
   con instrucciones de construcción y manifiesto. El instalador existente no
   se modificó ni se reemplazó durante esta auditoría.
3. Probar ese instalador definitivo en Windows 11 sin Python/.NET de desarrollo:
   instalar, abrir, analizar una imagen sintética, previsualizar PDF/imagen,
   guardar una selección, cerrar, reabrir la recuperación y desinstalar.
4. Publicar instalador, fuentes correspondientes, huellas y notas de la misma
   versión; entonces actualizar el enlace de descarga de la web.

La firma digital es una mejora de distribución separada; no se presenta aquí
como requisito de las licencias abiertas.

## Reproducir inventario y pruebas básicas

```powershell
python scripts/audit_distribution.py C:/Revivo-stage C:/QA/inventario.json
python -m unittest discover -s tests -p 'test_*.py'
```

El inventario y registros completos se conservan localmente en la carpeta de QA;
no se publican catálogos, rutas personales ni archivos recuperados de usuarios.

## Fuentes consultadas

- [Licencias de TSK](https://www.sleuthkit.org/sleuthkit/licenses.php)
- [Licencias de PyMuPDF](https://pymupdf.io/licensing)
- [TestDisk y GPL v2+](https://www.cgsecurity.org/wiki/TestDisk)
- [Fuentes de MuPDF 1.27.1](https://mupdf.com/downloads/archive/mupdf-1.27.1-source.tar.gz)

La URL y versión de MuPDF se contrastaron además con `setup.py` del archivo
de fuentes PyMuPDF 1.27.1 incluido en la etapa.
