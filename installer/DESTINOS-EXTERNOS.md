# REVIVO-USB 0.9.19: copia de seguridad en almacenamiento externo

En Cambiar carpeta se admite disco interno, otra USB o disco externo USB/SSD.
El volumen debe ser NTFS o exFAT, disponible y asociado a un solo disco físico.
No se admiten FAT32, red, enlaces, discos virtuales ni volúmenes compuestos.
La capacidad libre sigue incluyendo la reserva de trabajo del proceso completo.

La interfaz valida el destino antes de crear el caso. El ayudante elevado vuelve
 a comprobar que no pertenece al disco de origen antes de escribir informes,
y el motor de adquisición lo comprueba antes de crear la imagen. Dos letras
asociadas al mismo disco físico no son destinos diferentes.

La imagen y el espacio de trabajo pueden abrirse y analizarse desde el destino
externo. La exportación manual por selección conserva por ahora su requisito
de disco interno; la reutilización/formateo conserva sus restricciones anteriores.
No se formatea el destino automáticamente. Mantener ambos dispositivos conectados.
Una desconexión o falta de espacio no constituye una imagen completa. No se añade
reanudación automática de memorias físicas con identidad insuficiente.

Validación: pruebas de política con volúmenes simulados, bloqueo antes de crear
carpetas o leer el origen, pruebas existentes sobre imágenes sintéticas y flujo
integrado. Pendiente: prueba con dos dispositivos físicos, SSD externo y desconexión
controlada usando medios de prueba sin datos importantes. No se usó una USB real.

Se conservan las limitaciones de procedencia descritas en EMPAQUETADO-R2.md.
