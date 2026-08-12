# Implementación de paridad de proveedores

Fecha: 2026-08-12

## Resultado

TokenUsage registra la unión de proveedores inspeccionada en:

- [OpenUsage](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab)
- [CodexBar](https://github.com/steipete/CodexBar/tree/26ebaf9d5b0949e3b57fafcde0ed54aa3b27b3d2)
- [CodeBurn](https://github.com/getagentseal/codeburn/tree/d78bdb21f86025702376778fb27035cd3938956b)

El catálogo tiene 55 identidades: 10 activas, 1 opt-in, 35 preparadas y 9
bloqueadas por política. Un módulo preparado solo aporta identidad,
capacidades, estado e icono. No abre archivos ni inventa datos.

## Fuentes activas

| Proveedor | Fuente que lee TokenUsage | Datos | Costo | Límites |
|---|---|---|---|---|
| Codex | `codex app-server` y logs locales admitidos | tokens, modelos, uso diario, resets | informado o catálogo | interfaz local oficial |
| Claude | JSONL bajo la raíz de Claude Code | tokens, modelos y fecha | informado; si falta, catálogo | no disponible sin API pública |
| Cursor | proyección allowlist de `state.vscdb` | contadores reales por turno; contexto estimado para registros antiguos | valor API estimado para modelos conocidos | no disponible sin API pública o clave Admin propia |
| Grok Build | `unified.jsonl` o resumen local compatible | tokens, modelos y fecha | informado o catálogo | no disponible sin API pública |
| OpenCode | SQLite o almacenamiento JSON local | tokens, modelos y fecha | informado o catálogo | no hay una cuota común entre proveedores |
| Antigravity | metadatos numéricos locales | tokens, modelos y fecha | catálogo | bloqueado por política |
| Amp | `ledger.jsonl` | tokens, modelo y fecha | los créditos no son USD; valor API estimado cuando hay precio | no disponible |
| Mux | `session-usage.json` | tokens agregados por modelo y fecha | informado; si falta, catálogo | no disponible |
| Goose | proyección numérica de solo lectura sobre `sessions.db` | tokens acumulados, modelo, proveedor y fecha | informado si el esquema lo trae; si falta, catálogo | no disponible |
| Hermes | proyección numérica de solo lectura sobre `state.db` | tokens agregados, modelo, proveedor y fecha | informado si el esquema lo trae; si falta, catálogo | no disponible |

Los lectores de Amp, Mux, Goose y Hermes no abren threads, transcripts, mensajes,
comandos ni tool calls. Los IDs de sesión o mensaje se convierten en hashes
antes de crear eventos de uso.

## Cómo se calculan los costos

TokenUsage conserva tres estados separados:

1. `ProviderReported`: la fuente guarda un importe USD explícito. Este valor
   tiene prioridad.
2. `CatalogEstimated`: hay tokens reales y una coincidencia exacta con un
   modelo del catálogo. Es valor API estimado, no el cargo de una suscripción.
3. `Unavailable`: falta el importe o el modelo no tiene un precio comprobado.
   Los tokens siguen visibles como `unpriced`.

La estimación usa precios por millón de tokens:

`input × inputRate + output × outputRate + reasoning × outputRate + cacheRead × cacheReadRate + cacheWrite × cacheWriteRate`

El resultado se divide por `1,000,000`. Los catálogos se versionan y los modelos
desconocidos nunca heredan el precio de un modelo parecido. Amp guarda créditos
propios; TokenUsage no los etiqueta como USD.

## Cómo se obtienen los límites

Codex es la única integración activa que entrega límites de sesión mediante
una interfaz local oficial. TokenUsage también conserva Vercel AI Gateway como
opt-in porque puede usar una clave entregada directamente por el usuario.

Algunos upstreams obtienen más cuotas porque reutilizan cookies, tokens OAuth,
bearer tokens de editores o endpoints privados. TokenUsage no adopta esas
rutas. Claude, Cursor, Copilot, Grok y otros muestran uso local observado, pero
no una cuota restante ficticia. Un límite nuevo necesita una API pública con
scope de solo lectura o una clave que el usuario entregue a TokenUsage.

## Módulos sin reader activo

Preparados: `alibaba-cloud`, `anthropic`, `azure-openai`, `codebuff`,
`codewhale`, `copilot`, `crush`, `cursor-agent`, `deepseek`, `devin`, `droid`,
`forge`, `gemini-api`, `gemini-cli`, `groq`, `ibm-bob`, `kiro`,
`lingtai-tui`, `mistral`, `mistral-vibe`, `moonshot`, `ollama`, `omp`,
`open-design`, `openai`, `openclaude`, `openclaw`, `openrouter`, `pi`,
`quickdesk`, `qwen-cli`, `roo-code`, `warp`, `xai` y `zerostack`.

Opt-in: `vercel-ai-gateway`.

Bloqueados: `cline`, `cline-cli`, `kilo-code`, `kimi-cli`, `kimi-code`,
`perplexity`, `zai`, `zcode` y `zed`.

## Regla para continuar

Un módulo pasa a activo solo cuando dispone de una proyección numérica local
sin contenido, una API pública con credencial propia o una interfaz oficial de
solo lectura. El cambio debe incluir reader acotado, fixture sanitizado,
composición runtime, diagnóstico, cobertura de costo y prueba de paquete.
