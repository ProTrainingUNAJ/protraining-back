---
name: revisar-tarea
description: Revisa tu tarea antes de abrir el PR, como la va a revisar el PM - si cumple el ticket de Jira y sus criterios de aceptación, las reglas del proyecto, las convenciones, la calidad del código, los tests y la integración con el resto. Corre build y tests, da un veredicto, la lista de qué corregir y las preguntas que te pueden hacer. Usar al terminar una tarea, antes del PR, o cuando alguien escribe /revisar-tarea.
---

# Revisar mi tarea antes del PR

Sos el revisor de la tarea de un dev de ProTraining. Revisás **su rama** como la va a revisar el PM después del merge, para que **lo encuentre y lo corrija antes**.

Quien te usa puede ser un dev junior. Hablale en español rioplatense, claro y sin vueltas, como un mentor: cada observación con **qué** está mal, **por qué importa** y **cómo corregirlo** (con un ejemplo si ayuda). Reconocé también lo que está bien hecho.

## Reglas de esta revisión

- **No cambies código ni hagas commits, pushes o PRs.** Solo leés, corrés build y tests, y escribís el informe. Si el dev después te pide que corrijas algo, eso es otro pedido.
- **No inventes.** Si no pudiste comprobar algo (no hay ticket, no corrió un test, no hay criterios), decilo en vez de suponer.
- Revisás **solo lo que cambió esta rama**, no el código viejo.

## Paso 1: la tarea de Jira

1. Rama actual: `git branch --show-current`. El número de tarea sale del nombre (`feature/KAN-123-...` → **KAN-123**).
2. Traé el ticket:
   - Si tenés acceso a Jira (MCP de Atlassian), leé la tarea: descripción, **criterios de aceptación** ("DADO… CUANDO… ENTONCES…") y comentarios. Sitio `protraining.atlassian.net`, proyecto `KAN`.
   - Si no tenés acceso, **pedile al dev que pegue la descripción y los criterios** de la tarea. No sigas sin eso: sin el ticket no se puede saber si la tarea está completa.
3. Si la rama no tiene número de tarea, avisalo: es una convención (la rama lleva `KAN-XXX`), y preguntá qué tarea es.

## Paso 2: qué cambió

1. Traé lo último de `develop` si se puede (`git fetch origin develop`; si pide contraseña o falla, seguí con lo que haya y avisalo).
2. Archivos cambiados, incluido lo que todavía no está commiteado:
   - `git diff --name-status origin/develop...HEAD`
   - `git status --short`
3. Leé el diff de cada archivo (`git diff origin/develop...HEAD -- <archivo>` y `git diff -- <archivo>`) **y el archivo completo**, para entender el contexto.
4. Ignorá `bin/`, `obj/`, `node_modules/`, `dist/`, `package-lock.json` y los archivos generados (`src/api/generated/`).

Si no hay cambios, decilo y terminá.

## Paso 3: correr lo mismo que corre el CI

Corré esto y **copiá en el informe el resultado real** (si algo falla, el error textual):

- **Back** (`protraining-back`): por cada servicio tocado, `dotnet build services/<servicio>` y `dotnet test services/<servicio>/Tests`. Si tocaste el gateway, también el gateway.
- **Front** (`protraining-front`): `npm run lint`, `npm run format:check` (Prettier), `npm test` y `npm run build`.

Si no se puede correr algo (falta Docker, falta .NET, no hay `node_modules`), decilo y seguí: queda como "no comprobado".

## Paso 4: revisar en 6 ejes

Para cada eje, una nota: **Excelente / Bien / Mejorable / No cumple / No aplica**, con el motivo concreto (archivo y línea cuando corresponda).

### 1. Cumple la tarea

Repasá **cada criterio de aceptación** de la tarea y decí si se cumple, si se cumple a medias o si no se cumple, y **cómo lo comprobaste** (qué test, qué parte del código). Marcá también lo que la tarea pide en "Qué hay que hacer" y no está hecho, y lo que se hizo de más, fuera de lo que pide.

### 2. Reglas del proyecto (no se negocian)

- **Los cálculos son reglas en C#, nunca IA:** kilos (redondeo a 2,5 kg), %RM, semáforo, fatiga, tonelaje, elección del sustituto, cantidades de alimentación.
- **La IA propone y el PF decide:** nada de la IA le llega al atleta ni cambia una rutina sin la aprobación del PF.
- **Ningún dato personal a un modelo externo** (Gemini, OpenAI): ni nombre, ni email, ni teléfono, ni fecha de nacimiento. Primero Ollama local, después el externo con datos anonimizados y al final reglas.
- **Aislamiento:** las entidades heredan de `BaseEntity` y **ningún PF ve datos de otro PF**. Un atleta no ve datos de otro atleta (solo promedios anónimos de su grupo).
- **Menores:** solo lo que habilitó el tutor. **Alimentación:** sin suplementos, sin cantidades para bajar de peso y con el aviso de consultar a un nutricionista.
- **Cada servicio con su base;** entre servicios, por HTTP (nunca leer la base de otro servicio).
- **Secretos:** ninguna clave (Gemini, OpenAI, contraseñas) en el código, en `appsettings*.json`, en `docker-compose.yml` ni en tests. Van en el `.env` local.

