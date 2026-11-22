#!/usr/bin/env bash
# Datos de prueba de las demos (KAN-524, Demo 1; KAN-561, Demo 2). Solo para desarrollo local.
#
# Carga, con la API real y el token del PF de prueba, lo que la demo necesita ya armado:
# 20 atletas en 3 lugares (Club Quilmes, Personalizados y Gimnasio), con cuentas vinculadas o
# registradas por invitación (incluidos 2 menores con su tutor), récords, la planificación
# "Fuerza base" por bloques (4 semanas, lunes, martes, jueves y viernes a las 18; sin asignar), videos de
# YouTube del PF, el partido del sábado, las fases de la temporada, avisos en el Centro de Avisos, el perfil
# físico y la ficha médica para alimentación, y los check-ins que no se muestran en vivo.
#
# Planificación por bloques (KAN-585/587/588): cada día tiene Entrada en calor, Activación, Fuerza principal
# y Accesorios. El principal progresa 4×6 al 70 % → 4×5 al 75 % → 4×4 al 80 % → descarga 3×5 al 65 %.
# Los accesorios van con RPE o RIR y nadie tiene récord: muestran la "serie de referencia". Con "completo"
# se asigna desde el lunes de la semana pasada, así esta semana es la 2.
#
# Alimentación (KAN-494/634): Martín (78 kg, ganar masa, ficha sin patologías) recibe cantidades;
# Joaquín (asma en la ficha) se deriva a un profesional; Santino (menor, el tutor habilita) no recibe gramos.
#
# Los 20 atletas (KAN-479) y su semáforo, con "completo":
#   Club Quilmes · Primera División (10):
#     🔴 Lucas (lesión activa, isquiotibial) · Sofía (muy poco sueño: 3,5 h)
#     🟡 Joaquín (sueño moderado: 6 h) · Bruno (más de 12 h en 3 de los últimos 7 días)
#        Diego (dolor en cuádriceps → "Ver ajuste sugerido") · Mateo (esfuerzo alto: viene muy cargado)
#     🟢 Martín · Facundo · Ramiro      ⚪ Nicolás (sin cuenta)
#     Mateo es el compañero de Lucas sin variante (KAN-469: Lucas ve prensa y Mateo, sentadilla).
#   Club Quilmes · Reserva (5): Santino (16) y Bautista (17), menores con tutor: el tutor de Santino
#     habilita la alimentación y el de Bautista no (KAN-496/634); el WhatsApp va al tutor (KAN-503).
#     🟢 Santino · Bautista · Lautaro      ⚪ Agustina y Thiago (sin cuenta)
#   Personalizados · Alumnos particulares (3): 🔴 Camila (lesión activa, hombro) · 🟡 Valentina (dolor
#     nuevo, cuádriceps) · 🟢 Florencia
#   Gimnasio · Musculación (3): Camila (está en las dos) · 🟢 Paula · 🟡 Ignacio (sueño moderado: 6 h)
#
# Además deja listo: el WhatsApp del PF (botón "Contactar a mi preparador"), las lesiones de Lucas y Camila,
# una sesión de tests de campo en curso ("Evaluación de pretemporada") y, con "completo", una propuesta de
# récord de Martín para aprobar en el inicio del PF.
# Para la evolución: 3 récords anteriores por atleta (Martín, Facundo, Joaquín, Lucas y Diego) y 3 evaluaciones de
# campo cerradas con salto CMJ, 30 m y Cooper (las fechas de esas marcas se ajustan en la base de Seguimiento).
#
# Uso recomendado (igual en Windows, Mac y Linux; solo necesita Docker, con el compose levantado):
#   docker compose --profile seed run --rm seed             carga todo lo de abajo (una vez, con la base de cero)
#   docker compose --profile seed run --rm seed checkins    solo los check-ins de hoy (la mañana del ensayo y de la demo)
#   docker compose --profile seed run --rm seed completo    lo mismo que sin argumentos, más lo que en la demo se hace
#                                                           en vivo: récord de Lucas, asignación del plan y check-ins de Lucas y Martín
#                                                           (para el QA, usar este: sin asignación no hay rutina ni "Próximo entrenamiento")
#
# También se puede correr directo desde la raíz del repo (necesita bash, docker, curl y jq):
#   ./scripts/demo-data.sh [checkins|completo]
#
# Las cuentas las crea el seed de Identidad (IdentityDataSeed), con ASPNETCORE_ENVIRONMENT=Development.
set -euo pipefail

GATEWAY="${GATEWAY:-http://localhost:8000}"
PASSWORD="ProTraining2026!"
MODE="${1:-base}"

cd "$(dirname "$0")/.."

case "$MODE" in
  base | checkins | completo) ;;
  *)
    echo "Uso: $0 [checkins|completo]" >&2
    exit 1
    ;;
esac

# Dentro del contenedor "seed" (SEED_IN_CONTAINER=1) las bases se tocan con psql por la red interna;
# desde la compu, con "docker compose exec".
if [[ -n "${SEED_IN_CONTAINER:-}" ]]; then
  required_tools=(curl jq psql)
else
  required_tools=(docker curl jq)
fi
for tool in "${required_tools[@]}"; do
  command -v "$tool" >/dev/null || { echo "Falta '$tool' en el PATH." >&2; exit 1; }
done

# --- helpers ---------------------------------------------------------------

# run_sql <servicio-de-base> <sql> → corre el SQL en la base de ese servicio (db-estructura → base "estructura")
run_sql() {
  local service="$1" sql="$2"
  if [[ -n "${SEED_IN_CONTAINER:-}" ]]; then
    PGPASSWORD="$POSTGRES_PASSWORD" psql -q -h "$service" -U "$POSTGRES_USER" -d "${service#db-}" \
      -v ON_ERROR_STOP=1 -c "$sql"
  else
    # El usuario y la base los toma el propio contenedor de la base: no hace falta exportarlos en la compu.
    docker compose exec -T "$service" sh -c 'psql -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -c "$1"' \
      sh "$sql"
  fi
}

# login <email> → imprime la respuesta completa del login. Reintenta unos segundos: recién levantado el compose,
# Identidad puede estar terminando de crear las cuentas de prueba y responder 500 al primer pedido.
login() {
  local attempt
  for attempt in 1 2 3 4 5 6; do
    curl -sf -X POST "$GATEWAY/identidad/api/v1/auth/login" \
      -H 'Content-Type: application/json' \
      -d "{\"email\":\"$1\",\"password\":\"$PASSWORD\",\"deviceId\":\"demo-data\"}" && return 0
    sleep 5
  done
  echo "No se pudo iniciar sesión con $1. ¿Está levantado el compose?" >&2
  exit 1
}

