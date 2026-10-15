---
name: informe-po
description: Arma un informe de avance para el PO, en palabras de negocio y sin detalles técnicos — qué se logró en el sprint, qué ya se puede mostrar, en qué se está trabajando y qué viene en el próximo sprint. Lee lo mergeado en develop del back y del front. Usar cuando alguien pide el informe para el PO, el resumen del sprint, "cómo venimos" para la demo, o escribe /informe-po.
---

# Informe de avance para el PO

Escribís un informe **en español** para el Product Owner de ProTraining. El PO no es técnico: le importa **qué puede hacer hoy un preparador físico (PF) o un atleta con la app**, qué falta y qué viene. No le importa cómo está hecho.

El tono es de **buena noticia**: mostrá cada avance en su mejor versión, con entusiasmo y en términos de valor para el PF y el atleta. Pero **nunca inventes ni infles**: si algo está a medias, se dice que está en camino, no que está listo. Un PO que descubre en la demo que algo "listo" no funciona deja de creer en el informe.

## Paso 1: definir el período

Los sprints son de 2 semanas, del 9/10 al 20/11/2026:

| Sprint | Desde | Hasta |
| ------ | ----- | ----- |
| 1      | 9/10  | 22/10 |
| 2      | 23/10 | 5/11  |
| 3      | 6/11  | 20/11 |

Por defecto, el informe cubre **el sprint en curso** según la fecha de hoy. Si quien te usa pide otro sprint o un rango de fechas, usá ese. Si te dice que las fechas de la tabla no son las reales, usá las que te diga.

## Paso 2: juntar lo que se hizo

Trabajás sobre **los dos repos**: este (`protraining-back`) y el front, que se clona al lado (`../protraining-front`). Si el front no está ahí, preguntá dónde está; si no lo tiene clonado, seguí solo con el back y aclaralo al final del informe.

En cada repo:

1. Traé lo último: `git fetch origin` (si falla, seguí con lo que haya y avisalo).
2. **Lo terminado** (mergeado a develop en el período):
   `git log origin/develop --merges --since=<desde> --until=<hasta> --pretty='%ad %s' --date=short`
   Cada PR mergeado trae el número de Jira en el nombre de la rama (`feature/KAN-332-catalogo-tests`). Ignorá los merges de `develop` hacia una rama (`Merge branch 'develop' into ...`).
3. **Lo que está en curso** (ramas que todavía no entraron a develop):
   `git branch -r --no-merged origin/develop --sort=-committerdate`
   Quedate con las que tuvieron commits en el período (`git log -1 --format=%ad --date=short <rama>`).
4. Si el nombre de la rama o los commits no alcanzan para entender **qué permite hacer** el cambio, mirá por arriba los archivos que tocó (`git show --stat <merge>`) y, si hace falta, el código. No hace falta leer todo el diff: buscás el "para qué", no el "cómo".

## Paso 3: lo planificado para el próximo sprint

Esto no está en git. En este orden:

1. Si tenés acceso a Jira (proyecto **KAN**), buscá las tareas del próximo sprint (`project = KAN AND sprint in futureSprints()`) y las del sprint actual que no están terminadas.
2. Si no, pedile a quien te usa que pegue la lista de tareas planificadas.
3. Si no la tiene, dejá la sección con "A completar con lo planificado en Jira". **No la inventes** a partir de lo que "se supone" que sigue.

## Paso 4: traducir a lenguaje de PO

Agrupá todo por **áreas del producto**, no por repo ni por persona:

| Área para el PO                   | Qué entra                                                                    |
| --------------------------------- | ---------------------------------------------------------------------------- |
| Acceso y cuentas                  | login, sesiones, roles, permisos, consentimiento del tutor (servicio Identidad) |
| Clubes, categorías y atletas      | organizaciones, categorías, atletas, membresías, ficha médica, invitaciones, calendario (Estructura) |
| Planificación y entrenamiento     | ejercicios, récords, planificaciones, rutinas, cálculo de cargas (Entrenamiento) |
| Seguimiento diario de los atletas | check-in, molestias y lesiones, semáforo, tests físicos, asistencia (Seguimiento) |
| Asistente con IA                  | borradores, adaptaciones, avisos, recomendaciones, proveedores de IA (Asistente) |
| La app                            | pantallas, navegación, instalación en el celular (front)                     |
| Base del proyecto                 | gateway, CI, Docker, contratos, convenciones, refactors                      |

Para cada avance, clasificalo en uno de estos tres estados:

- **Listo para mostrar**: tiene pantalla en el front **y** su parte del back mergeada. Se puede usar en la demo.
- **Listo por dentro**: el back está mergeado pero todavía no hay pantalla (o al revés: la pantalla existe pero todavía no está conectada a datos reales). Se cuenta como "el motor ya está, falta la pantalla" o "la pantalla ya está, falta conectarla".
- **En camino**: está en una rama sin mergear.

