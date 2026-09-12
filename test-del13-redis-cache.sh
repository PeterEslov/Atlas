#!/usr/bin/env bash
#
# test-del13-redis-cache.sh — snabbtest av Del 13 (Redis-cache för
# GET /api/tickets/stats). Körs i git-bash/MINGW64 (eller vilken
# bash-kompatibel terminal som helst) på Peters maskin, mot en lokalt
# körande Atlas.Api (dotnet run) och en lokal Redis-container.
#
# Testar de tre sakerna som faktiskt bevisar att cache-aside + invalidering
# fungerar, inte bara att endpointen svarar 200:
#   TEST 1  Cache-hit        — två snabba anrop ska ge samma generatedAtUtc
#   TEST 2  Invalidering     — en statusändring ska ge NY generatedAtUtc direkt
#   TEST 3  Negativt test    — en kommentar (rör ingen räknad dimension) ska
#                               INTE ändra generatedAtUtc
#   TEST 4  Fail-open        — stoppar Redis-containern och verifierar att
#                               endpointen ändå svarar 200 (bara ocachat),
#                               aldrig 500. Körs bara om REDIS_CONTAINER_NAME
#                               hittas i `docker ps`; annars hoppas den över.
#
# Ingen andra användares user-id behövs (till skillnad från ett omtilldelnings-
# test) — TEST 3 använder AddComment istället för Assign, av precis det skälet.
#
# Fyll i BASE_URL/EMAIL/PASSWORD/TICKET_ID nedan om dina skiljer sig från
# default, kör sedan: bash scripts/test-del13-redis-cache.sh

set -euo pipefail

BASE_URL="http://localhost:5080"
EMAIL="mats.nilsson@telia.com"
PASSWORD="Tiaicisd12345+"
TICKET_ID="89dc048a-a70f-4e79-90b1-2c0e32f7ff1f"
REDIS_CONTAINER_NAME="atlas-redis"

# ---------- hjälpfunktioner ----------

if command -v jq >/dev/null 2>&1; then
  HAS_JQ=1
else
  HAS_JQ=0
fi

# Plockar ut ett enkelt JSON-fält. Använder jq om det finns installerat,
# annars en grep/sed-fallback som funkar fint för de platta (icke-nästlade)
# DTO:er det här skriptet pratar med (TicketStatsDto, TicketDetailDto, m.fl.).
json_field() {
  local json="$1" field="$2"
  if [ "$HAS_JQ" = "1" ]; then
    echo "$json" | jq -r ".$field // empty"
  else
    echo "$json" | grep -o "\"$field\":\"[^\"]*\"\|\"$field\":[^,}]*" \
      | head -n1 | sed -E "s/\"$field\":\"?([^\"]*)\"?/\1/"
  fi
}

pretty() {
  if [ "$HAS_JQ" = "1" ]; then echo "$1" | jq .; else echo "$1"; fi
}

line() { printf '%s\n' "----------------------------------------------------------------"; }
pass() { printf '  \033[32mPASS\033[0m — %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m — %s\n' "$1"; }
info() { printf '  %s\n' "$1"; }

# ---------- 0. Logga in ----------
line
echo "Loggar in som $EMAIL ..."
LOGIN_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")

TOKEN=$(json_field "$LOGIN_RESPONSE" "token")

if [ -z "$TOKEN" ]; then
  echo "Kunde inte logga in. Svar från servern:"
  pretty "$LOGIN_RESPONSE"
  exit 1
fi
info "Inloggad OK (token mottagen, ${#TOKEN} tecken)."

AUTH_HEADER="Authorization: Bearer $TOKEN"

get_stats() {
  curl -s "$BASE_URL/api/tickets/stats" -H "$AUTH_HEADER"
}

get_ticket() {
  curl -s "$BASE_URL/api/tickets/$TICKET_ID" -H "$AUTH_HEADER"
}

# ---------- Säkerställ känt, modifierbart läge på testärendet ----------
# ChangeStatus/AssignAsync/AddComment kastar alla ett fel om ärendet är
# Closed(5) eller Cancelled(6) (Ticket.EnsureNotClosed) — så om någon manuellt
# testat klart det ärendet sen sist, återöppnar vi det här, robust mot vilket
# tillstånd det råkar stå i just nu.
CURRENT_TICKET=$(get_ticket)
CURRENT_STATUS=$(json_field "$CURRENT_TICKET" "status")

if [ -z "$CURRENT_STATUS" ]; then
  echo "Kunde inte hämta ärende $TICKET_ID. Svar:"
  pretty "$CURRENT_TICKET"
  exit 1
fi

if [ "$CURRENT_STATUS" = "5" ] || [ "$CURRENT_STATUS" = "6" ]; then
  info "Ärendet är Closed/Cancelled (status=$CURRENT_STATUS) — återöppnar det först ..."
  curl -s -X POST "$BASE_URL/api/tickets/$TICKET_ID/reopen" -H "$AUTH_HEADER" > /dev/null
  CURRENT_STATUS=1
fi

# ---------- TEST 1: Cache-hit ----------
line
echo "TEST 1: Cache-hit (två snabba anrop ska ge samma generatedAtUtc)"
STATS_A=$(get_stats)
GEN_A=$(json_field "$STATS_A" "generatedAtUtc")
STATS_B=$(get_stats)
GEN_B=$(json_field "$STATS_B" "generatedAtUtc")

