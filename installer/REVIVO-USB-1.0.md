# REVIVO-USB 1.0

Después de guardar correctamente una selección se ofrece liberar el espacio de
la imagen completa creada por REVIVO-USB. Se muestra el tamaño y la ruta exacta.
El usuario debe confirmar el borrado permanente; No es la respuesta predeterminada.
Cerrar el aviso o abrir la carpeta no elimina nada.

Solo se elimina usb.img, nunca su carpeta, archivos recuperados, vistas ni informes.
La imagen debe tener un mapa completo y source.json con identidad coincidente.
La imagen se vuelve a comprobar al confirmar y se abre con acceso exclusivo:
si falta, cambió o está en uso, la operación se bloquea. El borrado usa el identificador
del archivo abierto. No es borrado seguro del contenido físico del dispositivo.
Las copias importadas sin registros de adquisición no ofrecen esta acción.

Sin la imagen ya no se puede repetir el análisis, volver a extraer datos ni realizar
las comprobaciones que requieren la copia. Los archivos ya exportados y las vistas
locales se conservan. El usuario debe revisarlos y respaldarlos antes de borrar.

Validación: 42/42 pruebas, incluida eliminación de una imagen sintética de 512 bytes,
conservación de otros archivos y bloqueo por cambio, uso o mapa incompleto.
La numeración 1.0 no cierra las pruebas físicas y de otra computadora pendientes,
ni la identificación de ntfsprogs indicada en EMPAQUETADO-R2.md.
