# REVIVO

**Tus archivos, primero.** Herramienta beta para Windows 11 que busca archivos de memorias USB dañadas o RAW trabajando sobre una copia protegida.

Sitio web: https://revivo.digital · Versión del código: **0.9.17 beta**.

## Qué incluye

- Aplicación de escritorio WPF y auxiliares de adquisición y reutilización.
- Copia sector por sector, escaneo con motores externos y búsqueda por firmas.
- Revisión de archivos, vistas previas locales y guardado por tipo.
- Diagnóstico de lectura; no certifica escritura, retención ni fiabilidad futura.
- Página web estática y receptor privado de feedback para un servidor propio.

No se garantiza recuperar todos los archivos ni conservar sus nombres y carpetas originales. Las comprobaciones estructurales no demuestran integridad completa. La reutilización con borrado es opcional y exige verificaciones y confirmación explícita.

## Estructura

| Carpeta | Contenido |
|---|---|
| `src/` | Aplicación .NET, motores de coordinación y auxiliares |
| `scripts/` | Validación, vistas previas y preparación de motores |
| `tests/` | Pruebas y generadores de imágenes sintéticas |
| `installer/` | Configuración de Inno Setup y auditoría de distribución |
| `web/` | Página pública HTML/CSS/JavaScript |
| `feedback/` | Receptor de mensajes y servicio systemd de ejemplo |

## Compilar y probar

Consulta [BUILD.md](BUILD.md). Los motores y el runtime Python se preparan aparte. Publicar el proyecto WPF no crea por sí solo un instalador completo.

Este repositorio no incluye instaladores, imágenes de USB, archivos recuperados, registros de usuarios, credenciales ni bases de datos de feedback. No hay todavía una GitHub Release pública del instalador: está pendiente completar la revisión de distribución de sus dependencias.

## Comentarios y contribuciones

Puedes [enviar feedback](https://revivo.digital/#feedback) o abrir un Issue. No adjuntes archivos recuperados, imágenes de memorias, claves, números de serie ni datos personales. Lee [CONTRIBUTING.md](CONTRIBUTING.md) antes de contribuir.

## Licencia

El código propio de REVIVO se publica bajo **AGPL-3.0-only**; véase [LICENSE.txt](LICENSE.txt). Las bibliotecas, motores y fuentes mantienen sus licencias: [THIRD_PARTY.md](THIRD_PARTY.md). Silkscreen se incluye bajo OFL 1.1. El repositorio contiene código propio y recursos de marca del proyecto; no incorpora los paquetes binarios de terceros ni afirma ser una entrega completa de sus fuentes correspondientes.
