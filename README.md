# ProTraining · back

5 microservicios .NET 8, cada uno con su propia base PostgreSQL:

| Servicio | Carpeta | Puerto local | Base (puerto local) |
|---|---|---|---|
| Identidad | `services/identidad` | 8081 | `identidad` (5433) |
| Estructura | `services/estructura` | 8082 | `estructura` (5434) |
| Entrenamiento | `services/entrenamiento` | 8083 | `entrenamiento` (5435) |
| Seguimiento | `services/seguimiento` | 8084 | `seguimiento` (5436) |
| Asistente | `services/asistente` | 8085 | `asistente` (5437) |

Adentro de Docker, todos los servicios escuchan en el **8080**.

Cada servicio tiene la misma estructura en capas (`Domain`, `Application`, `Infrastructure`, `API`) y ya trae:
- `GET /api/health`: chequea la conexión a su base (200 si está bien, 503 si no).
- Errores como ProblemDetails en español.
- Logs JSON con Serilog y el header `X-Correlation-Id` (si no viene, se genera; vuelve en la respuesta y sale en cada log como `CorrelationId`).
- Resiliencia estándar en todos los `HttpClient` (reintentos, timeout y circuit breaker).
- CORS con los orígenes desde la configuración.
- JWT Bearer configurado, todavía sin exigirlo en los endpoints.
- Swagger en `/swagger` (solo en Development).

El CRUD de atletas (`/api/athletes`) es solo un ejemplo y está únicamente en Estructura.

## Levantar todo con Docker

```bash
cp .env.example .env
docker compose up -d --build
```

Levanta los 5 servicios y sus 5 bases. Cada servicio espera a que su base esté sana, y si uno se cae, los demás siguen andando. Para probar:

```bash
curl http://localhost:8082/api/health
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

## Crear un servicio nuevo

1. Copiá `services/identidad` (es el más limpio) a `services/<servicio>` y renombrá el `.slnx`.
2. Sumá al `docker-compose.yml` su base `db-<servicio>` con su volumen y el servicio con su `Database__Host`, `Database__Name`, su puerto y el `depends_on`.
3. Compilá con `dotnet build services/<servicio>/API/API.csproj` y probá `/api/health`.
