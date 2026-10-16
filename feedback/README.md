# Receptor privado de feedback

Python 3 (biblioteca estándar), SQLite, systemd y Nginx. No envía correo ni ofrece un panel público. Guarda únicamente fecha, tipo, mensaje, versión y correo opcionales; los registros normales de Nginx pueden incluir IP y metadatos de las solicitudes.

Instalar `feedback.py` en `/opt/revivo-feedback/`. El servicio de ejemplo escucha en `127.0.0.1:18161`; no exponer ese puerto a Internet. systemd crea el directorio privado de estado y asigna un usuario dinámico. La base de datos es `/var/lib/revivo-feedback/feedback.sqlite3`: nunca copiarla al directorio web ni al repositorio.

Ejemplo para el contexto `http` de Nginx:

```nginx
limit_req_zone $binary_remote_addr zone=revivo_feedback:1m rate=2r/m;
```

Dentro del servidor HTTPS de la página:

```nginx
location = /api/feedback {
    client_max_body_size 8k;
    limit_req zone=revivo_feedback burst=3 nodelay;
    limit_req_status 429;
    proxy_pass http://127.0.0.1:18161;
    proxy_connect_timeout 3s;
    proxy_read_timeout 15s;
}
```

El backend valida origen y campos e incluye un campo trampa; el límite de frecuencia depende de Nginx. Ajustar los orígenes permitidos si cambia el dominio. Aplicar HTTPS. No ejecutar pruebas contra mensajes reales ni borrar datos para reiniciar el servicio. La administración, exportación, retención y eliminación de comentarios se realiza por acceso autorizado al servidor; no hay autenticación de administración web implementada.
