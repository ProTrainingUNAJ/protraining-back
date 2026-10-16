---
name: convenciones
description: Revisa los cambios de la rama actual contra develop y marca todo lo que no sigue las convenciones de ProTraining (idioma, carpetas, entidad base, migraciones, rutas, configuración, Git). No revisa la lógica del código. Usar antes de commitear o abrir un PR, o cuando alguien escribe /convenciones.
---

# Revisión de convenciones de ProTraining

Revisás **solo convenciones**: nombres, idioma, ubicación de archivos, reglas del proyecto. **No** opinás sobre si la lógica está bien, si el algoritmo es eficiente ni sobre el diseño: eso va en la revisión del PR.

Quien te usa puede ser un dev junior. Explicá cada problema en español simple, citando la regla y mostrando cómo quedaría corregido.

## Paso 1: qué cambió

1. Rama actual: `git branch --show-current`.
2. Traé lo último de develop si se puede (`git fetch origin develop`; si pide contraseña o falla, seguí con lo que haya).
3. Archivos cambiados respecto de develop, incluyendo lo que todavía no está commiteado:
   - `git diff --name-status origin/develop...HEAD`
   - `git status --short`
4. Leé el diff de cada archivo de código (`git diff origin/develop...HEAD -- <archivo>` y `git diff -- <archivo>` para lo no commiteado). Revisá **solo las líneas que agregó o cambió esta rama**, no el código viejo. Si el código nuevo usa nombres viejos que ya existían (por ejemplo una propiedad en español de otra entidad), **no** lo marques: no es responsabilidad de esta rama.
5. Ignorá `bin/`, `obj/` y el contenido generado de `Migrations/` (salvo para la regla de migraciones).

Si no hay cambios, decilo y terminá.

## Paso 2: las reglas

Para cada regla, buscá incumplimientos en lo que cambió. La fuente de verdad es la sección **"Convenciones"** del `README.md` del repo: si contradice algo de acá, manda el README.

### 1. Idioma

**La regla: lo que lee una persona va en español; todo lo demás, en inglés.**

| Qué | Idioma | Ejemplo bien | Ejemplo mal |
|---|---|---|---|
| Carpetas y archivos (back y front) | **Inglés** | `src/pages/CheckIn/`, `Application/DTOs/Athletes/` | `src/paginas/`, `DTOs/Atletas/` |
| Clases, interfaces, métodos, propiedades, variables, parámetros, enums | **Inglés** | `CheckInService`, `sleepHours` | `ServicioCheckIn`, `horasSueno` |
| Rutas de la API y campos del JSON (código y contratos) | **Inglés** | `/api/v1/athletes`, `firstName` | `/api/v1/atletas`, `nombre` |
| Tablas y columnas de la base (`snake_case`) | **Inglés** | `athletes`, `first_name` | `atletas`, `nombre` |
| Textos de pantallas del front | **Español** | `"Guardar check-in"` | `"Save check-in"` |
| Mensajes que lee una persona: excepciones, errores de la API (ProblemDetails), validaciones, logs | **Español** | `throw new DomainException("El atleta no existe.")` | `throw new DomainException("Athlete not found.")` |
| Documentación (README, docs, descripción del PR) | **Español** | | |
| Comentarios en el código (incluidos los `///`) | **Español** | `// Solo los datos del PF del token` | |

Las **únicas excepciones** (no son error):
- `PfId` / `pf_id` (preparador físico), término del dominio.
- Los nombres de los servicios: carpetas `services/identidad`, `estructura`, `entrenamiento`, `seguimiento`, `asistente` y sus rutas en el gateway.
- Los roles: `"PreparadorFisico"`, `"Atleta"`, `"Tutor"`.

Los comentarios van en español para que todo el equipo entienda el código; un comentario en inglés **no** es error, no lo marques.

