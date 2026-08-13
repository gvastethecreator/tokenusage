# Ledger de rendimiento — 2026-08-13

El informe canónico está en `docs/performance/performance-review-2026-08-13.md`.

| Métrica | Baseline | Resultado |
| --- | ---: | ---: |
| Ingesta 10k | 1218 ms | 385 ms |
| Cursor 10k antiguas + 100 recientes | 123 ms / 10,100 filas | 22 ms / 100 filas |
| OpenCode 5k mensajes / 15k partes | 5504 ms | 38 ms |

Gate final: 1081/1081 pruebas y paquete MSIX Release x64 correcto. Revisión independiente: `ACCEPT`.
