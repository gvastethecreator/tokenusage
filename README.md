# WOpenUsage

`WOpenUsage` es el nombre interno de una app Windows que muestra cuotas, reinicios, tokens y gasto local de herramientas de IA desde las sesiones que ya existen en el equipo.

El producto final tendrá nombre, logo y paquete propios. OpenUsage permite reutilizar su código bajo MIT, pero reserva su nombre, logo e identidad visual. Este repo no representa al proyecto OpenUsage.

## Estado

Investigación y plan listos. La solución WinUI aún no se creó.

- Upstream estudiado: `robinebers/openusage` en `9d2bf09f10e21f769494a525a9d65c84d7aeb1df`.
- Referencias de gasto local: `getagentseal/codeburn@6e3c57a9ff95a624f1d9affa7384d32a67f359b7` y `kenn-io/agentsview@1ee2de88e2dae54326d8b47aeb2de2f58b5944f9`.
- Clones locales ignorados: `.reference/openusage`, `.reference/codeburn` y `.reference/agentsview`.
- Plataforma elegida: C#, WinUI 3 y Windows App SDK, con paquete MSIX de confianza plena.
- Primer proveedor: Codex mediante su `app-server` oficial por `stdio`.
- Claude: uso de logs locales viable; cuota restante bloqueada para distribución hasta contar con un contrato público o permiso del proveedor.
- Grok Build y OpenCode: tokens y gasto local dentro de la beta; la cuota Grok queda sujeta a una interfaz pública o permiso.
- Antigravity CLI: solo lectura pasiva de datos locales en fase experimental; la app no usa su login ni consulta servicios privados.

## Documentos

- [Investigación de viabilidad](docs/research/2026-07-21-openusage-windows-feasibility.md)
- [Investigación de Grok, Antigravity, OpenCode y gasto local](docs/research/2026-07-21-agent-costs-and-quotas.md)
- [Especificación de producto](docs/PRODUCT-SPEC.md)
- [Arquitectura base](docs/architecture/ADR-0001-windows-native-baseline.md)
- [Matriz de proveedores](docs/PROVIDER-MATRIX.md)
- [Plan de implementación](docs/IMPLEMENTATION-PLAN.md)

## Principios

- La app no tendrá una cuenta propia.
- Cada proveedor debe tener una fuente local o pública comprobada.
- La app no copiará tokens a su propio almacén.
- El motor de gasto será propio y pequeño: no indexará transcripciones, herramientas ni comandos.
- Las claves que el usuario agregue de forma manual irán a Windows Credential Locker.
- La API local y la telemetría estarán apagadas al instalar.
- La UI mostrará cuándo un dato es remoto, local, estimado, incompleto o vencido.

## Inicio de la implementación

La primera fase crea la solución con la plantilla `winui-mvvm`, conserva el manifiesto y compila por arquitectura concreta (`x64` y luego `ARM64`). El orden, las pruebas y los criterios de salida están en el [plan](docs/IMPLEMENTATION-PLAN.md).