**El catálogo de ejercicios va en español** (`services/entrenamiento/API/Data/exercises.json`: nombres, equipamiento, músculos e instrucciones), porque lo lee el PF y el atleta. Es contenido, no código: los campos del JSON y las columnas siguen en inglés. Las instrucciones van en **español rioplatense, de vos** ("Acostate…", "Flexioná…"). Mezclar usted ("Acuéstese") o tú ("Flexiona") es ⚠️.

Marcá solo lo que **agregó o cambió esta rama**. Si hay muchos nombres en español, agrupalos en un solo punto con la lista de archivos.

### 2. Carpetas (cada servicio tiene `Domain`, `Application`, `Infrastructure`, `API`)

- Entidades → `Domain/Entities/`.
- DTOs y requests → `Application/DTOs/<Módulo>/` (lo compartido en `Application/DTOs/Common/`). Ejemplo: `Application/DTOs/Auth/LoginRequestDto.cs`. **Mal:** un DTO suelto en `Application/DTOs/`.
- Casos de uso → `Application/UseCases/<Módulo>/<CasoDeUso>/`. Ejemplo: `Application/UseCases/Athletes/CreateAthlete/`.
- Interfaces que implementa Infrastructure → `Application/Interfaces/Persistence/` o `Application/Interfaces/Services/`.
- Configuración de EF de cada entidad → `Infrastructure/Data/Configurations/<Entidad>Configuration.cs`.
- Controllers → `API/Controllers/`.
- **CQRS liviano:** cada caso de uso es un **Command** (escribe) o una **Query** (lee), con su handler, en `Application/UseCases/<Módulo>/<CasoDeUso>/`.
  - Escribe (crear, modificar, borrar) → `XxxCommand` + `XxxCommandHandler`, y el handler termina con `unitOfWork.SaveChangesAsync(...)`. Un handler que modifica y no guarda es ❌ (no persiste nada).
  - Lee → `XxxQuery` + `XxxQueryHandler`. **Nunca** llama a `SaveChanges` ni modifica entidades (❌), y lee con `AsNoTracking()` (si falta, ⚠️).
  - Un caso de uso con otro nombre (`XxxUseCase`, `XxxService` en `UseCases/`) es ⚠️: renombrarlo a Command o Query según si escribe o lee.
  - El Command o la Query y su handler pueden ir en el mismo archivo o en dos, pero **igual que el resto del servicio** (⚠️ si la rama mezcla las dos formas en un mismo servicio).
  - No se usa MediatR: los handlers se registran en `Program.cs` y se inyectan en el controller.
- Tests → **un solo proyecto por servicio**: `services/<servicio>/Tests/<Servicio>.Tests.csproj`, agregado al `<Servicio>.slnx` como `<Project Path="Tests/<Servicio>.Tests.csproj" />`, **sin** carpeta de solución (`<Folder>`). Si la rama crea un segundo proyecto de tests en el mismo servicio (por ejemplo `Entrenamiento.Tests/` al lado de `Tests/`), es ❌: los tests nuevos van al proyecto que ya existe. Si el proyecto no está en el `.slnx` o está dentro de un `<Folder>`, es ⚠️ (el CI lo corre igual porque busca cualquier `*Tests.csproj`).
- `Domain` no puede depender de `Application`, `Infrastructure` ni `API`. `Application` no puede depender de `Infrastructure` ni `API`. Revisá los `using` y los `.csproj`.

### 3. Entidades y datos

