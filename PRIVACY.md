# TokenUsage Privacy Policy

**Effective date:** August 17, 2026  
**Publisher:** GVASTETHECREATOR

TokenUsage is a local-first Windows application that summarizes quota, token usage, cost, activity, and reset-cycle information made available by supported AI developer tools.

This policy describes what TokenUsage accesses, what it stores, when it uses the network, and the choices available to the user.

## 1. Data TokenUsage may access

Depending on the providers installed and enabled by the user, TokenUsage may read limited technical data from documented local sources such as:

- numeric usage and cost records;
- model or provider identifiers needed to classify those records;
- quota totals, remaining values, and reset times exposed through an approved local interface;
- timestamps, freshness indicators, and coverage metadata;
- local application configuration required to locate an approved data source;
- user-supplied API credentials only when the user explicitly enables an integration that requires them.

TokenUsage attempts to read the smallest source necessary to answer a usage question.

## 2. Content TokenUsage does not intentionally collect

TokenUsage is not designed to copy or store:

- prompts;
- responses;
- conversations;
- source-code contents;
- command text;
- tool-call contents;
- emails;
- customer documents;
- session tokens owned by another application;
- passwords stored by another application;
- account identifiers when they are not required for an explicitly enabled integration.

Provider adapters must not add such content to TokenUsage storage, logs, diagnostics, fixtures, issues, or reports.

## 3. Local storage

The installed MSIX application stores its normalized usage records, settings, cache, and operational metadata in its Windows package-local data area.

The portable distribution stores its data in the `Data` directory beside the portable application. The installed and portable distributions use separate data locations.

Local records may include:

- normalized numeric usage entries;
- provider status and freshness;
- pricing references used for estimates;
- quota observations and reset history;
- language, appearance, and application settings;
- diagnostic state that does not intentionally contain credentials or customer content.

TokenUsage data remains on the user's device unless the user explicitly enables a documented integration that communicates with an external service.

## 4. Credentials

When a user explicitly supplies a credential for a supported opt-in integration, TokenUsage uses Windows credential-protection facilities where supported. Credentials must not be written to ordinary logs, exported reports, screenshots, issue templates, or unprotected application settings.

TokenUsage does not copy credentials from another application's private credential store.

## 5. Network activity

Network access is off by default for provider integrations unless the user enables a feature that clearly requires it.

TokenUsage may use the network for the following documented purposes:

- an explicitly enabled provider API or local service connection;
- links the user chooses to open, such as project, documentation, support, or release pages;
- Microsoft Store delivery, licensing, crash analytics, and update infrastructure that is operated by Microsoft rather than by TokenUsage.

Each network-enabled provider integration must identify its purpose and data source in the product documentation. TokenUsage does not sell usage data or use it for advertising.

## 6. Reported and estimated values

TokenUsage distinguishes provider-reported values from estimates. Estimated cost may be calculated locally from token counts and known pricing data. Such estimates are not billing statements and are not transmitted merely because they are calculated.

## 7. Sharing and sale of data

GVASTETHECREATOR does not sell TokenUsage data.

TokenUsage does not intentionally share local usage records with GVASTETHECREATOR or third parties unless the user explicitly enables an integration whose operation requires communication with that third party.

## 8. Diagnostics and support

Users may choose to share logs, screenshots, exports, or diagnostic output when requesting support. Users should review these materials before sharing them.

The application and repository are designed to avoid credentials and customer content in diagnostics, but users remain responsible for verifying files they voluntarily attach to a public issue or message.

## 9. Data retention and deletion

Users can remove TokenUsage local data by using application controls where provided or by uninstalling/removing the applicable data directory.

- Removing the MSIX package removes package-managed application data according to Windows package behavior.
- Removing the portable application does not automatically delete a separately preserved portable `Data` folder.
- Data owned by third-party provider applications is not deleted by TokenUsage.

## 10. Security

TokenUsage uses platform security features and bounded readers intended to minimize access. No software can guarantee absolute security. Security issues should be reported privately according to [`SECURITY.md`](SECURITY.md), not through a public issue containing credentials or personal data.

## 11. Children's privacy

TokenUsage is a developer utility and is not directed to children. It does not knowingly collect personal information from children.

## 12. Changes to this policy

This policy may be updated when TokenUsage adds or changes data sources, network integrations, storage behavior, or legal requirements. Material changes will be reflected in this document and its effective date.

## 13. Contact

Privacy and support questions can be submitted through the public project support channels without including credentials, private prompts, conversations, customer content, or other sensitive information.

Repository: `gvastethecreator/tokenusage`

---

# Política de Privacidad de TokenUsage

**Fecha de vigencia:** 17 de agosto de 2026  
**Publicador:** GVASTETHECREATOR

TokenUsage es una aplicación local para Windows que resume información de cuota, uso de tokens, costos, actividad y ciclos de reinicio disponible en herramientas de desarrollo con IA compatibles.

Esta política describe qué datos puede consultar TokenUsage, qué guarda, cuándo utiliza la red y qué opciones tiene el usuario.

## 1. Datos que TokenUsage puede consultar

Según los proveedores instalados y habilitados por el usuario, TokenUsage puede leer datos técnicos limitados desde fuentes locales documentadas, por ejemplo:

- registros numéricos de uso y costo;
- identificadores de modelo o proveedor necesarios para clasificar esos registros;
- totales de cuota, valores restantes y fechas de reinicio expuestos por una interfaz local aprobada;
- marcas de tiempo, indicadores de actualidad y metadatos de cobertura;
- configuración local necesaria para localizar una fuente aprobada;
- credenciales proporcionadas por el usuario únicamente cuando habilita explícitamente una integración que las requiere.

TokenUsage intenta leer la fuente mínima necesaria para responder una consulta de uso.

## 2. Contenido que TokenUsage no recopila intencionalmente

TokenUsage no está diseñado para copiar ni almacenar:

- prompts;
- respuestas;
- conversaciones;
- contenido de código fuente;
- texto de comandos;
- contenido de llamadas a herramientas;
- correos electrónicos;
- documentos de clientes;
- tokens de sesión pertenecientes a otra aplicación;
- contraseñas guardadas por otra aplicación;
- identificadores de cuenta cuando no sean necesarios para una integración habilitada explícitamente.

Los adaptadores de proveedores no deben incorporar ese contenido al almacenamiento, logs, diagnósticos, fixtures, issues o reportes de TokenUsage.

## 3. Almacenamiento local

La aplicación MSIX instalada guarda registros normalizados, configuración, caché y metadatos operativos en el área de datos local del paquete de Windows.

La distribución portable guarda sus datos en la carpeta `Data` junto a la aplicación. Las distribuciones instalada y portable usan ubicaciones separadas.

Los registros locales pueden incluir:

- entradas numéricas normalizadas de uso;
- estado y actualidad de proveedores;
- referencias de precios utilizadas para estimaciones;
- observaciones de cuota e historial de reinicios;
- idioma, apariencia y configuración de la aplicación;
- estado de diagnóstico que no debe contener credenciales ni contenido del usuario.

Los datos permanecen en el dispositivo salvo que el usuario habilite explícitamente una integración documentada que se comunique con un servicio externo.

## 4. Credenciales

Cuando el usuario proporciona una credencial para una integración opt-in compatible, TokenUsage utiliza mecanismos de protección de credenciales de Windows cuando están disponibles. Las credenciales no deben escribirse en logs comunes, reportes exportados, capturas, issues ni configuración sin protección.

TokenUsage no copia credenciales desde el almacén privado de otra aplicación.

## 5. Actividad de red

El acceso de red para integraciones de proveedores está desactivado de forma predeterminada, salvo que el usuario habilite una función que lo requiera claramente.

TokenUsage puede utilizar la red para:

- una API de proveedor o servicio local habilitado explícitamente;
- enlaces que el usuario decide abrir, como proyecto, documentación, soporte o releases;
- distribución, licencias, análisis de fallos y actualizaciones de Microsoft Store operados por Microsoft.

Cada integración con red debe documentar su finalidad y fuente de datos. TokenUsage no vende datos de uso ni los utiliza para publicidad.

## 6. Valores reportados y estimados

TokenUsage distingue los valores informados por un proveedor de las estimaciones. El costo estimado puede calcularse localmente a partir de tokens y precios conocidos. Estas estimaciones no son facturas y no se transmiten por el simple hecho de calcularse.

## 7. Cesión o venta de datos

GVASTETHECREATOR no vende datos de TokenUsage.

TokenUsage no comparte intencionalmente registros locales con GVASTETHECREATOR ni con terceros salvo que el usuario habilite una integración cuya operación requiera comunicarse con ese tercero.

## 8. Diagnóstico y soporte

El usuario puede decidir compartir logs, capturas, exportaciones o diagnósticos al solicitar soporte. Debe revisar ese material antes de compartirlo.

La aplicación y el repositorio están diseñados para evitar credenciales y contenido privado en diagnósticos, pero el usuario debe verificar los archivos que adjunte voluntariamente a un issue o mensaje público.

## 9. Conservación y eliminación

El usuario puede eliminar datos locales mediante los controles disponibles o removiendo el directorio correspondiente.

- La eliminación del paquete MSIX quita los datos administrados por el paquete según el comportamiento de Windows.
- Eliminar la aplicación portable no borra automáticamente una carpeta `Data` preservada por separado.
- TokenUsage no elimina datos pertenecientes a aplicaciones de terceros.

## 10. Seguridad

TokenUsage utiliza funciones de seguridad de la plataforma y lectores acotados para minimizar el acceso. Ningún software puede garantizar seguridad absoluta. Los problemas de seguridad deben informarse de forma privada según [`SECURITY.md`](SECURITY.md), sin publicar credenciales ni datos personales.

## 11. Privacidad de menores

TokenUsage es una herramienta para desarrolladores y no está dirigida a menores. No recopila conscientemente información personal de menores.

## 12. Cambios en esta política

La política puede actualizarse si cambian las fuentes de datos, integraciones de red, almacenamiento o requisitos legales. Los cambios materiales se reflejarán aquí y en la fecha de vigencia.

## 13. Contacto

Las consultas de privacidad y soporte pueden enviarse por los canales públicos del proyecto sin incluir credenciales, prompts privados, conversaciones, contenido de clientes ni otros datos sensibles.

Repositorio: `gvastethecreator/tokenusage`
