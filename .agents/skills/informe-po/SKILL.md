---
name: informe-po
description: Arma el resumen del sprint para el PO de UN servicio de ProTraining (Identidad, Estructura, Entrenamiento, Seguimiento o Asistente), en palabras de negocio y sin detalles técnicos — qué se puede mostrar, qué está listo por dentro, qué está en camino y qué viene — y lo publica en Notion, en Documentación PO. Cuenta el back del servicio y sus pantallas del front. Usar cuando alguien pide el resumen o informe para el PO, "cómo venimos" para la demo, o escribe /informe-po <servicio>.
---

# Resumen del sprint para el PO, por servicio

Escribís en **español** el resumen de **un servicio** para el Product Owner de ProTraining y lo publicás en Notion. El PO no es técnico: le importa **qué puede hacer hoy un preparador físico (PF) o un atleta con la app**, qué falta y qué viene. No le importa cómo está hecho.

El tono es de **buena noticia**: mostrá cada avance en su mejor versión, con entusiasmo y en términos de valor para el PF y el atleta. Pero **nunca inventes ni infles**: si algo está a medias, se dice que está en camino, no que está listo. Si el PO descubre en la demo que algo "listo" no funciona, deja de creer en el resumen.

Cada servicio tiene su página en Notion, y el resumen cubre **todo lo del servicio en el sprint**, no solo lo de quien lo corre. Si dos personas del mismo squad lo corren, la segunda actualiza la misma página.

## Reglas de git

Solo **leés**. Nunca cambies de rama ni toques el código de quien te usa: nada de `git checkout`, `git switch`, `git pull`, `git reset` ni `git stash`. Para leer develop usá `origin/develop` con `git log` y `git show`.

## Paso 1: el servicio y el período

**Servicio.** Es el argumento: `/informe-po entrenamiento`. Los válidos son `identidad`, `estructura`, `entrenamiento`, `seguimiento` y `asistente`. Si no lo pasaron, mirá qué carpeta de `services/` tocó la rama actual y proponelo ("¿Lo armo para Seguimiento?"). Si no hay forma de saberlo, preguntá.

**Sprint.** Los sprints son de 2 semanas, del 9/10 al 20/11/2026:

| Sprint | Desde | Hasta |
| ------ | ----- | ----- |
| 1      | 9/10  | 22/10 |
| 2      | 23/10 | 5/11  |
| 3      | 6/11  | 20/11 |

Por defecto, el del día de hoy. Si piden otro, usá ese. Si te dicen que las fechas no son las reales, usá las que te digan.

## Paso 2: lo que se hizo en el servicio

Trabajás sobre **los dos repos**: este (`protraining-back`) y el front, clonado al lado (`../protraining-front`). Si el front no está ahí, preguntá dónde está. Si no lo tiene clonado, seguí solo con el back y avisalo en el chat.

1. **Traé lo último:** `git fetch origin` en cada repo. Si falla (por ejemplo, por permisos), seguí con lo que haya descargado y **avisalo en el chat** al terminar: el resumen puede no tener lo último.

2. **Back, lo terminado:** los PRs mergeados a develop en el período que tocaron el servicio:
   `git log origin/develop --first-parent --merges --since=<desde> --until=<hasta> --pretty='%h %ad %s' --date=short -- services/<servicio>`
   El número de Jira está en el nombre de la rama (`feature/KAN-332-catalogo-tests`). **Descartá los PRs que tocan los cinco servicios a la vez** (plantilla, Swagger, validación de tokens, renombres, convenciones): son base del proyecto, no avances del servicio. Para saberlo: `git show --stat <hash>`.

3. **Back, lo que se sacó después:** para cada logro, fijate si un commit posterior lo revirtió o lo sacó:
   `git log origin/develop --since=<fecha del merge> --oneline -i --grep='revert' --grep='remov' --grep='sacar' --grep='elimin' -- services/<servicio>`
   Si algo se sacó, **no lo cuentes** como logro.

4. **Back, en curso:** ramas sin mergear con commits en el período que tocan el servicio:
   `git branch -r --no-merged origin/develop --sort=-committerdate`
   y para cada una, `git diff --stat origin/develop...<rama> -- services/<servicio>`.

