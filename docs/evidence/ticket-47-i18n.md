# Ticket 47: i18n inicial en inglés y español

Fecha: 2026-07-22

## Resultado

TokenUsage ahora ofrece `en-US` y `es-ES` en la app empaquetada. Opciones permite
elegir idioma; el cambio muestra un aviso de reinicio y se conserva tras el
reinicio de la instancia.

El arranque usa primero `ApplicationLanguages.PrimaryLanguageOverride`. Si no
existe, normaliza el primer idioma preferido: cualquier familia inglesa llega a
`en-US`, cualquier familia española a `es-ES` y el resto llega a `en-US`. Antes
de cargar XAML guarda el valor canónico en el override, para que los recursos
WinUI y `CultureInfo` usen la misma elección.

Los formatos visibles de gasto y tokens salen de recursos y de la cultura
activa. Por ejemplo, la muestra usa `$48.12` y `1.24M` en inglés, y `48,12 US$`
y `1,24 M` en español. El centro compacto del donut usa `48,12$` para no cortar
el valor dentro del anillo.

La llamada a `AppInstance.Restart` conserva el override durante un reinicio
correcto. Si la API devuelve un motivo de fallo, restaura el valor anterior y la
UI muestra el error localizado. La semántica se basa en la documentación de
Microsoft para [AppInstance.Restart](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-restart).

## Cobertura

- El manifiesto declara solo `en-US` y `es-ES`.
- `LocalizationContractTests` comprueba paridad exacta, valores no vacíos,
  marcadores de formato, `x:Uid`, lookups literales y el manifiesto.
- `AppLanguageCatalogTests` cubre override guardado, fallback de familias e
  idioma no compatible.
- `AppLanguageRestartArgumentsTests` conserva todos los flags `--test-*` y
  `--theme=*`, incluso con espacios, comillas o barra final.
- La automatización empaquetada cambia inglés y español, reinicia, comprueba
  flyout, bandeja, Opciones, estados de error, formato, límites de texto, UIA y
  los tres flags de fixture. Después repone el idioma inicial.

## Revisión y delegación

Grok Build `0.2.106` revisó el diseño y la implementación mediante snapshots
aislados dentro de `.snapshots/grok/`. La revisión final reanudada devolvió
`accept`; señaló como riesgos no bloqueantes el whitelist parcial de flags y la
falta de una prueba aislada del runtime. El parent amplió el whitelist, añadió
pruebas puras y volvió a ejecutar el ciclo empaquetado.

El review mínimo posterior de Grok agotó su límite sin emitir resultado. No se
usa como evidencia. La revisión local del diff, el formateo, el build y las
pruebas siguientes cubren esa corrección.

Resultados locales de Grok:

- `.snapshots/grok/t47-final-review-v2/.scratch/agent-cli-delegation/grok-build/t47-final-review-v2-followup/result.json`
- `.snapshots/grok/t47-repair-review/.scratch/agent-cli-delegation/grok-build/t47-repair-review/invocation.json`

## Prueba local

- `scripts/check.ps1 -Platform x64 -Configuration Debug`: Architecture 59,
  Core 60, CLI 2, Providers 169 y Platform Windows 52; solución con 0 avisos y
  0 errores.
- `BuildAndRun.ps1 ... -SkipRun /p:Platform=x64`: compilación empaquetada
  correcta.
- `BuildAndRun.ps1 ... -SkipRun /p:Platform=ARM64`: compilación cruzada
  empaquetada correcta.
- `dotnet format TokenUsage.slnx --verify-no-changes --no-restore`: correcto.
- `tests/ui/ticket-47-i18n.ps1`: 15/15 en la app empaquetada. El ciclo usa
  `--test-claude-config`, `--test-grok-home` y `--test-opencode-data`; verifica
  que los tres siguen presentes después del reinicio.
- Capturas: `artifacts/ticket-47/01-spanish-restart-english.png`,
  `02-english-sample.png`, `03-english-error.png`,
  `04-english-restart-spanish.png`, `05-spanish-options.png`,
  `06-spanish-sample.png` y `07-spanish-error.png`.

## Límite actual

El ejecutable CLI aún no expone un comando visible hasta Ticket 25. Sus dos
pruebas de este ticket ejecutan la agregación bajo ambas culturas; no se
presentan como prueba de una UX pública de CLI que todavía no existe.
