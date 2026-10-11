---
name: documentar
description: Escribe en español una explicación clara de lo que se hizo en la rama actual (qué, por qué, cómo funciona, cómo probarlo y cómo defenderlo), pensada para que un dev junior la entienda y pueda defender su código. Usar cuando alguien termina una tarea o escribe /documentar.
---

# Documentar lo que hice

Escribís un documento **en español** que le explica a quien hizo los cambios qué hizo, de forma que lo entienda de verdad y pueda **defenderlo** frente al profesor o al equipo. El código está en inglés; la explicación, en español.

Pensá que quien lo lee es un dev junior: explicá los conceptos, no des nada por sabido, pero sin relleno.

## Paso 1: entender el cambio

1. Rama actual: `git branch --show-current`. Si tiene el número de Jira (`KAN-XXX`), anotalo.
2. Traé develop si se puede (`git fetch origin develop`; si falla, seguí).
3. Mirá todo lo que cambió respecto de develop, incluido lo no commiteado:
   - `git log --oneline origin/develop..HEAD`
   - `git diff --stat origin/develop...HEAD` y `git status --short`
   - El diff completo de los archivos de código.
4. **Leé los archivos enteros que cambiaron**, no solo el diff, para entender cómo encajan.
5. Si el cambio toca un endpoint, leé su parte en `contracts/<servicio>.yaml`.
6. Si el usuario te dio contexto (el ticket, qué quería lograr), usalo.

Si algo no se entiende o parece incompleto (por ejemplo, un endpoint sin validación), **no lo inventes**: anotalo en "Dudas para revisar".

## Paso 2: escribir el documento

Guardalo en `notas/<rama-sin-barras>.md` en la raíz del repo (por ejemplo `notas/feature-KAN-305-login-jwt.md`). La carpeta `notas/` no se sube a GitHub: es para cada uno. Si el archivo ya existe, actualizalo en vez de pisarlo, conservando lo que siga siendo cierto.

Usá esta estructura:

```markdown
# <Título corto de lo que se hizo> (KAN-XXX)

> Rama `<rama>` · Servicio: <servicio> · Fecha: <fecha>

## En una frase
Qué hace esto, para alguien que no sabe nada del proyecto.

## Para qué sirve
El problema que resuelve dentro de ProTraining y quién lo usa (el PF, el atleta, otro servicio).

## Qué hice
Lista de los cambios, agrupados por capa:
- **Domain:** ...
- **Application:** ...
- **Infrastructure:** ...
- **API:** ...
- **Base de datos:** tablas o columnas nuevas, y la migración.

## Cómo funciona, paso a paso
El recorrido de un pedido real, desde que llega hasta que responde. Ejemplo:
1. El front manda `POST /api/v1/auth/login` con `{ email, password }`.
2. `AuthController.Login` recibe el pedido y llama a `LoginUseCase`.
3. ...
Nombrá los archivos y métodos reales (`services/identidad/Application/UseCases/Auth/Login/LoginUseCase.cs`).

## Conceptos que usé
Cada concepto técnico que aparece, explicado en 2 o 3 líneas y relacionado con este código. Por ejemplo: JWT, claim, DTO, caso de uso, migración, borrado lógico, filtro global por PF, inyección de dependencias, hash de contraseña. Solo los que aparecen en este cambio.

## Decisiones y por qué
Cada elección importante y su motivo. Si hubo alternativas, cuál se descartó y por qué. Si el motivo no está claro en el código, ponelo en "Dudas para revisar" en vez de inventarlo.

## Cómo probarlo
Comandos concretos para levantarlo y probarlo:
- `docker compose up -d --build <servicio>`
- La URL de Swagger o el `curl` con el body de ejemplo, y qué respuesta esperar (y un caso de error).

## Preguntas que te pueden hacer (y cómo responderlas)
Entre 5 y 8 preguntas típicas de una defensa sobre este cambio, con una respuesta corta y concreta basada en el código. Por ejemplo: "¿Qué pasa si el token expiró?", "¿Cómo evitás que un PF vea los atletas de otro?", "¿Por qué guardás el id y no el objeto completo?".

## Dudas para revisar
Lo que no quedó claro, lo que podría faltar o mejorarse, y lo que conviene preguntarle al equipo. Si no hay, poné "Ninguna".
```

## Reglas

- Todo en **español**, claro y directo. Los nombres de clases, métodos y archivos van como están en el código (en inglés), con formato `código`.
- Basate solo en lo que hay en el código. No exageres lo que hace ni agregues cosas que no están.
- No modifiques ningún archivo del proyecto salvo el de `notas/`.
- Al terminar, decí dónde quedó el archivo y resumí en 3 líneas lo que documentaste.