5. **Front:** el front no está separado por servicio. Listá los PRs mergeados a develop en el período (mismo `git log`, sin la carpeta) y quedate con los de pantallas de este servicio, usando la etiqueta de squad de su tarea en Jira (ver la tabla del Paso 4) o, si no tenés Jira, el nombre de la rama y las pantallas que tocó.
   Para saber si una pantalla **ya usa datos reales**, buscá en el código de `origin/develop` si llama a `src/api/` (`git show origin/develop:<archivo>`). Una pantalla que no llama a la API es un diseño sin conectar.

6. Si el nombre de la rama no alcanza para entender **qué permite hacer** el cambio, mirá por arriba los archivos que tocó. Buscás el "para qué", no el "cómo".

## Paso 3: Jira

Si tenés acceso a Jira (sitio `protraining.atlassian.net`, proyecto **KAN**):

1. **Las tareas del servicio en este sprint**, con su estado: `project = KAN AND sprint in openSprints() AND labels = squad-<X>`. Las de squad A son de Identidad y Estructura: separalas por el nombre de la tarea o su épica.
2. **Las del próximo sprint**: lo mismo con `sprint in futureSprints()`. Quedate con las del sprint siguiente.
3. **Diferencias entre Jira y git**: una tarea mergeada en develop que en Jira sigue "En curso" o "Tareas por hacer", o una "Finalizada" sin PR mergeado. **No va en el resumen del PO**: avisalo en el chat al terminar, para que alguien actualice Jira.

Si no tenés Jira, pedile a quien te usa que pegue las tareas pendientes de este sprint y las del próximo. Si no las tiene, dejá esas secciones con "A completar con lo planificado en Jira". **No las inventes** a partir de lo que "se supone" que sigue.

## Paso 4: traducir a lenguaje de PO

| Servicio      | Squad en Jira | Qué es para el PO                                                        |
| ------------- | ------------- | ------------------------------------------------------------------------ |
| Identidad     | `squad-A`     | Acceso y cuentas: login, sesiones, roles, consentimiento y permisos del tutor |
| Estructura    | `squad-A`     | Clubes, categorías y atletas: organizaciones, membresías, ficha médica, invitaciones, calendario |
| Entrenamiento | `squad-B`     | Planificación y entrenamiento: ejercicios, récords, planificaciones, rutinas, cálculo de pesos |
| Seguimiento   | `squad-D`     | Seguimiento diario de los atletas: check-in, molestias y lesiones, semáforo, tests físicos, asistencia |
| Asistente     | `squad-C`     | Asistente con IA: borradores, adaptaciones, avisos, recomendaciones |

Para cada avance, elegí uno de estos estados:

- **Listo para mostrar**: tiene pantalla en el front que usa datos reales **y** su parte del back mergeada. Se puede usar en la demo.
- **Listo por dentro**: el back está mergeado pero no hay pantalla, o la pantalla existe pero todavía no usa datos reales. Se cuenta como "el motor ya está, falta la pantalla" o "la pantalla ya está, falta conectarla".
- **En camino**: está en una rama sin mergear, o en Jira "En curso".

Cómo escribir cada avance:

- **Una línea, empezando por lo que habilita**, del lado del usuario: "El PF ya puede buscar ejercicios por nombre y músculo" en lugar de "Se hizo el endpoint de búsqueda de ejercicios".
- Si suma, agregá el **porqué** en media línea: "…así arma una rutina en segundos".
- Al final, entre paréntesis, los números de Jira: `(KAN-316)`. Varios PRs del mismo logro van en una sola línea: `(KAN-347, KAN-349)`.
- **Juntá** los pasos chicos de una misma funcionalidad en un solo logro. Cinco PRs de "entidades", "migración", "endpoint", "tests" y "ajustes" del check-in son un avance: "El check-in diario del atleta ya está listo por dentro".
- Los tests, refactors, arreglos y comentarios **no** van como logro aparte: se suman al logro de la funcionalidad o se omiten.

### Palabras que no van

El PO no tiene que leer ninguna de estas: endpoint, API, back, front, microservicio, servicio (en el sentido técnico), base de datos, tabla, entidad, migración, DTO, JWT, token, refresh, CRUD, gateway, YARP, Docker, CI, PR, merge, rama, commit, refactor, deploy, seeder, contrato, OpenAPI, Ollama, Gemini, adapter, fallback, multi-tenant.

Traducciones típicas:

| Técnico                                       | Para el PO                                                          |
| --------------------------------------------- | ------------------------------------------------------------------- |
| Login con JWT y refresh token                 | El PF y el atleta entran con su cuenta y la sesión se mantiene      |
| Filtro por PF / multi-tenant                  | Cada PF ve solo a sus atletas; los datos de un club no se mezclan con los de otro |
| Motor de reglas del semáforo                  | La app ya detecta sola qué atletas necesitan atención               |
| Cálculo de cargas con redondeo a discos       | La app calcula el peso de cada ejercicio, redondeado a los discos que hay en el gimnasio |
| Cadena de proveedores de IA (Ollama → Gemini → reglas) | El asistente sigue respondiendo aunque falle la IA principal |
| Datos anonimizados a la IA externa            | Los nombres de los atletas nunca salen de la app                    |
| Exportar CSV                                  | El PF puede descargar sus datos en una planilla                     |
| Entidades y migración de X                    | Ya está preparado el lugar donde se va a guardar X (listo por dentro) |
| App shell / PWA                               | La app se instala en el celular como una app más                    |

## Paso 5: el resumen

Usá esta estructura. Las secciones vacías se sacan, salvo "Lo que viene".

```markdown
# <Servicio> · Sprint <n>

> <Qué es para el PO, de la tabla del Paso 4> · Del <desde> al <hasta> · Actualizado el <fecha de hoy>

## En pocas palabras
Dos o tres líneas con lo más importante del servicio en el sprint: qué cambió para el PF y el atleta, y cuál es el próximo paso. Es lo único que va a leer el PO si está apurado: poné acá lo más vendedor que sea cierto.

## Lo que ya se puede mostrar
Los avances "listos para mostrar". Si todavía no hay ninguno, decilo en positivo: "Este sprint fue de construir el motor; las pantallas llegan en <cuándo, según Jira>".

## Lo que ya funciona por dentro
Los avances "listos por dentro", diciendo qué falta para verlos.

## En qué estamos trabajando
Los avances "en camino", una línea cada uno.

## Lo que viene
**Para cerrar este sprint (hasta el <hasta>):** las tareas del sprint actual que todavía no empezaron, agrupadas en logros. Si es el último día del sprint o ya terminó, sacá esta parte.
**Sprint <n+1>:** lo planificado, agrupado en logros.

## Para hablar con el PO
Solo si hay algo real: una decisión que necesitamos de él, un riesgo para la demo, algo que se movió de sprint. Si no hay, sacá la sección.
```

## Paso 6: publicarlo en Notion

En el Notion de ProTraining, **Documentación PO** tiene un apartado por sprint, y adentro una página por servicio.

1. Si tenés las herramientas de Notion, buscá la página "Documentación PO", entrá al apartado del sprint (por ejemplo "Sprint 1") y abrí la página del servicio.
2. **Si la página está vacía**, escribí el resumen ahí.
3. **Si ya tiene contenido**, mostrale a quien te usa la sección "En pocas palabras" y preguntale si la reemplazás entera. El resumen cubre todo el sprint del servicio, así que la versión nueva incluye lo anterior. Si alguien agregó notas a mano que no salen de git ni de Jira, avisá antes de pisarlas.
4. **Si no existe la página del servicio** o el apartado del sprint, creala con el mismo formato de nombre que las demás (por ejemplo "Seguimiento").
5. **Si no tenés Notion conectado**, guardá el resumen en `notas/informe-po-<servicio>-sprint-<n>.md` (la carpeta `notas/` no se sube a GitHub) y decile a quien te usa que lo pegue en la página del servicio: Notion convierte el formato solo. Recordale que puede conectar el MCP de Notion en Antigravity para que la próxima vez se publique solo.

## Paso 7: cerrar en el chat

Al terminar, en el chat (no en Notion):

- El link a la página de Notion, o dónde quedó el archivo.
- La sección "En pocas palabras".
- Las diferencias entre Jira y git que encontraste (Paso 3).
- Si el `git fetch` falló, o si no tenías el front o Jira.

## Reglas

- Todo en **español**, frases cortas, sin jerga. Si dudás de si el PO entiende una palabra, cambiala.
- Basate solo en lo que hay en git, en Jira o en lo que te digan. Si algo no queda claro, preguntá antes de escribirlo.
- No nombres a los devs: el resumen es del equipo.
- Sin emojis.
- No modifiques ningún archivo del proyecto. Lo único que escribís es la página de Notion o, si no hay Notion, el archivo en `notas/`.