- Toda entidad nueva hereda de **`BaseEntity`** (`Domain/Common/BaseEntity.cs`), que ya trae `Id`, `PfId`, `CreatedAt` y `DeletedAt`. **No** las vuelvas a declarar en la entidad.
- **No** completes a mano `PfId`, `CreatedAt` ni `DeletedAt`: lo hace solo el `AppDbContext` al guardar.
- **No** filtres a mano por PF (`Where(x => x.PfId == ...)`) ni por borrados: el filtro global ya lo hace. Si ves eso, es un error.
- `IgnoreQueryFilters()` solo con un comentario que explique por qué (es saltarse la seguridad entre PFs).
- Borrar es `Remove(...)`: el `AppDbContext` lo convierte en borrado lógico. **No** hagas `DELETE` en SQL ni pongas `DeletedAt` a mano.
- El PF del pedido se obtiene con **`ICurrentUser`** (claim `pf_id`), nunca leyendo el token a mano.
- Los ids de otros servicios se guardan como `Guid`, **sin** relación de base (sin foreign key ni navegación a una entidad de otro servicio).
- Un servicio **nunca** accede a la base de otro: se hablan por HTTP.
- **Fechas: la app es, por ahora, solo para Argentina (UTC-3).**
  - En la base se guardan los instantes en **UTC** (`timestamp with time zone`, como `created_at`).
  - Pero **"hoy"** (la rutina del día, el check-in, el semáforo) se calcula en la hora de Argentina, zona `America/Argentina/Buenos_Aires`: `TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Argentina/Buenos_Aires")`. Mejor en un solo lugar reutilizable.
  - Usar `DateTime.UtcNow.Date`, `DateTime.UtcNow.DayOfWeek` o `DateOnly.FromDateTime(DateTime.UtcNow)` como "hoy" es ❌: después de las 21 h de Argentina da el día siguiente.
  - Un día sin hora (fecha de inicio de un plan, fecha del check-in) va como `DateOnly`.

### 4. Migraciones

- **Una sola migración por PR.** Si hay más de una nueva en `Infrastructure/Data/Migrations/`, avisá.
- Se crean con:
  ```bash
  dotnet ef migrations add <Nombre> --project services/<servicio>/Infrastructure --startup-project services/<servicio>/API --output-dir Data/Migrations
  ```
- El nombre de la migración en inglés y descriptivo (`AddCheckInTable`), no `Migracion1`.
- **Nunca** modificar una migración que ya está en develop. Si se ve un cambio en una migración vieja, es un error.
- Si la migración tiene `DropColumn` o `DropTable` donde parecía un renombre, avisá: puede perder datos (debería ser `RenameColumn`/`RenameTable`).

### 5. API

- **Todo error sale como ProblemDetails**, lanzando una excepción del proyecto (`DomainException` o una de `Application/Exceptions/`) que el `ExceptionMiddleware` traduce al código HTTP. **No** devuelvas errores armados a mano (`new { message = ... }`, un string, `BadRequest("...")`) ni `try/catch` que se trague la excepción.
- Si se crea una excepción nueva, tiene que estar mapeada en el `ExceptionMiddleware` con su código (por ejemplo, 401 para credenciales inválidas); si no, sale como 500.
- Si el cambio agrega o modifica un endpoint, revisá `contracts/<servicio>.yaml`:
  - Si el endpoint es **nuevo** y no aparece en el contrato, avisá como ⚠️: hay que sumarlo al contrato.
  - Si el endpoint ya está en el contrato pero la ruta o los campos no coinciden, avisá como ⚠️ (hoy hay diferencias conocidas entre contratos y código; el dev tiene que hablarlo con Javi, dueño de los contratos).
- Endpoints que necesitan usuario logueado: `[Authorize]`. Los públicos (login, registro, health) llevan `[AllowAnonymous]`. Mientras el proyecto no exija el JWT en todos lados, que falte `[AllowAnonymous]` es ⚠️, no ❌.
- Lo que solo puede hacer el PF (crear, modificar o borrar datos del PF) lleva `[Authorize(Roles = "PreparadorFisico")]`. Un `[Authorize]` sin rol en un endpoint que modifica es ❌: un atleta del mismo PF podría usarlo.