# api <método> <ruta> <token> [json] → imprime el cuerpo; corta el script si la respuesta no es 2xx
api() {
  local method="$1" path="$2" token="$3" body="${4:-}"
  local response status
  response=$(curl -s -w '\n%{http_code}' -X "$method" "$GATEWAY$path" \
    -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
    ${body:+-d "$body"})
  status="${response##*$'\n'}"
  response="${response%$'\n'*}"
  if [[ "$status" != 2* ]]; then
    echo "Error $status en $method $path: $response" >&2
    exit 1
  fi
  printf '%s' "$response"
}

# Lunes de la semana pasada, en hora Argentina: con la asignación desde ahí, esta semana es la 2 del plan
monday_last_week() {
  jq -nr '(now - 10800) as $t
    | ($t | strftime("%u") | tonumber) as $dow
    | ($t - ($dow - 1 + 7) * 86400) | strftime("%Y-%m-%d")'
}

# date_plus <AAAA-MM-DD> <días> → la fecha corrida esa cantidad de días (puede ser negativa)
date_plus() {
  jq -nr --arg d "$1" --argjson n "$2" '($d + "T12:00:00Z" | fromdateiso8601) + $n * 86400 | strftime("%Y-%m-%d")'
}

# exercise_id <nombre exacto> → id del ejercicio en el catálogo
exercise_id() {
  local id
  id=$(api GET "/entrenamiento/api/v1/exercises?search=$(jq -rn --arg q "$1" '$q|@uri')&pageSize=50" "$PF_TOKEN" \
    | jq -r --arg name "$1" '(if type == "array" then . else .items end)[] | select(.name == $name) | .id' | head -1)
  [[ -n "$id" ]] || { echo "No encontré el ejercicio \"$1\" en el catálogo." >&2; exit 1; }
  printf '%s' "$id"
}

# athlete_id_of <email> → id del atleta en Estructura (con el token del propio atleta)
athlete_id_of() {
  local token
  token=$(login "$1" | jq -r .accessToken)
  api GET "/estructura/api/v1/me" "$token" | jq -r .athleteId
}

# active_injury_of <email> → id de su lesión activa. Desde la KAN-487 el check-in tiene que puntuar
# el dolor de cada lesión activa; en el modo "checkins" las lesiones ya existen y hay que buscarlas.
active_injury_of() {
  api GET "/seguimiento/api/v1/athletes/$(athlete_id_of "$1")/injuries?status=Active" "$PF_TOKEN" \
    | jq -r '.[0].id // empty'
}

# check_in <email> <json> [días atrás] → check-in con el token del atleta (si ya hizo uno ese día, lo pisa).
# Sin días atrás es el de hoy; con días atrás manda la fecha, como el front al sincronizar sin conexión.
check_in() {
  local token athlete_id body="$2" days_ago="${3:-0}" label="hoy"
  token=$(login "$1" | jq -r .accessToken)
  athlete_id=$(api GET "/estructura/api/v1/me" "$token" | jq -r .athleteId)
  if [[ "$days_ago" != 0 ]]; then
    body=$(jq -c --arg d "$(day_ago "$days_ago")" '. + {date: $d}' <<<"$body")
    label="hace $days_ago días"
  fi
  api POST "/seguimiento/api/v1/athletes/$athlete_id/check-in" "$token" "$body" \
    | jq -r --arg who "$1" --arg when "$label" '"  check-in de \($who) (\($when)): \(.trafficLight) (\(.trafficLightReason // "-"))"'
}

# day_ago <n> → fecha de hace n días, en hora Argentina (UTC-3)
day_ago() {
  jq -nr --argjson n "$1" '(now - 10800 - $n * 86400) | strftime("%Y-%m-%d")'
}

# next_saturday → el próximo sábado (o el de hoy, si es sábado), en hora Argentina
next_saturday() {
  jq -nr '(now - 10800) as $t
    | ($t | strftime("%u") | tonumber) as $dow
    | ($t + ((6 - $dow + 7) % 7) * 86400) | strftime("%Y-%m-%d")'
}

# invite_athlete <nombre> <apellido> <email> <teléfono> <categoría> → registra la cuenta por invitación
# (KAN-397/398/401) como lo hace la landing del QR, e imprime el id del atleta
invite_athlete() {
  local first="$1" last="$2" email="$3" phone="$4" category="$5" token session
  token=$(api POST "/estructura/api/v1/categories/$category/invitation-token" "$PF_TOKEN" | jq -r .token)
  session=$(curl -sf -X POST "$GATEWAY/identidad/api/v1/auth/register-by-invitation" \
    -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg e "$email" --arg p "$PASSWORD" --arg n "$first $last" --arg t "$token" \
      '{email:$e,password:$p,fullName:$n,token:$t,deviceId:"demo-data"}')" | jq -r .accessToken) \
    || { echo "No se pudo registrar a $email por invitación." >&2; exit 1; }
  api POST "/estructura/api/v1/invitations/$token/accept" "$session" \
    "$(jq -nc --arg f "$first" --arg l "$last" --arg p "$phone" '{firstName:$f,lastName:$l,phone:$p,birthDate:"2000-03-15"}')" \
    | jq -r .id
}

# invite_minor <nombre> <apellido> <email> <teléfono> <categoría> <nacimiento> <tutor> <teléfono del tutor> <habilita alimentación: true|false>
# → registra a un menor por invitación, con el consentimiento de su tutor (KAN-401), e imprime el id del atleta
invite_minor() {
  local first="$1" last="$2" email="$3" phone="$4" category="$5" birth="$6" guardian="$7" guardian_phone="$8" habits="$9"
  local token session
  token=$(api POST "/estructura/api/v1/categories/$category/invitation-token" "$PF_TOKEN" | jq -r .token)
  session=$(curl -sf -X POST "$GATEWAY/identidad/api/v1/auth/register-by-invitation" \
    -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg e "$email" --arg p "$PASSWORD" --arg n "$first $last" --arg t "$token" \
      '{email:$e,password:$p,fullName:$n,token:$t,deviceId:"demo-data"}')" | jq -r .accessToken) \
    || { echo "No se pudo registrar a $email por invitación." >&2; exit 1; }
  api POST "/estructura/api/v1/invitations/$token/accept" "$session" \
    "$(jq -nc --arg f "$first" --arg l "$last" --arg p "$phone" --arg b "$birth" --arg g "$guardian" \
      --arg gp "$guardian_phone" --argjson h "$habits" \
      '{firstName:$f,lastName:$l,phone:$p,birthDate:$b,guardianName:$g,guardianPhone:$gp,
        allowsPhysicalTraining:true,allowsHabitsRecommendations:$h}')" \
    | jq -r .id
}

PF_TOKEN=$(login pf@protraining.com | jq -r .accessToken)

