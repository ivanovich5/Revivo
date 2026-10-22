# Panel privado y operación

El servicio escucha únicamente en `127.0.0.1:18161`. Nginx publica exclusivamente `POST /api/feedback` y `POST /api/events`; `/admin` no está expuesto. Para administración, un túnel SSH autenticado reenvía `127.0.0.1:18162` hacia el servicio. Abrir `http://127.0.0.1:18162/admin` después de conectar. No publicar el puerto ni crear un proxy público hacia las rutas de administración.

Las mutaciones del panel validan Host y Origin local; no se habilita CORS. CSP impide scripts externos, scripts inline y marcos. El contenido de comentarios se presenta con `textContent`, sin interpretar HTML. La base de datos, notas, correos y respaldos son privados y nunca deben entrar al repositorio o al directorio público.

Los comentarios mantienen su esquema anterior y se migran de forma aditiva con estado y notas. El panel pagina de 50 en 50. Guardar los cambios antes de actualizar. No hay envío automático de respuestas.

Las estadísticas empiezan al activar esta versión: aperturas de página y clics del instalador, no visitantes únicos ni descargas completas. Un identificador aleatorio por evento evita duplicar un reintento. Se conserva hasta 48 horas y luego se limpia al llegar otro evento. Los totales se agregan por día UTC. No se guardan IP, correo, agente o referencia en las tablas de estadísticas. El registro de acceso normal de Nginx sigue separado y rota durante 14 días. Se respeta Do Not Track. Las estadísticas son orientativas, no antifraude.

## Respaldos

`revivo-maintenance.timer` ejecuta diariamente `maintenance.py` a las 09:15 UTC, con hasta 5 minutos de variación. Conserva los últimos 7 archivos diarios en `/var/backups/revivo/daily` (acceso root). Copia SQLite con su API de backup, verifica integridad, empaqueta web, backend y configuración, y verifica la restauración de la base archivada en un directorio temporal. No modifica la base de producción durante la comprobación. Los instaladores y fuentes se inventarían mediante SHA-256, sin duplicarlos cada día. Conservar también los originales de distribución fuera del servidor.

Los informes privados incluyen espacio, servicios, HTTPS, vigencia del certificado, renovación automática y disponibilidad de descarga. Las alertas aparecen al consultar el panel; no se envían notificaciones por correo. Comprobar periódicamente la fecha de revisión.

La herramienta local «Respaldar REVIVO» descarga el último archivo privado y su SHA-256, verifica la copia y confirma la fecha en el panel. No sube información a un tercero. Mantener una copia externa reciente: los respaldos del mismo servidor no cubren la pérdida de ese servidor. La copia externa manual no equivale a un respaldo externo automático.

## Restauración y despliegue

Antes de actualizar se guarda una copia privada de código, configuración y SQLite. Para restaurar, verificar SHA-256, trabajar en una carpeta privada, detener el servicio, recuperar la web y el backend, y restaurar la base preservando su propietario y permisos del StateDirectory. Validar SQLite y `nginx -t` antes de reiniciar. Nunca extraer un respaldo directamente dentro de la raíz pública: contiene datos privados. El archivo no incluye llaves SSH, llaves TLS ni configuración de otros sitios del servidor. Estos deben recuperarse por sus procesos propios.

El despliegue solo cambia la configuración del sitio REVIVO, añade el límite de frecuencia de eventos y un servicio/timer de mantenimiento. No toca la configuración de otros sitios.

Pruebas: `python test_feedback.py` comprueba migración, privacidad, origen, notas, deduplicación y paginación. `node test-download.cjs` comprueba que la descarga nativa no se cancela, el desplazamiento a apoyo y Do Not Track.