**REST nivel 2 (modelo de Richardson).** Toda la API lo cumple:
- **Recursos con sustantivos** en la URL, en plural e inglés: `/api/v1/athletes/{id}`, `/api/v1/training-plans`. Una acción en la URL (`/sugerir-rutina`, `/getAthlete`, `/createPlan`) es ❌. Excepción aceptada: `POST /api/v1/auth/login` y `/auth/refresh`.
- **El verbo correcto:** `GET` lee (nunca modifica), `POST` crea, `PUT` reemplaza, `PATCH` cambia una parte, `DELETE` borra.
- **El código correcto:**

  | Caso | Código |
  |---|---|
  | Lectura bien | `200` con el dato |
  | Creación bien | `201` con la ubicación de lo creado: `Created($"/api/v1/<recurso>/{id}", dto)`. `Created(string.Empty, …)` es ❌ |
  | Modificación o borrado sin cuerpo | `204` |
  | Datos inválidos | `400` |
  | Sin token | `401` |
  | Token válido pero sin permiso (rol) | `403` |
  | No existe | `404`, con una excepción propia mapeada en el `ExceptionMiddleware` (por ejemplo, `NotFoundException`). `KeyNotFoundException` u otra excepción sin mapear sale como `500`: es ❌ |
  | Conflicto (duplicado, concurrencia) | `409` |

- Si un caso devuelve `500` y no es un error inesperado del servidor, es ❌.
- La respuesta de éxito es el recurso (o su DTO), no un objeto armado a mano tipo `new { message = "…" }`.

### 6. Configuración y seguridad

- **Nunca** secretos en el repo: contraseñas, claves, tokens o connection strings en el código, en `appsettings.json` o en el compose. Van por variables de entorno (`.env`, que no se commitea).
  - Excepción: datos de prueba de un seed que **solo corre en Development** (por ejemplo la contraseña del usuario de prueba) son ⚠️, recomendando moverlos a una variable de entorno.
- Variables nuevas: agregadas a `.env.example` con un valor de ejemplo y explicadas en el README.
- **No** cambiar `<TargetFramework>net8.0</TargetFramework>`.
- Paquetes NuGet nuevos: versión `8.x` o compatible con .NET 8, y avisá que se agregó (hay que mencionarlo en el PR).
- No se usan Redis, Semantic Kernel ni un AI Gateway aparte. Si aparecen, avisá.
- Logs con `ILogger` (Serilog). **No** `Console.WriteLine`. No loguear contraseñas, tokens ni datos médicos.
- **Asistente / IA:** nunca mandar nombres de atletas a un modelo externo (Gemini). Si el cambio arma prompts con nombres, es un error grave.

### 7. Git

- La rama sale de `develop` y se llama `feature/KAN-XXX-descripcion` (o `fix/`, `refactor/`, `chore/`). Si se llama distinto o no tiene el número de Jira, avisá.
- El PR va a **`develop`**, nunca a `main`. (Esto y la descripción del PR no se pueden ver desde el diff: recordalo en una línea al final, no lo marques.)
- Archivos que no deberían estar en el commit: `.env`, `bin/`, `obj/`, `appsettings.Development.json`, archivos personales (`CLAUDE.md`, notas).

## Paso 3: compilar

Para cada servicio que cambió: `dotnet build services/<servicio>/API/API.csproj`. Si cambió `contracts/`: `npx @redocly/cli lint contracts/*.yaml`. Si algo falla, va primero en el informe: el CI lo va a rechazar igual.

## Paso 4: el informe

Respondé en español, con este formato (los ejemplos de código dentro del informe van en su propio bloque de código):

~~~
## Revisión de convenciones · <rama>

Revisé N archivos (lista corta).
Compilación: ✅ / ❌ (con el error) / no corrida.

### ❌ Hay que corregir (N)
1. **<regla>** · `ruta/archivo.cs:línea`
   Qué pasa: ...
   Cómo queda bien: (bloque de código con la versión corregida)

### ⚠️ Para revisar (N)
(cosas que pueden estar bien según el caso, por ejemplo una diferencia con el contrato)

### ✅ Bien
(qué reglas cumple; una línea por regla)
~~~

Agrupá problemas iguales en un solo punto con la lista de archivos y líneas, en vez de repetirlos.

Al final, una línea: "Listo para commitear" si no hay ❌, o "Corregí los ❌ y volvé a correr /convenciones".

**No corrijas nada por tu cuenta.** Si el usuario te pide que lo corrijas, recién ahí lo hacés.
