# WOpenUsage

`WOpenUsage` es el nombre interno de una app Windows que muestra cuotas, reinicios y uso local de herramientas de IA desde las sesiones que ya existen en el equipo.

El producto final tendrá nombre, logo y paquete propios. OpenUsage permite reutilizar su código bajo MIT, pero reserva su nombre, logo e identidad visual. Este repo no representa al proyecto OpenUsage.

## Estado

Investigación y plan listos. La solución WinUI aún no se creó.

- Upstream estudiado: `robinebers/openusage` en `9d2bf09f10e21f769494a525a9d65c84d7aeb1df`.
- Clon local ignorado: `.reference/openusage`.
- Plataforma elegida: C#, WinUI 3 y Windows App SDK, con paquete MSIX de confianza plena.
- Primer proveedor: Codex mediante su `app-server` oficial por `stdio`.
- Claude: uso de logs locales viable; cuota restante bloqueada para distribución hasta contar con un contrato público o permiso del proveedor.

## Documentos

- [Investigación de viabilidad](docs/research/2026-07-21-openusage-windows-feasibility.md)
- [Especificación de producto](docs/PRODUCT-SPEC.md)
- [Arquitectura base](docs/architecture/ADR-0001-windows-native-baseline.md)
- [Matriz de proveedores](docs/PROVIDER-MATRIX.md)
- [Plan de implementación](docs/IMPLEMENTATION-PLAN.md)

## Principios

- La app no tendrá una cuenta propia.
- Cada proveedor debe tener una fuente local o pública comprobada.
- La app no copiará tokens a su propio almacén.
- Las claves que el usuario agregue de forma manual irán a Windows Credential Locker.
- La API local y la telemetría estarán apagadas al instalar.
- La UI mostrará cuándo un dato es remoto, local, estimado, incompleto o vencido.

## Inicio de la implementación

La primera fase crea la solución con la plantilla `winui-mvvm`, conserva el manifiesto y compila por arquitectura concreta (`x64` y luego `ARM64`). El orden, las pruebas y los criterios de salida están en el [plan](docs/IMPLEMENTATION-PLAN.md).
