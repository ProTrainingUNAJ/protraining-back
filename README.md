# ProTraining · back

5 microservicios .NET 8, cada uno con su propia base PostgreSQL:

| Servicio | Carpeta | Puerto local | Base (puerto local) |
|---|---|---|---|
| Identidad | `services/identidad` | 8081 | `identidad` (5433) |
| Estructura | `services/estructura` | 8082 | `estructura` (5434) |
| Entrenamiento | `services/entrenamiento` | 8083 | `entrenamiento` (5435) |
| Seguimiento | `services/seguimiento` | 8084 | `seguimiento` (5436) |
| Asistente | `services/asistente` | 8085 | `asistente` (5437) |

Adelante está el **gateway** (`gateway/`, YARP) en el puerto **8000**: es la única entrada para el front.

Adentro de Docker, todos los servicios y el gateway escuchan en el **8080**.

Cada servicio tiene la misma estructura en capas (`Domain`, `Application`, `Infrastructure`, `API`) y ya trae:
- `GET /health`: chequea la conexión a su base (200 si está bien, 503 si no). Lo usan el compose y el CI. `GET /api/health` sigue andando y devuelve el detalle en JSON.
- Errores como ProblemDetails en español.
- Logs JSON con Serilog y el header `X-Correlation-Id` (si no viene, se genera; vuelve en la respuesta y sale en cada log como `CorrelationId`).
- Resiliencia estándar en todos los `HttpClient` (reintentos, timeout y circuit breaker).
- CORS con los orígenes desde la configuración.
- JWT Bearer configurado, todavía sin exigirlo en los endpoints.
- Swagger en `/swagger` (solo en Development), con el nombre del servicio.
- La base del modelo de datos: entidad base, filtro por PF, borrado lógico y migraciones (ver más abajo).

El CRUD de atletas (`/api/athletes`) es solo un ejemplo y está únicamente en Estructura.

## Levantar todo con Docker

```bash
cp .env.example .env
docker compose up -d --build
```

Levanta el gateway, los 5 servicios y sus 5 bases. Cada servicio espera a que su base esté sana, y si uno se cae, los demás siguen andando. Para probar:

```bash
curl http://localhost:8082/health                     # Estructura directo
curl http://localhost:8000/estructura/api/athletes   # Estructura por el gateway
curl http://localhost:11434/api/tags                  # Ollama directo (modelos instalados)
```

Para sumar el front (tiene que estar clonado al lado, en `../protraining-front`), queda en `http://localhost:3000`:

```bash
docker compose --profile front up -d --build
```

Comandos útiles:

```bash
docker compose ps                        # estado de todo
docker compose logs -f estructura        # logs de un servicio
docker compose up -d --build estructura  # reconstruir un solo servicio
docker compose down                      # bajar todo (los datos quedan en los volúmenes)
docker compose down -v                   # bajar todo y borrar las bases
```

### Modelo local de IA (Ollama)

El compose incluye el contenedor oficial de **Ollama** en el puerto `11434` con el volumen persistente `ollama` para almacenar los modelos.

- **Modelo inicial para pruebas:** `llama3.2:1b` (se descarga automáticamente al iniciar el compose con el servicio auxiliar `ollama-model`).
- **Espacio en disco:** ~1.3 GB en el volumen `ollama`.
- **Memoria RAM requerida:**
  - En reposo (inactivo): ~200 MB.
  - En inferencia/ejecución: ~1.5 GB a 2.0 GB de RAM.
  - *(Como referencia, el modelo `llama3.2:3b` requiere ~3.5 GB a 4 GB de RAM).*


## Gateway

`gateway/` es un proxy con YARP. Sólo reparte los pedidos y resuelve CORS; **no valida tokens** (eso lo hace cada servicio).

| Ruta en el gateway | Va a |
|---|---|
| `/identidad/...` | Identidad (`/...`) |
| `/estructura/...` | Estructura |
| `/entrenamiento/...` | Entrenamiento |
| `/seguimiento/...` | Seguimiento |
| `/asistente/...` | Asistente |

Le saca el prefijo: `localhost:8000/estructura/api/athletes` llega a Estructura como `/api/athletes`. Las rutas están en `gateway/appsettings.json`.