# Recién levantado el compose, los servicios figuran sanos antes de terminar de arrancar (migraciones y catálogo
# de ejercicios) y el primer pedido puede fallar. Se espera a que cada uno responda de verdad, hasta 2 minutos.
wait_until_ready() {
  local path waited
  for path in /estructura/api/v1/organizations /entrenamiento/api/v1/exercises?pageSize=1 \
    /seguimiento/api/v1/fitness-tests /asistente/api/v1/alerts; do
    waited=0
    until curl -sf -o /dev/null "$GATEWAY$path" -H "Authorization: Bearer $PF_TOKEN"; do
      if (( waited >= 120 )); then
        echo "El servicio de $path no respondió en 2 minutos. Revisá: docker compose ps" >&2
        exit 1
      fi
      sleep 5
      waited=$((waited + 5))
    done
  done
}
wait_until_ready

# --- check-ins de hoy (los que no se hacen en vivo) ------------------------

load_check_ins() {
  echo "Check-ins:"
  # Joaquín durmió 6 h → amarillo "Sueño moderado" (de 4 a menos de 8 h, KAN-560)
  check_in joaquin@protraining.com '{"sleepHours":6,"sleepQuality":3,"muscleSoreness":2,"fatigueLevel":3,"moodRate":3,"stressLevel":3,"effortRate":6,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Facundo, todo bien → verde
  check_in facundo@protraining.com '{"sleepHours":8.5,"sleepQuality":4,"muscleSoreness":1,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Valentina con dolor nuevo en el cuádriceps → amarillo
  check_in valentina@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":3,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":6,"isSick":false,"painZones":"cuadriceps:3","hasActiveInjury":false}'
  # Camila con lesión activa en el hombro → rojo (está en Alumnos particulares y en Musculación)
  CAMILA_INJURY_ID="${CAMILA_INJURY_ID:-$(active_injury_of camila@protraining.com)}"
  if [[ -n "${CAMILA_INJURY_ID:-}" ]]; then
    camila_body=$(jq -nc --arg inj "$CAMILA_INJURY_ID" '{sleepHours:8,sleepQuality:4,muscleSoreness:3,fatigueLevel:2,moodRate:3,stressLevel:2,effortRate:5,isSick:false,painZones:"hombros:4",hasActiveInjury:true,injuries:[{injuryId:$inj,level:4}]}')
  else
    camila_body='{"sleepHours":8,"sleepQuality":4,"muscleSoreness":3,"fatigueLevel":2,"moodRate":3,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":"hombros:4","hasActiveInjury":true}'
  fi
  check_in camila@protraining.com "$camila_body"
  # Sofía durmió 3,5 h → rojo "Muy poco sueño" (menos de 4 h, KAN-560)
  check_in sofia@protraining.com '{"sleepHours":3.5,"sleepQuality":1,"muscleSoreness":2,"fatigueLevel":4,"moodRate":2,"stressLevel":3,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Bruno durmió más de 12 h hace 4 días, hace 2 días y hoy → amarillo "Durmió más de 12 horas en 3 de los últimos 7 días…"
  local long_sleep='{"sleepHours":13,"sleepQuality":3,"muscleSoreness":1,"fatigueLevel":3,"moodRate":3,"stressLevel":2,"effortRate":4,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  check_in bruno@protraining.com "$long_sleep" 4
  check_in bruno@protraining.com "$long_sleep" 2
  check_in bruno@protraining.com "$long_sleep"
  # Diego con dolor en el cuádriceps y sentadilla en la rutina de hoy → amarillo y "Ver ajuste sugerido" (KAN-451/452)
  check_in diego@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":3,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":"cuadriceps:3","hasActiveInjury":false}'

  # Los que se sumaron para llegar a 20 atletas (KAN-479)
  local ok='{"sleepHours":8,"sleepQuality":4,"muscleSoreness":1,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  local moderate_sleep='{"sleepHours":6,"sleepQuality":3,"muscleSoreness":2,"fatigueLevel":3,"moodRate":3,"stressLevel":3,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Mateo viene muy cargado (esfuerzo 9 de 10) → amarillo "Esfuerzo alto"
  check_in mateo@protraining.com '{"sleepHours":8,"sleepQuality":3,"muscleSoreness":4,"fatigueLevel":4,"moodRate":3,"stressLevel":3,"effortRate":9,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  check_in ramiro@protraining.com "$ok"
  check_in santino@protraining.com "$ok"
  check_in bautista@protraining.com "$ok"
  check_in lautaro@protraining.com "$ok"
  check_in florencia@protraining.com "$ok"
  check_in paula@protraining.com "$ok"
  # Ignacio durmió 6 h → amarillo "Sueño moderado"
  check_in ignacio@protraining.com "$moderate_sleep"
}

load_live_parts() {
  echo "Lo que en la demo se hace en vivo:"
  local lucas_id squat_id plan_id start
  lucas_id=$(athlete_id_of atleta@protraining.com)
  squat_id=$(exercise_id "Sentadilla con barra")
  api POST "/entrenamiento/api/v1/athletes/$lucas_id/records" "$PF_TOKEN" \
    "{\"exerciseId\":\"$squat_id\",\"weightOneRepMaxKg\":100,\"origin\":\"TestedInSession\"}" >/dev/null
  echo "  récord de Lucas: sentadilla 100 kg"

  plan_id=$(api GET "/entrenamiento/api/v1/training-plans" "$PF_TOKEN" \
    | jq -r '(if type == "array" then . else .items end)[] | select(.name == "Fuerza base") | .id' | head -1)
  # Desde el lunes de la semana pasada: esta semana es la 2 y se ve que los valores cambian de semana a semana.
  start=$(monday_last_week)
  api POST "/entrenamiento/api/v1/training-plans/assignments" "$PF_TOKEN" \
    "{\"trainingPlanId\":\"$plan_id\",\"startDate\":\"$start\",\"categoryId\":\"$PRIMERA_ID\"}" >/dev/null
  echo "  plan \"Fuerza base\" asignado a Primera División desde el $start (esta semana es la 2)"

  # Martín registra 115 kg × 5 en sentadilla: Epley estima ~132,5 kg > su récord de 120 kg, así que queda una
  # propuesta de récord pendiente en el inicio del PF (KAN-392/394). Hay sentadilla lunes, jueves y viernes.
  local martin_token
  martin_token=$(login martin@protraining.com | jq -r .accessToken)
  if curl -sf -X POST "$GATEWAY/entrenamiento/api/v1/me/today-routine/performed-sets" \
    -H "Authorization: Bearer $martin_token" -H 'Content-Type: application/json' \
    -d "[{\"exerciseId\":\"$squat_id\",\"weight\":115,\"reps\":5}]" >/dev/null; then
    echo "  Martín registró sentadilla 115 kg × 5 → propuesta de récord para el PF"
  else
    echo "  (hoy no hay sentadilla en el plan: la propuesta de récord de Martín se genera lunes, jueves y viernes)" >&2
  fi

  # Lucas con lesión activa en el isquiotibial → rojo (la zona se ve pintada en la ficha del PF); Martín bien → verde
  if [[ -n "${LUCAS_INJURY_ID:-}" ]]; then
    lucas_body=$(jq -nc --arg inj "$LUCAS_INJURY_ID" '{sleepHours:8,sleepQuality:3,muscleSoreness:3,fatigueLevel:3,moodRate:3,stressLevel:2,effortRate:6,isSick:false,painZones:"isquiotibiales:4",hasActiveInjury:true,injuries:[{injuryId:$inj,level:4}]}')
  else
    lucas_body='{"sleepHours":8,"sleepQuality":3,"muscleSoreness":3,"fatigueLevel":3,"moodRate":3,"stressLevel":2,"effortRate":6,"isSick":false,"painZones":"isquiotibiales:4","hasActiveInjury":true}'
  fi
  check_in atleta@protraining.com "$lucas_body"
  check_in martin@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":1,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
}