info "Anrop 1: generatedAtUtc=$GEN_A"
info "Anrop 2: generatedAtUtc=$GEN_B"
if [ -n "$GEN_A" ] && [ "$GEN_A" = "$GEN_B" ]; then
  pass "andra anropet var en cache-hit."
else
  fail "generatedAtUtc ändrades (eller kunde inte läsas) — cachen träffades inte som väntat."
fi

# ---------- TEST 2: Invalidering vid statusändring ----------
line
echo "TEST 2: Invalidering (statusändring ska ge NY generatedAtUtc direkt)"

if [ "$CURRENT_STATUS" = "2" ]; then NEW_STATUS=1; else NEW_STATUS=2; fi  # 1=Open, 2=InProgress

BEFORE=$(get_stats)
GEN_BEFORE=$(json_field "$BEFORE" "generatedAtUtc")
info "Före (status $CURRENT_STATUS -> $NEW_STATUS): generatedAtUtc=$GEN_BEFORE, openCount=$(json_field "$BEFORE" "openCount"), inProgressCount=$(json_field "$BEFORE" "inProgressCount")"

curl -s -X POST "$BASE_URL/api/tickets/$TICKET_ID/status" \
  -H "$AUTH_HEADER" -H "Content-Type: application/json" \
  -d "{\"status\": $NEW_STATUS}" > /dev/null

AFTER=$(get_stats)
GEN_AFTER=$(json_field "$AFTER" "generatedAtUtc")
info "Efter: generatedAtUtc=$GEN_AFTER, openCount=$(json_field "$AFTER" "openCount"), inProgressCount=$(json_field "$AFTER" "inProgressCount")"

if [ -n "$GEN_BEFORE" ] && [ "$GEN_BEFORE" != "$GEN_AFTER" ]; then
  pass "cachen invaliderades — ny snapshot beräknades direkt, ingen väntan på TTL."
else
  fail "generatedAtUtc oförändrad — invalideringen verkar inte ha kört."
fi

# ---------- TEST 3: Negativt test — en kommentar invaliderar INTE ----------
line
echo "TEST 3: Negativt test (en kommentar rör ingen räknad dimension — generatedAtUtc ska förbli OFÖRÄNDRAD)"

BEFORE=$(get_stats)
GEN_BEFORE=$(json_field "$BEFORE" "generatedAtUtc")
info "Före: generatedAtUtc=$GEN_BEFORE"

curl -s -X POST "$BASE_URL/api/tickets/$TICKET_ID/comments" \
  -H "$AUTH_HEADER" -H "Content-Type: application/json" \
  -d '{"body": "Automatiskt testmeddelande fran test-del13-redis-cache.sh", "isInternal": true}' > /dev/null

AFTER=$(get_stats)
GEN_AFTER=$(json_field "$AFTER" "generatedAtUtc")
info "Efter: generatedAtUtc=$GEN_AFTER"

if [ -n "$GEN_BEFORE" ] && [ "$GEN_BEFORE" = "$GEN_AFTER" ]; then
  pass "cachen lämnades orörd, precis som tänkt (en kommentar räknas inte)."
else
  fail "generatedAtUtc ändrades — något invaliderade cachen som inte borde ha gjort det."
fi

# ---------- TEST 4: Fail-open (kräver att Redis-containern kan stoppas/startas härifrån) ----------
line
echo "TEST 4: Fail-open (Redis nere ska ge 200 med färska data, aldrig 500)"

if command -v docker >/dev/null 2>&1 && docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$REDIS_CONTAINER_NAME"; then
  info "Stoppar containern '$REDIS_CONTAINER_NAME' ..."
  docker stop "$REDIS_CONTAINER_NAME" > /dev/null

  HTTP_STATUS=$(curl -s -o /tmp/del13_failopen_body.json -w "%{http_code}" "$BASE_URL/api/tickets/stats" -H "$AUTH_HEADER")
  info "HTTP-status med Redis nere: $HTTP_STATUS"

  if [ "$HTTP_STATUS" = "200" ]; then
    pass "endpointen svarade fortfarande 200 utan Redis (kolla Atlas.Api-loggen efter en LogWarning om misslyckad Redis GET/SET)."
  else
    fail "endpointen svarade $HTTP_STATUS istället för 200 — fail-open fungerar inte som tänkt."
  fi

  info "Startar containern igen ..."
  docker start "$REDIS_CONTAINER_NAME" > /dev/null
  sleep 1
  info "Redis igång igen. Ett nytt anrop bör nu cachas som vanligt (se TEST 1)."
else
  info "Hoppar över — hittade ingen körande Docker-container som heter '$REDIS_CONTAINER_NAME'."
  info "Sätt REDIS_CONTAINER_NAME högst upp i skriptet om din container har ett annat namn,"
  info "eller kör manuellt: docker stop $REDIS_CONTAINER_NAME && curl ... && docker start $REDIS_CONTAINER_NAME"
fi

line
echo "Klart. Sista stats-snapshot:"
pretty "$(get_stats)"
