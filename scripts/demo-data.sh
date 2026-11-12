#!/usr/bin/env bash
# Datos de prueba de las demos (KAN-524, Demo 1; KAN-561, Demo 2). Solo para desarrollo local.
#
# Carga, con la API real y el token del PF de prueba, lo que la demo necesita ya armado:
# organizaciones, categorías, atletas vinculados a sus cuentas, 3 atletas más registrados por
# invitación (Sofía, Bruno y Diego), récords, la planificación "Fuerza base" (sin asignar),
# videos de YouTube del PF, el partido del sábado y los check-ins que no se muestran en vivo.
#
# Semáforo de la Demo 2 (reglas de sueño de la KAN-560), con "completo":
#   🔴 Lucas (lesión activa, isquiotibial) · Camila (lesión activa, hombro) · Sofía (muy poco sueño: 3,5 h)
#   🟡 Joaquín (sueño moderado: 6 h) · Valentina (dolor nuevo, cuádriceps)
#      Bruno (más de 12 h en 3 de los últimos 7 días) · Diego (dolor en cuádriceps → "Ver ajuste sugerido")
#   🟢 Martín y Facundo (8 h o más, sin otras señales)
#   ⚪ Nicolás y Agustina sin cuenta: quedan "Sin check-in"
#   (Valentina y Camila están en Alumnos particulares y Musculación, no en Primera División)
#
# Además deja listo: el WhatsApp del PF (botón "Contactar a mi preparador"), las lesiones de Lucas y Camila,
# una sesión de tests de campo en curso ("Evaluación de pretemporada") y, con "completo", una propuesta de
# récord de Martín para aprobar en el inicio del PF.
# Para la evolución: 3 récords anteriores por atleta (Martín, Facundo, Joaquín, Lucas y Diego) y 3 evaluaciones de
# campo cerradas con salto CMJ y 30 m (las fechas de esas marcas se ajustan en la base de Seguimiento).
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

PF_TOKEN=$(login pf@protraining.com | jq -r .accessToken)

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
  check_in camila@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":3,"fatigueLevel":2,"moodRate":3,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":"hombros:4","hasActiveInjury":true}'
  # Sofía durmió 3,5 h → rojo "Muy poco sueño" (menos de 4 h, KAN-560)
  check_in sofia@protraining.com '{"sleepHours":3.5,"sleepQuality":1,"muscleSoreness":2,"fatigueLevel":4,"moodRate":2,"stressLevel":3,"effortRate":5,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  # Bruno durmió más de 12 h hace 4 días, hace 2 días y hoy → amarillo "Durmió más de 12 horas en 3 de los últimos 7 días…"
  local long_sleep='{"sleepHours":13,"sleepQuality":3,"muscleSoreness":1,"fatigueLevel":3,"moodRate":3,"stressLevel":2,"effortRate":4,"isSick":false,"painZones":null,"hasActiveInjury":false}'
  check_in bruno@protraining.com "$long_sleep" 4
  check_in bruno@protraining.com "$long_sleep" 2
  check_in bruno@protraining.com "$long_sleep"
  # Diego con dolor en el cuádriceps y sentadilla en la rutina de hoy → amarillo y "Ver ajuste sugerido" (KAN-451/452)
  check_in diego@protraining.com '{"sleepHours":8,"sleepQuality":4,"muscleSoreness":3,"fatigueLevel":2,"moodRate":4,"stressLevel":2,"effortRate":5,"isSick":false,"painZones":"cuadriceps:3","hasActiveInjury":false}'
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

  # Martín registra 115 kg × 5 en sentadilla: Epley estima ~132,5 kg > su récord de 120 kg, así que queda una
  # propuesta de récord pendiente en el inicio del PF (KAN-392/394). Solo hay rutina de miércoles a viernes.
  local martin_token
  martin_token=$(login martin@protraining.com | jq -r .accessToken)
  if curl -sf -X POST "$GATEWAY/entrenamiento/api/v1/me/today-routine/performed-sets" \
    -H "Authorization: Bearer $martin_token" -H 'Content-Type: application/json' \
    -d "[{\"exerciseId\":\"$squat_id\",\"weight\":115,\"reps\":5}]" >/dev/null; then
    echo "  Martín registró sentadilla 115 kg × 5 → propuesta de récord para el PF"
  else
    echo "  (hoy no hay sesión en el plan: la propuesta de récord de Martín se genera de miércoles a viernes)" >&2
  fi

  # Lucas con lesión activa en el isquiotibial → rojo (la zona se ve pintada en la ficha del PF); Martín bien → verde
  check_in atleta@protraining.com '{"sleepHours":8,"sleepQuality":3,"muscleSoreness":3,"fatigueLevel":3,"moodRate":3,"stressLevel":2,"effortRate":6,"isSick":false,"painZones":"isquiotibiales:4","hasActiveInjury":true}'
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