- **CORS en un solo lugar**: lo resuelve el gateway, y no le pasa el header `Origin` a los servicios, así no se duplican los permisos.
- **`X-Correlation-Id`**: si el pedido no lo trae, el gateway lo genera y se lo pasa al servicio. El mismo id aparece en los logs de los dos.
- **Si el gateway se cae**, cada servicio sigue respondiendo directo en su puerto (8081-8085). Si se cae un servicio, sólo falla su ruta (502).
- `GET /health` del gateway dice si el gateway está vivo.

El front llama a `/api/<servicio>/...` y su nginx (o el Vite en desarrollo) le saca el `/api` y se lo pasa al gateway. Ejemplo: `/api/estructura/api/athletes` → gateway `/estructura/api/athletes` → Estructura `/api/athletes`.

## Correr un servicio con `dotnet run`

Útil para debuggear. Levantá solo su base con Docker y pasale las variables:

```bash
docker compose up -d db-estructura

export Database__Host=localhost Database__Port=5434 Database__Name=estructura \
       Database__User=protraining_app Database__Password=demo_app_cambiar_05 \
       Jwt__Key=clave_local_de_desarrollo_cambiar_32+ Jwt__Issuer=protraining-identidad Jwt__Audience=protraining

dotnet run --project services/estructura/API
```

> El SDK de .NET 8 no lee los `.slnx`: para compilar o restaurar desde la terminal usá el proyecto, por ejemplo `dotnet build services/estructura/API/API.csproj`.

## Variables de entorno

En el `.env` (las usa el compose):

| Variable | Para qué |
|---|---|
| `POSTGRES_USER`, `POSTGRES_PASSWORD` | Usuario y contraseña de las 5 bases |
| `JWT_KEY` | Clave para firmar y validar tokens (mínimo 32 caracteres) |
| `JWT_ISSUER`, `JWT_AUDIENCE` | Quién emite el token y para quién es |
| `ASPNETCORE_ENVIRONMENT` | `Development` habilita Swagger |

Lo que recibe cada servicio (el compose las arma solo):

| Variable | Para qué |
|---|---|
| `Database__Host`, `Database__Port`, `Database__Name` | Dónde está su base |
| `Database__User`, `Database__Password` | Usuario de la app (no puede ser `root`) |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Validación de tokens |
| `Cors__AllowedOrigins__0`, `__1`, … | Orígenes permitidos del front |
| `Serilog__MinimumLevel__Default` | Nivel de log (opcional) |

Si falta una variable obligatoria, el servicio no arranca y avisa cuál falta. Nunca subas secretos al repo: el `.env` no se commitea.

## Modelo de datos

### Entidad base

Toda entidad hereda de `BaseEntity` (`Domain/Common/BaseEntity.cs`):

| Campo | Columna | Quién lo completa |
|---|---|---|
| `Id` (Guid) | `id` | Se genera al crear la entidad |
| `PfId` | `pf_id` | El `AppDbContext` al guardar, con el PF del token |
| `CreatedAt` | `created_at` | El `AppDbContext` al guardar (UTC) |
| `DeletedAt` | `deleted_at` | El `AppDbContext` al borrar |

```csharp
public sealed class CheckIn : BaseEntity
{
    public Guid AtletaId { get; private set; }   // id de otro servicio: Guid, sin relación de base
    // ...
}
```

Las columnas se nombran en `snake_case` con `HasColumnName` en la configuración de cada entidad (`Infrastructure/Data/Configurations`). Las columnas de la base ya vienen configuradas.

### El PF del token

Cada servicio lee el PF del claim **`pf_id`** del JWT (`ICurrentUser`). Identidad tiene que poner ese claim al emitir el token: para un PF es su propio id, y para un atleta el id de su PF.

### Lo que hace solo el `AppDbContext`

- **Filtro global**: toda consulta sobre una entidad que hereda de `BaseEntity` trae sólo los datos del PF del token y nunca los borrados. Sin token no trae nada. No hace falta agregar `Where(x => x.PfId == ...)` a mano.
- **Al crear**: completa `PfId` y `CreatedAt`. Sin PF en el token, no deja guardar y responde 401.
- **Al borrar** (`Remove`): no borra la fila, completa `DeletedAt` (borrado lógico). `PfId` y `CreatedAt` no se pueden cambiar.
- Si en algún caso puntual hace falta ver todo (por ejemplo, un proceso interno), se usa `IgnoreQueryFilters()` a conciencia.

