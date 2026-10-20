# REVIVO-USB 1.0.2

El guardado por selección muestra tiempo restante estimado a partir del avance
real de copia y verificación SHA-256. Primero indica que está calculando; al
terminar los datos indica que finaliza el informe. La velocidad puede variar.
El diálogo del nombre muestra un aviso discreto al seleccionar un destino externo.

Cancelar (o cerrar la ventana de progreso) solicita detener la operación. Se
espera a que termine la operación de entrada/salida en curso; no se fuerza el
cierre ni se elimina la recuperación original. Los archivos completos y verificados
se conservan, y un archivo en curso conserva la extensión .partial. Se escribe
un informe con los archivos completados y se informa cuántos quedaron guardados.
Cancelar no muestra el mensaje de guardado completo ni ofrece eliminar la imagen.

Pruebas: guardado por lote con progreso ordenado y hashes correctos, cancelación
durante la verificación y después del primer archivo; comprobación de informes,
conservación de archivos originales y del archivo parcial. Pruebas con datos
sintéticos; confirmar comportamiento en la memoria externa del usuario.