# Sofía, Bruno y Diego se registran solos por la invitación de Primera División (KAN-397/398),
# como lo haría un atleta que escanea el QR: no hace falta vincular su cuenta a mano.
echo "Atletas registrados por invitación:"
SOFIA_ID=$(invite_athlete "Sofía" "Méndez" "sofia@protraining.com" "1150000009" "$PRIMERA_ID")
BRUNO_ID=$(invite_athlete "Bruno" "Castro" "bruno@protraining.com" "1150000010" "$PRIMERA_ID")
DIEGO_ID=$(invite_athlete "Diego" "Funes" "diego@protraining.com" "1150000011" "$PRIMERA_ID")
echo "  Sofía, Bruno y Diego en Primera División"

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
  "$(jq -nc --arg d "$MATCH_DATE" '{date:$d,startTime:"15:30:00",opponent:"Los Pumitas RC",venue:"Home",type:"Official",notes:"Fecha del torneo"}')" >/dev/null
echo "  Primera División vs. Los Pumitas RC, el $MATCH_DATE a las 15:30"

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
      '{zone:$z,description:$d,injuryDate:$from,estimatedReturnDate:$to}')" >/dev/null
}
injury "$LUCAS_ID" "isquiotibiales" "Distensión grado 1 en un sprint del entrenamiento."
injury "$CAMILA_ID" "hombros" "Sobrecarga del manguito rotador."
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
# Tres evaluaciones anteriores, ya cerradas (hace 10, 6 y 2 semanas), con salto CMJ y 30 m: así la evolución
# (salto contra carga, velocidad) muestra una curva. La API registra cada marca con la hora del momento, así que
# después se le pone la fecha de su sesión directo en la base de Seguimiento (solo datos de demo).
past_session() {  # past_session <nombre> <días atrás> → imprime el id de la sesión, en curso
  local id
  id=$(api POST "/seguimiento/api/v1/field-test-sessions" "$PF_TOKEN" \
    "$(jq -nc --arg c "$PRIMERA_ID" --arg d "$(day_ago "$2")" --arg n "$1" --arg j "$CMJ_TEST" --arg r "$SPRINT_TEST" \
      '{categoryId:$c,date:$d,name:$n,tests:[{catalogTestId:$j,order:1},{catalogTestId:$r,order:2}]}')" | jq -r .id)
  api PATCH "/seguimiento/api/v1/field-test-sessions/$id/status" "$PF_TOKEN" '{"status":"InProgress"}' >/dev/null
  echo "$id"
}
past_results() {  # past_results <sesión> <atleta> <CMJ en cm> <30 m en s>
  api POST "/seguimiento/api/v1/field-test-sessions/$1/results" "$PF_TOKEN" \
    "$(jq -nc --arg t "$CMJ_TEST" --arg a "$2" --argjson v "$3" '{catalogTestId:$t,athleteId:$a,value:$v,attempt:1}')" >/dev/null
  api POST "/seguimiento/api/v1/field-test-sessions/$1/results" "$PF_TOKEN" \
    "$(jq -nc --arg t "$SPRINT_TEST" --arg a "$2" --argjson v "$4" '{catalogTestId:$t,athleteId:$a,value:$v,attempt:1}')" >/dev/null
}
i=0
for s in "Evaluación inicial|70" "Control de mitad de bloque|42" "Control pre torneo|14"; do
  sid=$(past_session "${s%|*}" "${s#*|}")
  cmj_martin=(38 39.5 41) cmj_joaquin=(34 35.5 36.5) cmj_facundo=(41 42.5 44) cmj_lucas=(36 37.5 39) cmj_diego=(35 36 37.5)
  t30_martin=(4.52 4.45 4.38) t30_joaquin=(4.60 4.55 4.49) t30_facundo=(4.35 4.29 4.22) t30_lucas=(4.48 4.41 4.36) t30_diego=(4.55 4.50 4.44)
  past_results "$sid" "$MARTIN_ID" "${cmj_martin[$i]}" "${t30_martin[$i]}"
  past_results "$sid" "$JOAQUIN_ID" "${cmj_joaquin[$i]}" "${t30_joaquin[$i]}"
  past_results "$sid" "$FACUNDO_ID" "${cmj_facundo[$i]}" "${t30_facundo[$i]}"
  past_results "$sid" "$LUCAS_ID" "${cmj_lucas[$i]}" "${t30_lucas[$i]}"
  past_results "$sid" "$DIEGO_ID" "${cmj_diego[$i]}" "${t30_diego[$i]}"
  api PATCH "/seguimiento/api/v1/field-test-sessions/$sid/status" "$PF_TOKEN" '{"status":"Closed"}' >/dev/null
  i=$((i + 1))
done
docker compose exec -T db-seguimiento sh -c 'psql -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "
  UPDATE field_test_results r SET recorded_at = (s.date + time '\''15:00'\'') AT TIME ZONE '\''UTC'\''
  FROM field_test_sessions s WHERE r.session_id = s.id AND s.date < CURRENT_DATE"' >/dev/null
echo "  3 evaluaciones cerradas (hace 10, 6 y 2 semanas): salto CMJ y 30 m de Martín, Joaquín, Facundo, Lucas y Diego"

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

load_check_ins

if [[ "$MODE" == "completo" ]]; then
  load_live_parts
fi

echo "Listo. PF: pf@protraining.com · atletas: atleta@ (Lucas), martin@, joaquin@, facundo@, valentina@, camila@, sofia@, bruno@, diego@protraining.com · contraseña: $PASSWORD"
