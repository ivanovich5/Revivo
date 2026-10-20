# REVIVO-USB 1.0.1

Corrección del guardado de archivos seleccionados en una segunda USB o SSD externo.
La comprobación de imagen NTFS/exFAT ya no se reutiliza para la exportación.
FAT32 admite archivos de hasta 4,294,967,295 bytes; se comprueba cada archivo,
además del espacio total y un margen para el informe, antes de crear la carpeta.
La imagen y el espacio de trabajo siguen requiriendo NTFS/exFAT.

Para un destino USB se consulta el disco físico actual y se compara con la identidad
y capacidad registradas en source.json/request.json de la adquisición. Se bloquean
identificadores o series coincidentes. Si la capacidad es igual y faltan series
para distinguir los dispositivos, se pide un destino interno o identificable.
Las recuperaciones importadas sin datos de origen conservan el destino interno.
No se formatea ninguna unidad ni se repite el escaneo.

Se conservan la copia por archivo .partial y la verificación SHA-256 antes de
considerarlo exportado. Confirmar esta corrección con una segunda USB real.
El usuario informa que sus pruebas anteriores sí se hicieron en otra computadora;
no debe volver a describirse ese antecedente como una prueba aún no realizada.

La identificación de ntfsprogs se cerró: ver IDENTIFICACION-NTFSPROGS.md.