# Avisos del Centro de Avisos (KAN-474/570): los que genera el proceso de la noche, ya cargados para que el
# cajón del PF no arranque vacío. Uno de cada tipo y de cada gravedad, coherentes con el check-in de cada atleta.
load_alerts() {
  echo "Avisos preventivos (Centro de Avisos):"
  alert() {  # alert <atleta> <nombre> <categoría> <gravedad> <tipo> <motivo> <sugerencia> [ajuste] [porcentaje]
    api POST "/asistente/api/v1/alerts" "$PF_TOKEN" \
      "$(jq -nc --arg a "$1" --arg n "$2" --arg c "$3" --arg s "$4" --arg t "$5" --arg r "$6" --arg g "$7" \
        --arg adj "${8:-}" --arg pct "${9:-}" \
        '{athleteId:$a,athleteName:$n,categoryId:$c,severity:$s,type:$t,reason:$r,suggestion:$g}
          + (if $adj != "" then {suggestedAdjustmentType:$adj} else {} end)
          + (if $pct != "" then {suggestedAdjustmentPercentage:($pct|tonumber)} else {} end)')" >/dev/null
  }
  alert "$LUCAS_ID" "Lucas Gómez" "$PRIMERA_ID" High AccumulatedFatigue \
    "Fatiga acumulada en la semana y partido el sábado" \
    "Bajar 20 % las series hasta el partido" LoadReduction 20
  alert "$MATEO_ID" "Mateo Ríos" "$PRIMERA_ID" High AcuteOverload \
    "Carga aguda alta: esfuerzo 9 de 10 y la carga de la semana muy por encima de su promedio" \
    "Bajar 30 % el volumen de hoy" LoadReduction 30
  alert "$CAMILA_ID" "Camila Torres" "$PARTICULARES_ID" Medium InjuryRisk \
    "Sigue con molestia en el hombro (manguito rotador) en los últimos check-ins" \
    "Cambiar el press por ejercicios sin carga por encima de la cabeza" RoutineAdjustment
  alert "$SOFIA_ID" "Sofía Méndez" "$PRIMERA_ID" Medium WellnessAlert \
    "Durmió menos de 4 horas" \
    "Hoy, descanso activo y hablar con la atleta" Rest
  alert "$BRUNO_ID" "Bruno Castro" "$PRIMERA_ID" Low WellnessAlert \
    "Durmió más de 12 horas en 3 de los últimos 7 días" \
    "Preguntarle si está enfermo o con sobrecarga antes de entrenar"
  alert "$IGNACIO_ID" "Ignacio Luna" "$MUSCULACION_ID" Low AccumulatedFatigue \
    "Sueño moderado varios días seguidos" \
    "Bajar 10 % la carga esta semana" LoadReduction 10
  echo "  6 pendientes: Lucas y Mateo (alta), Camila y Sofía (media), Bruno e Ignacio (baja)"
}

if [[ "$MODE" == "checkins" ]]; then
  load_check_ins
  echo "Listo."
  exit 0
fi

# --- estructura ------------------------------------------------------------

if api GET "/estructura/api/v1/organizations" "$PF_TOKEN" | jq -e '.[] | select(.name == "Club Quilmes")' >/dev/null; then
  echo "Ya hay datos de la demo cargados (existe \"Club Quilmes\")." >&2
  echo "Para empezar de cero: docker compose down -v && docker compose up -d --build" >&2
  echo "y después: docker compose --profile seed run --rm seed" >&2
  echo "Para cargar solo los check-ins de hoy: $0 checkins" >&2
  exit 1
fi

echo "Organizaciones y categorías:"
new_org() { api POST "/estructura/api/v1/organizations" "$PF_TOKEN" "$(jq -nc --arg n "$1" --arg d "$2" '{name:$n,description:$d}')" | jq -r .id; }
new_category() { api POST "/estructura/api/v1/categories" "$PF_TOKEN" "$(jq -nc --arg o "$1" --arg n "$2" '{organizationId:$o,name:$n}')" | jq -r .id; }

CLUB_ID=$(new_org "Club Quilmes" "Club de fútbol")
PERSONAL_ID=$(new_org "Personalizados" "Alumnos particulares")
GYM_ID=$(new_org "Gimnasio" "Gimnasio del barrio")
PRIMERA_ID=$(new_category "$CLUB_ID" "Primera División")
RESERVA_ID=$(new_category "$CLUB_ID" "Reserva")
PARTICULARES_ID=$(new_category "$PERSONAL_ID" "Alumnos particulares")
MUSCULACION_ID=$(new_category "$GYM_ID" "Musculación")
echo "  Club Quilmes (Primera División, Reserva), Personalizados, Gimnasio"

# new_athlete <nombre> <apellido> <email> <teléfono> <categoría…> → imprime el id
new_athlete() {
  local first="$1" last="$2" email="$3" phone="$4"
  shift 4
  local id
  id=$(api POST "/estructura/api/v1/athletes" "$PF_TOKEN" \
    "$(jq -nc --arg f "$first" --arg l "$last" --arg e "$email" --arg p "$phone" \
      '{firstName:$f,lastName:$l,email:$e,phone:$p,isMinor:false}')" | jq -r .id)
  for category in "$@"; do
    api POST "/estructura/api/v1/athletes/$id/categories/$category" "$PF_TOKEN" >/dev/null
  done
  printf '%s' "$id"
}

