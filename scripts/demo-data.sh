#!/usr/bin/env bash
# Datos de prueba de la Demo 1 (KAN-524). Solo para desarrollo local.
#
# Carga, con la API real y el token del PF de prueba, lo que la demo necesita ya armado:
# organizaciones, categorías, atletas vinculados a sus cuentas, récords, la planificación
# "Fuerza base" (sin asignar) y los check-ins de hoy de los atletas que no se muestran en vivo.
#
# Uso (desde la raíz del repo, con el compose levantado):
#   ./scripts/demo-data.sh            carga todo lo de arriba (una vez, con la base de cero)
#   ./scripts/demo-data.sh checkins   solo los check-ins de hoy (correrlo la mañana del ensayo y de la demo)
#   ./scripts/demo-data.sh completo   lo mismo que sin argumentos, más lo que en la demo se hace en vivo:
#                                     récord de sentadilla de Lucas, asignación del plan y check-ins de Lucas y Martín
#
# Necesita: docker, curl y jq. Las cuentas las crea el seed de Identidad (IdentityDataSeed).
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

for tool in docker curl jq; do
  command -v "$tool" >/dev/null || { echo "Falta '$tool' en el PATH." >&2; exit 1; }
done

# --- helpers ---------------------------------------------------------------

# login <email> → imprime la respuesta completa del login
login() {
  curl -sf -X POST "$GATEWAY/identidad/api/v1/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$1\",\"password\":\"$PASSWORD\",\"deviceId\":\"demo-data\"}" \
    || { echo "No se pudo iniciar sesión con $1. ¿Está levantado el compose?" >&2; exit 1; }
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

# Lunes de esta semana, en hora Argentina (UTC-3): así la semana 1 del plan es la actual
monday_this_week() {
  jq -nr '(now - 10800) as $t
    | ($t | strftime("%u") | tonumber) as $dow
    | ($t - ($dow - 1) * 86400) | strftime("%Y-%m-%d")'
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

# check_in <email> <json> → check-in de hoy con el token del atleta (si ya hizo uno hoy, lo pisa)
check_in() {
  local token athlete_id
  token=$(login "$1" | jq -r .accessToken)
  athlete_id=$(api GET "/estructura/api/v1/me" "$token" | jq -r .athleteId)
  api POST "/seguimiento/api/v1/athletes/$athlete_id/check-in" "$token" "$2" \
    | jq -r --arg who "$1" '"  check-in de \($who): \(.trafficLight) (\(.trafficLightReason // "-"))"'
}

PF_TOKEN=$(login pf@protraining.com | jq -r .accessToken)

# --- check-ins de hoy (los que no se hacen en vivo) ------------------------

load_check_ins() {
  echo "Check-ins de hoy:"
  # Joaquín durmió poco → amarillo
  check_in joaquin@protraining.com '{"sleepHours":4.5,"sleepQuality":2,"muscleSoreness":2,"fatigueLevel":3,"moodRate":3,"stressLevel":3,"effortRate":6,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Facundo, todo bien → verde
  check_in facundo@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":1,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Valentina con dolor nuevo en el cuádriceps → amarillo
  check_in valentina@protraining.com '{"sleepHours":7.5,"sleepQuality":4,"muscleSoreness":3,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":6,"isSick":false,"painZones":"cuadriceps:3","hasActiveInjury":false}'
  # Camila, todo bien → verde
  check_in camila@protraining.com '{"sleepHours":8.5,"sleepQuality":5,"muscleSoreness":1,"fatigueLevel":1,"moodRate":5,"stressLevel":1,"effortRate":4,"isSick":false,"painZones":null,"hasActiveInjury":false}'
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
  start=$(monday_this_week)
  api POST "/entrenamiento/api/v1/training-plans/assignments" "$PF_TOKEN" \
    "{\"trainingPlanId\":\"$plan_id\",\"startDate\":\"$start\",\"categoryId\":\"$PRIMERA_ID\"}" >/dev/null
  echo "  plan \"Fuerza base\" asignado a Primera División desde el $start"

  # Lucas con lesión activa → rojo; Martín bien → verde
  check_in atleta@protraining.com '{"sleepHours":7,"sleepQuality":3,"muscleSoreness":3,"fatigueLevel":3,"moodRate":3,"stressLevel":2,"effortRate":6,"isSick":false,"painZones":null,"hasActiveInjury":true}'
  check_in martin@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":1,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
}

if [[ "$MODE" == "checkins" ]]; then
  load_check_ins
  echo "Listo."
  exit 0
fi

# --- estructura ------------------------------------------------------------

if api GET "/estructura/api/v1/organizations" "$PF_TOKEN" | jq -e '.[] | select(.name == "Club Demo")' >/dev/null; then
  echo "Ya hay datos de la demo cargados (existe \"Club Demo\")." >&2
  echo "Para empezar de cero: docker compose down -v && docker compose up -d --build" >&2
  echo "Para cargar solo los check-ins de hoy: $0 checkins" >&2
  exit 1
fi

echo "Organizaciones y categorías:"
new_org() { api POST "/estructura/api/v1/organizations" "$PF_TOKEN" "$(jq -nc --arg n "$1" --arg d "$2" '{name:$n,description:$d}')" | jq -r .id; }
new_category() { api POST "/estructura/api/v1/categories" "$PF_TOKEN" "$(jq -nc --arg o "$1" --arg n "$2" '{organizationId:$o,name:$n}')" | jq -r .id; }

CLUB_ID=$(new_org "Club Demo" "Club de rugby amateur")
PERSONAL_ID=$(new_org "Personalizados" "Alumnos particulares")
GYM_ID=$(new_org "Gimnasio" "Gimnasio del barrio")
PRIMERA_ID=$(new_category "$CLUB_ID" "Primera División")
RESERVA_ID=$(new_category "$CLUB_ID" "Reserva")
PARTICULARES_ID=$(new_category "$PERSONAL_ID" "Alumnos particulares")
MUSCULACION_ID=$(new_category "$GYM_ID" "Musculación")
echo "  Club Demo (Primera División, Reserva), Personalizados, Gimnasio"

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
echo "  8 atletas (Nicolás y Agustina sin cuenta: en el semáforo quedan sin check-in)"

# Vincular cada atleta a su cuenta de Identidad. Lo hace la invitación por QR (Sprint 2);
# mientras tanto, directo en la base de Estructura (solo desarrollo).
echo "Vínculo de las cuentas:"
set -a
# shellcheck disable=SC1091
source .env
set +a
link_sql=""
for pair in "atleta@protraining.com:$LUCAS_ID" "martin@protraining.com:$MARTIN_ID" \
  "joaquin@protraining.com:$JOAQUIN_ID" "facundo@protraining.com:$FACUNDO_ID" \
  "valentina@protraining.com:$VALENTINA_ID" "camila@protraining.com:$CAMILA_ID"; do
  email="${pair%%:*}"
  athlete_id="${pair#*:}"
  account_id=$(login "$email" | jq -r .user.id)
  link_sql+="update athletes set account_id = '$account_id' where id = '$athlete_id';"
done
docker compose exec -T db-estructura psql -q -U "$POSTGRES_USER" -d estructura -v ON_ERROR_STOP=1 -c "$link_sql"
echo "  6 cuentas vinculadas"

# --- récords y planificación -----------------------------------------------

SQUAT_ID=$(exercise_id "Sentadilla con barra")
BENCH_ID=$(exercise_id "Press de banca con barra - Agarre medio")
RDL_ID=$(exercise_id "Peso muerto rumano")

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
echo "  11 récords (falta a propósito la sentadilla de Lucas)"

# Sesiones el miércoles, jueves y viernes: así hay rutina hoy, en el ensayo y en la demo.
# DayOfWeek va como número (3 = miércoles, 4 = jueves, 5 = viernes).
echo "Planificación:"
PLAN_JSON=$(jq -nc --arg squat "$SQUAT_ID" --arg bench "$BENCH_ID" --arg rdl "$RDL_ID" '{
  name: "Fuerza base",
  objective: "Base de fuerza para la pretemporada",
  weeks: [{
    weekNumber: 1,
    sessions: [
      {name: "Día A · Fuerza", order: 1, dayOfWeek: 3, coachNotes: "Entrada en calor de 10 minutos antes de cargar la barra.",
        exercises: [
          {exerciseId: $squat, order: 1, sets: 4, reps: 5, targetPercentage: 75},
          {exerciseId: $bench, order: 2, sets: 4, reps: 6, targetPercentage: 70}]},
      {name: "Día B · Fuerza", order: 2, dayOfWeek: 4, coachNotes: "Pausa de 2 minutos entre series.",
        exercises: [
          {exerciseId: $squat, order: 1, sets: 4, reps: 5, targetPercentage: 80},
          {exerciseId: $rdl, order: 2, sets: 3, reps: 8, targetPercentage: 70}]},
      {name: "Día C · Fuerza", order: 3, dayOfWeek: 5, coachNotes: "Controlar la técnica en el descenso.",
        exercises: [
          {exerciseId: $squat, order: 1, sets: 4, reps: 5, targetPercentage: 80},
          {exerciseId: $bench, order: 2, sets: 4, reps: 6, targetPercentage: 75},
          {exerciseId: $rdl, order: 3, sets: 3, reps: 8, targetPercentage: 70}]}
    ]}]}')
api POST "/entrenamiento/api/v1/training-plans" "$PF_TOKEN" "$PLAN_JSON" >/dev/null
echo "  \"Fuerza base\": semana 1, miércoles, jueves y viernes (sin asignar)"

load_check_ins

if [[ "$MODE" == "completo" ]]; then
  load_live_parts
fi

echo "Listo. PF: pf@protraining.com · atletas: atleta@ (Lucas), martin@, joaquin@, facundo@, valentina@, camila@protraining.com · contraseña: $PASSWORD"