### Migraciones

Cada servicio aplica sus migraciones pendientes al arrancar. Para crear una (desde la raíz del repo):

```bash
dotnet tool restore   # la primera vez: instala dotnet-ef 8.0.11
dotnet ef migrations add <Nombre> \
  --project services/<servicio>/Infrastructure \
  --startup-project services/<servicio>/API \
  --output-dir Data/Migrations
```

No necesita la base ni variables de entorno. Va a mostrar un aviso de que falta `Database__User`: es normal, y sigue igual.

Reglas:
- Los cambios al modelo van por PR, **con una sola migración por PR**.
- Si dos migraciones del mismo servicio chocan, el segundo en mergear borra la suya, hace pull de `develop` y la vuelve a generar.

## Convenciones

### Idioma

**La regla: lo que lee una persona va en español; todo lo demás, en inglés.**

| En **español** | En **inglés** |
|---|---|
| Textos de las pantallas del front | Carpetas y archivos (back y front) |
| Mensajes de la API: errores (ProblemDetails), validaciones, excepciones | Clases, interfaces, métodos, propiedades, variables, parámetros |
| Emails y notificaciones | Comentarios en el código |
| Logs | Rutas de la API y campos del JSON (`/api/v1/athletes`, `firstName`) |
| Documentación: README, Jira, PRs, notas de `/documentar` | Tablas y columnas de la base (en `snake_case`: `athletes`, `first_name`) |
| | Claves de configuración y valores internos (enums) |

**Las únicas excepciones** (quedan en español a propósito):
- **`PfId` / `pf_id`** (preparador físico): término del dominio.
- **Los nombres de los servicios**: carpetas `services/identidad`, `estructura`, `entrenamiento`, `seguimiento`, `asistente` y sus rutas en el gateway (`/estructura/...`).
- **Los roles**: `PreparadorFisico`, `Atleta`, `Tutor`.

Todo lo nuevo sigue esta regla. Lo que ya existe y no la cumple se va corrigiendo en las tareas de cada uno.

### Carpetas

DTOs y requests van por módulo dentro de `Application/DTOs/`:

```
Application/
  DTOs/
    Common/      ← HealthDto y lo compartido
    Auth/        ← LoginRequestDto, LoginResponseDto, UserDto
    Athletes/    ← AthleteDto, CreateAthleteRequest
  UseCases/
    Auth/Login/
    Athletes/CreateAthlete/
```

## Comandos de IA para el equipo

En `.agents/` hay dos comandos que funcionan con el asistente de IA que uses (Antigravity u otro compatible con *skills*):

- **`/convenciones`**: revisa los cambios de tu rama contra `develop` y te dice qué no sigue las convenciones de este README (idioma, carpetas, entidad base, migraciones, contratos, secretos, ramas), con cómo corregirlo. No revisa la lógica. Usalo **antes de commitear o abrir el PR**.
- **`/documentar`**: escribe en español qué hiciste en tu rama, cómo funciona paso a paso, los conceptos que usaste, cómo probarlo y las preguntas que te pueden hacer en la defensa. Lo guarda en `notas/<rama>.md`, que no se sube a GitHub.

Cómo se usan:
- **Antigravity**: escribí `/convenciones` o `/documentar` en el chat del agente (son *workflows*, en `.agents/workflows/`).
- **Otro asistente**: pedile "seguí las instrucciones de `.agents/skills/convenciones/SKILL.md`".

Las instrucciones están en un solo lugar: `.agents/skills/<comando>/SKILL.md`. Si cambian las convenciones, se actualizan este README y ese archivo.

## Crear un servicio nuevo

1. Copiá `services/identidad` (es el más limpio) a `services/<servicio>` y renombrá el `.slnx`.
2. Sumá al `docker-compose.yml` su base `db-<servicio>` con su volumen y el servicio con su `Database__Host`, `Database__Name`, su puerto y el `depends_on`.
3. Sumá su ruta y su cluster en `gateway/appsettings.json`, y el servicio a la lista del CI (`.github/workflows/ci.yml`).
4. Compilá con `dotnet build services/<servicio>/API/API.csproj` y probá `/health`.