echo "Atletas:"
LUCAS_ID=$(new_athlete "Lucas" "Gómez" "atleta@protraining.com" "1150000001" "$PRIMERA_ID")
MARTIN_ID=$(new_athlete "Martín" "Pérez" "martin@protraining.com" "1150000002" "$PRIMERA_ID")
JOAQUIN_ID=$(new_athlete "Joaquín" "Díaz" "joaquin@protraining.com" "1150000003" "$PRIMERA_ID")
FACUNDO_ID=$(new_athlete "Facundo" "Sosa" "facundo@protraining.com" "1150000004" "$PRIMERA_ID")
new_athlete "Nicolás" "Herrera" "nicolas@protraining.com" "1150000005" "$PRIMERA_ID" >/dev/null
new_athlete "Agustina" "Morales" "agustina@protraining.com" "1150000006" "$RESERVA_ID" >/dev/null
VALENTINA_ID=$(new_athlete "Valentina" "Ruiz" "valentina@protraining.com" "1150000007" "$PARTICULARES_ID")
CAMILA_ID=$(new_athlete "Camila" "Torres" "camila@protraining.com" "1150000008" "$PARTICULARES_ID" "$MUSCULACION_ID")
new_athlete "Thiago" "Benítez" "thiago@protraining.com" "1150000012" "$RESERVA_ID" >/dev/null
echo "  9 atletas (Nicolás, Agustina y Thiago sin cuenta: en el semáforo quedan sin check-in)"

# Sofía, Bruno y Diego se registran solos por la invitación de Primera División (KAN-397/398),
# como lo haría un atleta que escanea el QR: no hace falta vincular su cuenta a mano.
echo "Atletas registrados por invitación:"
SOFIA_ID=$(invite_athlete "Sofía" "Méndez" "sofia@protraining.com" "1150000009" "$PRIMERA_ID")
BRUNO_ID=$(invite_athlete "Bruno" "Castro" "bruno@protraining.com" "1150000010" "$PRIMERA_ID")
DIEGO_ID=$(invite_athlete "Diego" "Funes" "diego@protraining.com" "1150000011" "$PRIMERA_ID")
MATEO_ID=$(invite_athlete "Mateo" "Ríos" "mateo@protraining.com" "1150000013" "$PRIMERA_ID")
invite_athlete "Ramiro" "Acosta" "ramiro@protraining.com" "1150000014" "$PRIMERA_ID" >/dev/null
echo "  Sofía, Bruno, Diego, Mateo y Ramiro en Primera División"
invite_athlete "Lautaro" "Medina" "lautaro@protraining.com" "1150000015" "$RESERVA_ID" >/dev/null
# Menores: el tutor de Santino habilita las recomendaciones de alimentación y el de Bautista no
SANTINO_ID=$(invite_minor "Santino" "Vega" "santino@protraining.com" "1150000016" "$RESERVA_ID" \
  "2010-05-10" "Claudia Vega" "1150000116" true)
invite_minor "Bautista" "Ojeda" "bautista@protraining.com" "1150000017" "$RESERVA_ID" \
  "2009-08-20" "Marcelo Ojeda" "1150000117" false >/dev/null
echo "  Lautaro, Santino (16) y Bautista (17) en Reserva; los menores, con el consentimiento de su tutor"
invite_athlete "Florencia" "Navarro" "florencia@protraining.com" "1150000018" "$PARTICULARES_ID" >/dev/null
echo "  Florencia en Alumnos particulares"
invite_athlete "Paula" "Romero" "paula@protraining.com" "1150000019" "$MUSCULACION_ID" >/dev/null
IGNACIO_ID=$(invite_athlete "Ignacio" "Luna" "ignacio@protraining.com" "1150000020" "$MUSCULACION_ID")
echo "  Paula e Ignacio en Musculación (20 atletas en total)"

# Vincular cada atleta a su cuenta de Identidad. Lo hace la invitación por QR (Sprint 2);
# mientras tanto, directo en la base de Estructura (solo desarrollo).
echo "Vínculo de las cuentas:"
link_sql=""
for pair in "atleta@protraining.com:$LUCAS_ID" "martin@protraining.com:$MARTIN_ID" \
  "joaquin@protraining.com:$JOAQUIN_ID" "facundo@protraining.com:$FACUNDO_ID" \
  "valentina@protraining.com:$VALENTINA_ID" "camila@protraining.com:$CAMILA_ID"; do
  email="${pair%%:*}"
  athlete_id="${pair#*:}"
  account_id=$(login "$email" | jq -r .user.id)
  link_sql+="update athletes set account_id = '$account_id' where id = '$athlete_id';"
done
run_sql db-estructura "$link_sql"
echo "  6 cuentas vinculadas"

# --- récords y planificación -----------------------------------------------

SQUAT_ID=$(exercise_id "Sentadilla con barra")
BENCH_ID=$(exercise_id "Press de banca con barra - Agarre medio")
RDL_ID=$(exercise_id "Peso muerto rumano")

# Historia de récords (KAN-434/435): tres marcas viejas, cada 4 semanas, para que la evolución del 1RM muestre una
# curva real. Van por la importación (la única entrada que acepta fecha) y en orden cronológico: cada fila desactiva
# la anterior. Los récords de hoy (abajo) quedan como los vigentes, así que la confiabilidad y la propuesta de
# Martín no cambian.
echo "Historia de récords (hace 12, 8 y 4 semanas):"
history_rows=""
history() {  # history <atleta> <ejercicio> <origen> <kg hace 12 sem> <kg hace 8> <kg hace 4>
  local weeks=(84 56 28) i
  for i in 0 1 2; do
    history_rows+=$(jq -nc --arg a "$1" --arg e "$2" --arg o "$3" --argjson w "${@:4+i:1}" \
      --arg d "$(day_ago "${weeks[$i]}")T15:00:00Z" \
      '{athleteId:$a,exerciseId:$e,weightOneRepMaxKg:$w,achievedAt:$d,origin:$o}')$'\n'
  done
}
history "$MARTIN_ID" "$SQUAT_ID" TestedInSession 100 107.5 112.5
history "$MARTIN_ID" "$BENCH_ID" TestedInSession 75 77.5 82.5
history "$MARTIN_ID" "$RDL_ID" Reported 115 120 125
history "$FACUNDO_ID" "$SQUAT_ID" TestedInSession 125 130 135
history "$FACUNDO_ID" "$RDL_ID" TestedInSession 135 140 145
history "$JOAQUIN_ID" "$SQUAT_ID" Reported 82.5 87.5 90
history "$JOAQUIN_ID" "$BENCH_ID" Reported 57.5 60 62.5
history "$LUCAS_ID" "$BENCH_ID" Reported 60 62.5 67.5
history "$LUCAS_ID" "$RDL_ID" TestedInSession 95 100 105
history "$DIEGO_ID" "$SQUAT_ID" TestedInSession 90 95 100
# Se ordena por fecha para que, por atleta y ejercicio, la última fila importada sea la más nueva.
api POST "/entrenamiento/api/v1/records/import/confirm" "$PF_TOKEN" \
  "$(jq -sc 'sort_by(.achievedAt) | {rows: .}' <<<"$history_rows")" >/dev/null
