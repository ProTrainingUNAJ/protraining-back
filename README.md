# ProTraining · back

API .NET 8 en capas (`Domain`, `Application`, `Infrastructure`, `API`). Es la plantilla de los 5 microservicios: Identidad, Estructura, Entrenamiento, Seguimiento y Asistente.

Ya trae:
- `GET /api/health`: chequea la conexión a la base (200 si está bien, 503 si no).
- Errores como ProblemDetails en español.
- Logs JSON con Serilog y el header `X-Correlation-Id` (si no viene, se genera; vuelve en la respuesta y sale en cada log como `CorrelationId`).
- Resiliencia estándar en todos los `HttpClient` (reintentos, timeout y circuit breaker).
- CORS con los orígenes desde la configuración.
- JWT Bearer configurado, todavía sin exigirlo en los endpoints.
- Swagger en `/swagger` (sólo en Development).

## Levantarlo local

Requisitos: SDK de .NET 8 y un PostgreSQL 16.

```bash
# 1. Una base de prueba (opcional, si no tenés una)
docker run -d --name protraining-db -p 5432:5432 \
  -e POSTGRES_DB=protraining_demo -e POSTGRES_USER=protraining_app \
  -e POSTGRES_PASSWORD=demo_app_cambiar_05 \
  -v "$PWD/db:/docker-entrypoint-initdb.d" postgres:16-alpine

# 2. Variables de entorno
cp .env.example .env
set -a; source .env; set +a

# 3. Correr la API
dotnet run --project API
```

Con Docker:

```bash
docker build -t protraining-api .
docker run --rm -p 8080:8080 --env-file .env -e Database__Host=host.docker.internal protraining-api
curl http://localhost:8080/api/health
```

El contenedor siempre escucha en el puerto **8080**.

> El SDK de .NET 8 no lee `ProTraining.slnx`: para compilar o restaurar desde la terminal usá el proyecto, por ejemplo `dotnet build API/API.csproj`.

## Variables de entorno

| Variable | Para qué | Ejemplo |
|---|---|---|
| `Database__Host` | Host de PostgreSQL | `localhost` |
| `Database__Port` | Puerto de PostgreSQL | `5432` |
| `Database__Name` | Nombre de la base | `protraining_demo` |
| `Database__User` | Usuario de la app (no puede ser `root`) | `protraining_app` |
| `Database__Password` | Contraseña | |
| `Jwt__Key` | Clave para firmar y validar tokens (mínimo 32 caracteres) | |
| `Jwt__Issuer` | Quién emite el token | `protraining-identidad` |
| `Jwt__Audience` | Para quién es el token | `protraining` |
| `Cors__AllowedOrigins__0`, `__1`, … | Orígenes permitidos del front | `http://localhost:5173` |
| `Serilog__MinimumLevel__Default` | Nivel de log (opcional) | `Information` |

Si falta una variable obligatoria, la API no arranca y avisa cuál falta. Nunca subas secretos al repo: el `.env` real no se commitea.

## Crear un servicio nuevo a partir de la plantilla

1. Copiá las carpetas `Domain`, `Application`, `Infrastructure` y `API`, más el `Dockerfile` y el `.dockerignore`, a `services/<servicio>/`.
2. Borrá lo del ejemplo de atletas que no corresponda (entidad, configuración, repositorio, casos de uso, controller y sus registros en `Program.cs` y `DependencyInjection.cs`). El `/api/health`, los middlewares y la configuración se quedan.
3. Dale al servicio su propia base: sus propias variables `Database__*`. Ningún servicio usa la base de otro.
4. Compilá con `dotnet build services/<servicio>/API/API.csproj` y probá `/api/health`.