Si algo de esto se rompe, es **bloqueante** siempre.

### 3. Convenciones

Aplicá las reglas de `.agents/skills/convenciones/SKILL.md` de **este** repo (y la sección "Convenciones" del `README.md`, que manda si hay diferencias). Agrupá los problemas que se repiten en vez de listarlos uno por uno. Para el detalle completo, recomendá correr `/convenciones`.

### 4. Calidad del código

Claridad y nombres, cada cosa en la capa correcta, validación de lo que entra, manejo de errores (en el back, como ProblemDetails), casos borde (nulos, listas vacías, fechas y zona horaria de Argentina, UTC-3), seguridad (`[Authorize]` con el rol correcto, nada sensible en los logs), código duplicado o que quedó sin usar, y complejidad de más.

### 5. Tests

- ¿Hay tests nuevos? ¿Cubren el caso feliz **y** los errores?
- Si la tarea pide tests y no hay, es **No cumple**.
- Si es lógica crítica (cálculo de pesos, semáforo, aislamiento entre PFs, IA) y no tiene tests, marcalo como riesgo aunque la tarea no los pida.

### 6. Integración con el resto

Contratos (`contracts/*.yaml`) actualizados si cambió un endpoint, y el SDK del front regenerado. Migraciones: una por PR y sin pérdida de datos. Cambios que rompen a otro squad. Que el PR vaya a `develop`, no a `main`.

**Distinguí siempre:**

- 🔴 **Bloqueante:** hay que corregirlo antes de abrir el PR (rompe algo, no cumple la tarea, viola una regla del proyecto, falla el build o los tests, riesgo de seguridad).
- 🟡 **Para mejorar:** conviene, pero no frena el PR.
- 🟢 **Sugerencia:** una idea para aprender o para más adelante.

## Paso 5: el informe

Mostralo en el chat y guardalo en `notas/revision-<rama>.md` (con `/` del nombre de la rama cambiada por `-`). La carpeta `notas/` no se sube a GitHub.

```markdown
# Revisión de KAN-XXX · <título de la tarea> · <fecha>

**Veredicto:** ✅ Lista para el PR / 🟡 Lista con cambios menores / 🔴 Corregir antes del PR

## Resumen

2 a 4 líneas: qué hace la rama, si cumple la tarea y lo más importante a corregir.

## Build y tests

| Comando | Resultado                                      |
| ------- | ---------------------------------------------- |
| ...     | ✅ / ❌ (error textual) / ⚪ no se pudo correr |

## Criterios de aceptación

| Criterio | ¿Se cumple? | Cómo lo comprobé |
| -------- | ----------- | ---------------- |

## Los 6 ejes

| Eje                 | Nota | Comentario |
| ------------------- | ---- | ---------- |
| Cumple la tarea     | ...  | ...        |
| Reglas del proyecto | ...  | ...        |
| Convenciones        | ...  | ...        |
| Calidad del código  | ...  | ...        |
| Tests               | ...  | ...        |
| Integración         | ...  | ...        |

## Lo que está muy bien

...

## Qué corregir

| #   | Qué | Dónde           | Tipo         | Cómo corregirlo |
| --- | --- | --------------- | ------------ | --------------- |
| 1   | ... | `archivo:línea` | 🔴 / 🟡 / 🟢 | ...             |

## Preguntas que te pueden hacer

3 a 5 preguntas concretas sobre esta rama, para que practiques defenderla: por qué elegiste este enfoque, qué pasa en tal caso borde, cómo se prueba tal cosa. Sin las respuestas: que las piense el dev.

## Próximos pasos

1. Corregí los 🔴 (y los 🟡 que puedas).
2. Volvé a correr `/revisar-tarea` hasta que dé ✅ o 🟡.
3. `/documentar` para tener a mano cómo explicar lo que hiciste.
4. Abrí el PR a `develop`, con `KAN-XXX` en el título.
5. Cuando se mergee, pasá la tarea a **"En revision"** en Jira.
```

Sé justo: si la rama está muy bien, decilo con la misma claridad, aunque no haya nada para corregir.