echo "  Martín, Facundo, Joaquín, Lucas y Diego: 3 marcas cada uno, de menos a más"

echo "Récords (1RM):"
record() {
  api POST "/entrenamiento/api/v1/athletes/$1/records" "$PF_TOKEN" \
    "{\"exerciseId\":\"$2\",\"weightOneRepMaxKg\":$3,\"origin\":\"$4\"}" >/dev/null
}
# La sentadilla de Lucas no: se carga en vivo en la demo
record "$LUCAS_ID" "$BENCH_ID" 70 Reported
record "$LUCAS_ID" "$RDL_ID" 110 TestedInSession
record "$MARTIN_ID" "$SQUAT_ID" 120 TestedInSession
record "$MARTIN_ID" "$BENCH_ID" 85 TestedInSession
record "$MARTIN_ID" "$RDL_ID" 130 Reported
record "$JOAQUIN_ID" "$SQUAT_ID" 95 Reported
record "$JOAQUIN_ID" "$BENCH_ID" 65 Reported
record "$FACUNDO_ID" "$SQUAT_ID" 140 TestedInSession
record "$FACUNDO_ID" "$RDL_ID" 150 TestedInSession
record "$VALENTINA_ID" "$SQUAT_ID" 70 Reported
record "$CAMILA_ID" "$SQUAT_ID" 80 TestedInSession
record "$SOFIA_ID" "$SQUAT_ID" 75 Reported
record "$BRUNO_ID" "$SQUAT_ID" 110 TestedInSession
record "$DIEGO_ID" "$SQUAT_ID" 105 TestedInSession
record "$DIEGO_ID" "$BENCH_ID" 80 Reported
echo "  15 récords (falta a propósito la sentadilla de Lucas)"