Cómo escribir cada avance:

- **Una línea, empezando por lo que habilita**, con el sujeto del lado del usuario: "El PF ya puede buscar ejercicios por nombre y músculo" en lugar de "Se hizo el endpoint de búsqueda de ejercicios".
- Si suma, agregá el **porqué** en media línea: "…así arma una rutina en segundos".
- Al final, entre paréntesis, los números de Jira: `(KAN-316)`. Varios PRs del mismo logro van en una sola línea: `(KAN-347, KAN-349)`.
- **Juntá** los pasos chicos de una misma funcionalidad en un solo logro. Cinco PRs de "entidades", "migración", "endpoint", "tests" y "ajustes" del check-in son un avance: "El check-in diario del atleta ya está listo por dentro".
- La **base del proyecto** va en **una o dos líneas** en total, en términos de beneficio: "Dejamos armada la base técnica para que el equipo trabaje más rápido y con menos errores: cada cambio se prueba solo antes de entrar". Nunca una lista de infraestructura.
- Los tests, refactors, arreglos de convenciones y comentarios **no** van como logro aparte: se suman al logro de la funcionalidad, o a la línea de base del proyecto, o se omiten.

### Palabras que no van

El PO no tiene que leer ninguna de estas: endpoint, API, back, front, microservicio, servicio (en el sentido técnico), base de datos, tabla, entidad, migración, DTO, JWT, token, refresh, CRUD, gateway, YARP, Docker, CI, PR, merge, rama, commit, refactor, deploy, seeder, contrato, OpenAPI, Ollama, Gemini, adapter, fallback, multi-tenant.

Traducciones típicas:

| Técnico                                       | Para el PO                                                          |
| --------------------------------------------- | ------------------------------------------------------------------- |
| Login con JWT y refresh token                 | El PF y el atleta pueden entrar con su cuenta y la sesión se mantiene |
| Filtro por PF / multi-tenant                  | Cada PF ve solo sus atletas; los datos de un club no se mezclan con los de otro |
| Motor de reglas del semáforo                  | La app ya detecta sola qué atletas necesitan atención               |
| Cálculo de cargas con redondeo a discos       | La app calcula el peso de cada ejercicio, redondeado a los discos que hay en el gimnasio |
| Cadena de proveedores de IA (Ollama → Gemini → reglas) | El asistente sigue respondiendo aunque falle la IA principal |
| Datos anonimizados a la IA externa            | Los nombres de los atletas nunca salen de la app                    |
| Exportar CSV                                  | El PF puede descargar los datos en una planilla                     |
| Entidades y migración de X                    | Ya está preparado el lugar donde se va a guardar X (listo por dentro) |
| App shell / PWA                               | La app se puede instalar en el celular como una app más             |

## Paso 5: escribir el informe

Guardalo en `notas/informe-po-sprint-<n>-<aaaa-mm-dd>.md` (la carpeta `notas/` no se sube a GitHub). Si ya existe uno del mismo día, actualizalo.

Usá esta estructura:

```markdown
# ProTraining · Avance del sprint <n>

> Del <desde> al <hasta> · Actualizado el <fecha de hoy>

## En pocas palabras
Tres o cuatro líneas con lo más importante del sprint: qué cambió para el PF y el atleta, y cuál es el próximo gran paso. Es lo único que va a leer el PO si está apurado, así que poné acá lo más vendedor que sea cierto.

## Lo que ya se puede mostrar
Los avances "listos para mostrar", agrupados por área. Si no hay ninguno todavía, decilo en positivo: "Este sprint fue de construir el motor; las primeras pantallas conectadas llegan en el próximo".

## Lo que ya funciona por dentro
Los avances "listos por dentro", agrupados por área, diciendo qué falta para verlos ("falta la pantalla").

## En qué estamos trabajando
Los avances "en camino", en una línea cada uno.

## Lo que viene en el próximo sprint
Lo planificado (Paso 3), en lenguaje de PO y agrupado por área.

## Para hablar con el PO
Solo si hay algo real: una decisión que necesitamos de él, un riesgo para la demo, algo que se movió de sprint. Si no hay, sacá la sección.
```

## Reglas

- Todo en **español**, frases cortas, sin jerga. Si dudás de si el PO entiende una palabra, cambiala.
- Basate solo en lo que hay en git, en Jira o en lo que te digan. Si algo no queda claro, preguntá antes de escribirlo.
- No nombres a los devs: el informe es del equipo.
- Sin emojis.
- No modifiques ningún archivo del proyecto salvo el informe en `notas/`.
- Al terminar, decí dónde quedó el archivo y mostrá la sección "En pocas palabras" en el chat.