# Planificación por bloques (KAN-585): 4 semanas, lunes, martes, jueves y viernes a las 18, así hay rutina en el
# QA (lunes y martes), en el ensayo (jueves) y en la demo (viernes). La sentadilla va lunes, jueves y viernes:
# de eso dependen el ajuste de Diego por el cuádriceps, la variante de Lucas y la propuesta de récord de Martín.
# DayOfWeek va como número (1 = lunes … 5 = viernes). Los ejercicios por tiempo llevan reps 1 y la meta en segundos.
# Progresión lineal del principal (+5 %/semana) con la 4.ª de descarga, dentro de la tabla de carga por objetivo de
# la NSCA (Essentials of Strength Training and Conditioning, 4.ª ed.) que ya usan las reglas de la KAN-605.
echo "Planificación:"
JOG_ID=$(exercise_id "Trote suave o bicicleta (5 min, entrada en calor)")
JACKS_ID=$(exercise_id "Jumping jacks (entrada en calor)")
PLANK_ID=$(exercise_id "Plancha (activación)")
LUNGE_ID=$(exercise_id "Zancada con barra")
ROW_ID=$(exercise_id "Remo con mancuerna a una mano")
OHP_ID=$(exercise_id "Press de hombros con barra")
PLAN_JSON=$(jq -nc \
  --arg squat "$SQUAT_ID" --arg bench "$BENCH_ID" --arg rdl "$RDL_ID" \
  --arg jog "$JOG_ID" --arg jacks "$JACKS_ID" --arg plank "$PLANK_ID" \
  --arg lunge "$LUNGE_ID" --arg row "$ROW_ID" --arg ohp "$OHP_ID" '
  # Valores de cada semana: el principal con %RM y los accesorios con RPE o RIR
  [ {n: 1, sets: 4, reps: "6", pct: 70, rpe: 7,   rir: 3, note: null},
    {n: 2, sets: 4, reps: "5", pct: 75, rpe: 7.5, rir: 2, note: null},
    {n: 3, sets: 4, reps: "4", pct: 80, rpe: 8,   rir: 2, note: null},
    {n: 4, sets: 3, reps: "5", pct: 65, rpe: 6,   rir: 4, note: "Semana de descarga: bajar la carga para recuperar."} ] as $weeks
  | def warmup: {title: "Entrada en calor", order: 1, exercises: [
        {exerciseId: $jog, order: 1, sets: 1, reps: "1", targetTimeSeconds: 300},
        {exerciseId: $jacks, order: 2, sets: 2, reps: "1", targetTimeSeconds: 60}]};
    def activation: {title: "Activación", order: 2, exercises: [
        {exerciseId: $plank, order: 1, sets: 3, reps: "1", targetTimeSeconds: 30}]};
    def main($w; $ids): {title: "Fuerza principal", order: 3, exercises: ($ids | to_entries | map(
        {exerciseId: .value, order: (.key + 1), sets: $w.sets, reps: $w.reps, targetPercentage: $w.pct,
         notes: ($w.note // "Pausa de 2 a 3 minutos entre series.")}))};
    def acc_rpe($w; $id; $order): {exerciseId: $id, order: $order, sets: 3, reps: "8-10",
        intensityType: "RPE", targetRpe: $w.rpe};
    def acc_rir($w; $id; $order): {exerciseId: $id, order: $order, sets: 3, reps: "10",
        intensityType: "RIR", targetRir: $w.rir};
    def session($w; $name; $order; $day; $main; $acc): {name: $name, order: $order, dayOfWeek: $day,
        startTime: "18:00:00", coachNotes: "Entrada en calor completa antes de cargar la barra.",
        blocks: [warmup, activation, main($w; $main), {title: "Accesorios", order: 4, exercises: $acc}]};
  {
    name: "Fuerza base",
    objective: "Base de fuerza para la pretemporada de Primera",
    weeks: ($weeks | map(. as $w | {
      weekNumber: $w.n,
      sessions: [
        session($w; "Tren inferior A"; 1; 1; [$squat]; [acc_rpe($w; $lunge; 1)]),
        session($w; "Tren superior A"; 2; 2; [$bench]; [acc_rir($w; $row; 1), acc_rpe($w; $ohp; 2)]),
        session($w; "Tren inferior B"; 3; 4; [$rdl, $squat]; [acc_rpe($w; $lunge; 1)]),
        session($w; "Cuerpo completo"; 4; 5; [$squat, $bench, $rdl]; [acc_rir($w; $row; 1)])
      ]}))
  }')
api POST "/entrenamiento/api/v1/training-plans" "$PF_TOKEN" "$PLAN_JSON" >/dev/null
echo "  \"Fuerza base\" por bloques: 4 semanas (70 → 75 → 80 % y descarga al 65 %), lunes, martes, jueves y viernes a las 18 (sin asignar)"

# Videos de técnica del PF (KAN-380): se pueden embeber (verificado con oEmbed el 11/11).
# El peso muerto rumano queda sin video a propósito: sin link, no aparece el botón "Ver video".
echo "Videos de YouTube del PF:"
video() {
  api PUT "/entrenamiento/api/v1/exercises/$1/video" "$PF_TOKEN" "$(jq -nc --arg u "$2" '{videoUrl:$u}')" >/dev/null
}
video "$SQUAT_ID" "https://www.youtube.com/watch?v=bEv6CCg2BC8"
video "$BENCH_ID" "https://www.youtube.com/watch?v=rT7DgCr-3pg"
echo "  sentadilla y press de banca (peso muerto rumano sin video)"

# Partido del sábado de Primera División (KAN-403/404): se ve en el calendario de la temporada.
echo "Calendario:"
MATCH_DATE=$(next_saturday)
api POST "/estructura/api/v1/categories/$PRIMERA_ID/events" "$PF_TOKEN" \
  "$(jq -nc --arg d "$MATCH_DATE" '{date:$d,startTime:"15:30:00",opponent:"Atlético Varela",venue:"Home",type:"Official",notes:"Fecha del torneo"}')" >/dev/null
echo "  Primera División vs. Atlético Varela, el $MATCH_DATE a las 15:30"

# Fases de la temporada (KAN-403): la pretemporada termina el viernes y el torneo arranca con el partido del sábado.
phase() {
  api POST "/estructura/api/v1/categories/$PRIMERA_ID/season-phases" "$PF_TOKEN" \
    "$(jq -nc --arg n "$1" --arg t "$2" --arg s "$3" --arg e "$4" '{name:$n,type:$t,startDate:$s,endDate:$e}')" >/dev/null
}
phase "Pretemporada" Preseason "$(date_plus "$MATCH_DATE" -42)" "$(date_plus "$MATCH_DATE" -1)"
phase "Torneo Apertura" Competitive "$MATCH_DATE" "$(date_plus "$MATCH_DATE" 120)"
echo "  fases de Primera: Pretemporada (6 semanas) y Torneo Apertura (desde el $MATCH_DATE)"

# Perfil físico y ficha médica para las recomendaciones de alimentación (KAN-494/634). Las cantidades solo salen
# si la ficha dice explícitamente que no hay condiciones; cualquier condición deriva a un profesional.
echo "Perfil físico y ficha médica (alimentación):"
physical_profile() {
  api PATCH "/estructura/api/v1/athletes/$1/physical-profile" "$PF_TOKEN" \
    "$(jq -nc --argjson w "$2" --arg g "$3" '{weightKg:$w,athleticGoal:$g}')" >/dev/null
}
medical_record() {  # medical_record <atleta> <antecedentes> <patologías>
  api PUT "/estructura/api/v1/athletes/$1/medical-record" "$PF_TOKEN" \
    "$(jq -nc --arg h "$2" --arg p "$3" --arg from "$(day_ago 60)" --arg to "$(day_ago -305)" \
      '{justification:"Carga inicial de la ficha (datos de la demo)",medicalHistory:$h,surgeries:null,pathologies:$p,
        hasPhysicalFitnessCertificate:true,physicalFitnessIssuedAt:$from,physicalFitnessExpiresAt:$to}')" >/dev/null
}
# Sin el consentimiento digital firmado no se puede cargar la ficha, y sin "hábitos" habilitado no hay
# recomendaciones de alimentación, ni siquiera para un adulto. Lo firma cada atleta desde su sesión.
sign_consent() {
  local token
  token=$(login "$1" | jq -r .accessToken)
  api POST "/estructura/api/v1/me/consent" "$token" '{"allowsHabitsRecommendations":true,"allowsPhysicalTraining":true}' >/dev/null
}
sign_consent martin@protraining.com
sign_consent facundo@protraining.com
sign_consent joaquin@protraining.com
physical_profile "$MARTIN_ID" 78 "Ganar masa muscular"
medical_record "$MARTIN_ID" "Sin alergias. Sin antecedentes." "Sin patologías."
physical_profile "$FACUNDO_ID" 82 "Rendimiento deportivo"
medical_record "$FACUNDO_ID" "Sin alergias. Sin antecedentes." "Sin patologías."
physical_profile "$JOAQUIN_ID" 70 "Ganar masa muscular"
medical_record "$JOAQUIN_ID" "Sin alergias." "Asma leve, controlada con inhalador."
physical_profile "$SANTINO_ID" 65 "Ganar masa muscular"
medical_record "$SANTINO_ID" "Sin alergias. Sin antecedentes." "Sin patologías."
echo "  Martín y Facundo (ficha sin condiciones: con cantidades), Joaquín (asma: se deriva), Santino (menor: sin gramos)"

# WhatsApp del PF (KAN-379/562): sin número, el atleta no ve el botón "Contactar a mi preparador".
# Número ficticio; para que abra un WhatsApp real: DEMO_PF_PHONE=+549… ./scripts/demo-data.sh
echo "WhatsApp del PF:"
api PUT "/identidad/api/v1/users/me/phone" "$PF_TOKEN" \
  "$(jq -nc --arg p "${DEMO_PF_PHONE:-+5491150000000}" '{phoneNumber:$p}')" >/dev/null
echo "  ${DEMO_PF_PHONE:-+5491150000000}"

# Lesiones con estado y fecha de retorno (KAN-419/421): se ven en el panel de lesiones de la ficha del atleta.
echo "Lesiones:"
injury() {
  api POST "/seguimiento/api/v1/athletes/$1/injuries" "$PF_TOKEN" \
    "$(jq -nc --arg z "$2" --arg d "$3" --arg from "$(day_ago 6)" --arg to "$(day_ago -10)" \
      '{zone:$z,description:$d,injuryDate:$from,estimatedReturnDate:$to}')"
}
LUCAS_INJURY_ID=$(injury "$LUCAS_ID" "isquiotibiales" "Distensión grado 1 en un sprint del entrenamiento." | jq -r .id)
CAMILA_INJURY_ID=$(injury "$CAMILA_ID" "hombros" "Sobrecarga del manguito rotador." | jq -r .id)
echo "  Lucas (isquiotibiales) y Camila (hombro), activas, con retorno estimado en 10 días"

# Sesión de tests de campo de hoy, ya en curso (KAN-425/427): la pantalla táctil arranca con marcas cargadas.
# La sentadilla 1RM queda sin cargar: en la demo se carga y se confirma en vivo (manda el 1RM a Entrenamiento).
echo "Tests de campo:"
test_id() {
  api GET "/seguimiento/api/v1/fitness-tests" "$PF_TOKEN" | jq -r --arg n "$1" '.[] | select(.name == $n) | .id' | head -1
}
SQUAT_TEST=$(test_id "Sentadilla 1RM")
CMJ_TEST=$(test_id "Salto Vertical CMJ")
SPRINT_TEST=$(test_id "Carrera 30 m")
COOPER_TEST=$(test_id "Test de Cooper (12 min)")
# Tres evaluaciones anteriores, ya cerradas (hace 10, 6 y 2 semanas), con salto CMJ, 30 m y Cooper: así la
# evolución (salto, velocidad y resistencia) muestra una curva. La API registra cada marca con la hora del momento, así que
# después se le pone la fecha de su sesión directo en la base de Seguimiento (solo datos de demo).
past_session() {  # past_session <nombre> <días atrás> → imprime el id de la sesión, en curso
  local id
  id=$(api POST "/seguimiento/api/v1/field-test-sessions" "$PF_TOKEN" \
    "$(jq -nc --arg c "$PRIMERA_ID" --arg d "$(day_ago "$2")" --arg n "$1" --arg j "$CMJ_TEST" --arg r "$SPRINT_TEST" --arg k "$COOPER_TEST" \
      '{categoryId:$c,date:$d,name:$n,tests:[{catalogTestId:$j,order:1},{catalogTestId:$r,order:2},{catalogTestId:$k,order:3}]}')" | jq -r .id)
  api PATCH "/seguimiento/api/v1/field-test-sessions/$id/status" "$PF_TOKEN" '{"status":"InProgress"}' >/dev/null
  echo "$id"
}
past_results() {  # past_results <sesión> <atleta> <CMJ en cm> <30 m en s> <Cooper en m>
  api POST "/seguimiento/api/v1/field-test-sessions/$1/results" "$PF_TOKEN" \
    "$(jq -nc --arg t "$CMJ_TEST" --arg a "$2" --argjson v "$3" '{catalogTestId:$t,athleteId:$a,value:$v,attempt:1}')" >/dev/null
  api POST "/seguimiento/api/v1/field-test-sessions/$1/results" "$PF_TOKEN" \
    "$(jq -nc --arg t "$SPRINT_TEST" --arg a "$2" --argjson v "$4" '{catalogTestId:$t,athleteId:$a,value:$v,attempt:1}')" >/dev/null
  api POST "/seguimiento/api/v1/field-test-sessions/$1/results" "$PF_TOKEN" \
    "$(jq -nc --arg t "$COOPER_TEST" --arg a "$2" --argjson v "$5" '{catalogTestId:$t,athleteId:$a,value:$v,attempt:1}')" >/dev/null
}
i=0
for s in "Evaluación inicial|70" "Control de mitad de bloque|42" "Control pre torneo|14"; do
  sid=$(past_session "${s%|*}" "${s#*|}")
  cmj_martin=(38 39.5 41) cmj_joaquin=(34 35.5 36.5) cmj_facundo=(41 42.5 44) cmj_lucas=(36 37.5 39) cmj_diego=(35 36 37.5)
  t30_martin=(4.52 4.45 4.38) t30_joaquin=(4.60 4.55 4.49) t30_facundo=(4.35 4.29 4.22) t30_lucas=(4.48 4.41 4.36) t30_diego=(4.55 4.50 4.44)
  cooper_martin=(2650 2720 2800) cooper_joaquin=(2500 2560 2610) cooper_facundo=(2780 2840 2900) cooper_lucas=(2600 2680 2750) cooper_diego=(2550 2600 2660)
  past_results "$sid" "$MARTIN_ID" "${cmj_martin[$i]}" "${t30_martin[$i]}" "${cooper_martin[$i]}"
  past_results "$sid" "$JOAQUIN_ID" "${cmj_joaquin[$i]}" "${t30_joaquin[$i]}" "${cooper_joaquin[$i]}"
  past_results "$sid" "$FACUNDO_ID" "${cmj_facundo[$i]}" "${t30_facundo[$i]}" "${cooper_facundo[$i]}"
  past_results "$sid" "$LUCAS_ID" "${cmj_lucas[$i]}" "${t30_lucas[$i]}" "${cooper_lucas[$i]}"
  past_results "$sid" "$DIEGO_ID" "${cmj_diego[$i]}" "${t30_diego[$i]}" "${cooper_diego[$i]}"
  api PATCH "/seguimiento/api/v1/field-test-sessions/$sid/status" "$PF_TOKEN" '{"status":"Closed"}' >/dev/null
  i=$((i + 1))
done
run_sql db-seguimiento "UPDATE field_test_results r SET recorded_at = (s.date + time '15:00') AT TIME ZONE 'UTC'
  FROM field_test_sessions s WHERE r.session_id = s.id AND s.date < CURRENT_DATE" >/dev/null
echo "  3 evaluaciones cerradas (hace 10, 6 y 2 semanas): salto CMJ, 30 m y Cooper de Martín, Joaquín, Facundo, Lucas y Diego"

FIELD_SESSION=$(api POST "/seguimiento/api/v1/field-test-sessions" "$PF_TOKEN" \
  "$(jq -nc --arg c "$PRIMERA_ID" --arg d "$(day_ago 0)" --arg s "$SQUAT_TEST" --arg j "$CMJ_TEST" --arg r "$SPRINT_TEST" \
    '{categoryId:$c,date:$d,name:"Evaluación de pretemporada",tests:[{catalogTestId:$s,order:1},{catalogTestId:$j,order:2},{catalogTestId:$r,order:3}]}')" \
  | jq -r .id)
api PATCH "/seguimiento/api/v1/field-test-sessions/$FIELD_SESSION/status" "$PF_TOKEN" '{"status":"InProgress"}' >/dev/null
field_result() {
  api POST "/seguimiento/api/v1/field-test-sessions/$FIELD_SESSION/results" "$PF_TOKEN" \
    "$(jq -nc --arg t "$1" --arg a "$2" --argjson v "$3" '{catalogTestId:$t,athleteId:$a,value:$v,attempt:1}')" >/dev/null
}
field_result "$CMJ_TEST" "$MARTIN_ID" 42.5
field_result "$CMJ_TEST" "$JOAQUIN_ID" 38
field_result "$CMJ_TEST" "$FACUNDO_ID" 45
field_result "$SPRINT_TEST" "$MARTIN_ID" 4.31
field_result "$SPRINT_TEST" "$FACUNDO_ID" 4.18
echo "  \"Evaluación de pretemporada\" (Primera División), en curso: salto CMJ y 30 m cargados, sentadilla 1RM para cargar en vivo"

load_alerts

load_check_ins

if [[ "$MODE" == "completo" ]]; then
  load_live_parts
fi

echo "Listo. PF: pf@protraining.com · atletas: atleta@ (Lucas), martin@, joaquin@, facundo@, valentina@, camila@, sofia@, bruno@, diego@,"
echo "  mateo@, ramiro@, lautaro@, santino@ y bautista@ (menores), florencia@, paula@, ignacio@protraining.com · contraseña: $PASSWORD"
