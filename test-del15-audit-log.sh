#!/usr/bin/env bash
#
# test-del15-audit-log.sh — snabbtest av Del 15 (systemomfattande AuditLog).
# Körs i git-bash/MINGW64 (eller vilken bash-kompatibel terminal som helst)
# på Peters maskin, mot en lokalt körande Atlas.Api (dotnet run).
#
# Testar att AuditLog faktiskt skrivs (och läses tillbaka via GET
# /api/audit-logs) för alla fyra entitetstyper Del 15 kopplade in, plus att
# åtkomstkontrollen (Admin-only) verkligen stoppar en Manager:
#
#   TEST 1  User skapad          — självregistrering -> 'Created'
#   TEST 2  User rollbyte        — -> 'RoleChanged' med old/new i JSON
#   TEST 3  User deaktiverad/reaktiverad -> 'Deactivated' / 'Reactivated'
#   TEST 4  Organization skapad/omdöpt/deaktiverad -> 'Created' / 'Renamed' / 'Deactivated'
#   TEST 5  Project skapat/arkiverat/avarkiverat -> 'Created' / 'Archived' / 'Unarchived'
#   TEST 6  Ticket raderad       — hård-radering -> 'Deleted' med en
#                                   ögonblicksbild (title/status/priority),
#                                   den enda kvarvarande spåren efter att
#                                   TicketHistory/Comments cascadar bort
#   TEST 7  Åtkomstkontroll      — en Manager-token ska få 403 på
#                                   GET /api/audit-logs (AuditLog.Read är
#                                   Admin-only, se RolePermissions)
#
# Skriptet registrerar sina egna engångskonton (en Admin, en vanlig
# testanvändare, en Manager) via POST /api/auth/register i stället för att
# kräva att du redan har ett Admin-konto liggande — det utnyttjar medvetet
# den öppna självregistreringen (RegisterRequest's egen kommentar: "anyone
# can create an account with any role and organization... Don't ship this
# endpoint open to the public as-is") för att skriptet ska kunna köras helt
# själv, utan att du behöver leta upp inloggningsuppgifter först. Det är
# alltså inte ett nytt hål Del 15 introducerar, bara Del 5:s redan
# dokumenterade lucka som råkar göra det här skriptet bekvämt att skriva.
#
# Städning: det finns ingen radera-användare/radera-organisation-endpoint i
# API:et (bara deaktivera/arkivera), så testkontona för Admin/Manager/
# testanvändaren blir kvar i din databas efter körning (samma sak händer
# redan varje gång du testar manuellt med ett nytt e-postkonto). Test-
# organisationen deaktiveras och testprojektet arkiveras i slutet, som en
# lätt städning snarare än en riktig radering.
#
# Fyll i BASE_URL/ORG_ID nedan om dina skiljer sig från default, kör sedan:
# bash scripts/test-del15-audit-log.sh

set -euo pipefail

BASE_URL="http://localhost:5080"
ORG_ID="9a6dbc3f-9472-4d0c-b03c-00ae93e1bba3"  # Northstar IT — måste redan finnas i din databas
STAMP=$(date +%s)

# ---------- hjälpfunktioner ----------

if command -v jq >/dev/null 2>&1; then
  HAS_JQ=1
else
  HAS_JQ=0
fi

# Plockar ut ett enkelt, platt JSON-fält (jq om det finns, annars grep/sed).
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

# GET /api/audit-logs filtrerat på entitet + action. Varje anrop här filtrerar
# tillräckligt snävt (samma entityId OCH samma action) för att i praktiken
# bara träffa den enda rad testet just skrev — därför räcker "totalCount" och
# "första förekomsten av fältet i hela svaret" (utan jq) för att vara ett
# korrekt test, inte bara en approximation.
audit_search() {
  local entityName="$1" entityId="$2" action="$3"
  curl -s -G "$BASE_URL/api/audit-logs" \
    -H "$ADMIN_AUTH_HEADER" \
    --data-urlencode "entityName=$entityName" \
    --data-urlencode "entityId=$entityId" \
    --data-urlencode "action=$action"
}

audit_total_count() {
  local json="$1"
  if [ "$HAS_JQ" = "1" ]; then
    echo "$json" | jq -r ".totalCount // 0"
  else
    echo "$json" | grep -o "\"totalCount\":[0-9]*" | head -n1 | sed -E 's/.*:([0-9]*)/\1/'
  fi
}

audit_first_field() {
  local json="$1" field="$2"
  if [ "$HAS_JQ" = "1" ]; then
    echo "$json" | jq -r ".items[0].$field // empty"
    return
  fi

  # oldValuesJson/newValuesJson are special: their VALUE is itself JSON text,
  # so the raw response contains an escaped quote (\") a few characters in —
  # e.g. "newValuesJson":"{\"role\":1}". The generic [^"]*-based extraction
  # below stops at that first escaped quote (it has no idea it's escaped,
  # since plain grep/sed don't parse JSON), which is exactly what produced
  # the garbled, truncated output without jq installed. For these two fields
  # specifically, bound the match on the NEXT field's name instead — thanks
  # to AuditLogDto's fixed property order (entityId, oldValuesJson,
  # newValuesJson, timestampUtc), oldValuesJson is always immediately
  # followed by "newValuesJson" and newValuesJson by "timestampUtc" — so a
  # greedy .* anchored on both sides correctly spans every embedded quote
  # instead of stopping at the first one.
  local next_field=""
  case "$field" in
    oldValuesJson) next_field="newValuesJson" ;;
    newValuesJson) next_field="timestampUtc" ;;
  esac

  if [ -n "$next_field" ]; then
    if echo "$json" | grep -q "\"$field\":null"; then
      return
    fi
    echo "$json" \
      | sed -E "s/.*\"$field\":\"(.*)\",\"$next_field\":.*/\1/" \
      | sed 's/\\"/"/g'
    return
  fi

  echo "$json" | grep -o "\"$field\":\"[^\"]*\"\|\"$field\":[^,}]*" \
    | head -n1 | sed -E "s/\"$field\":\"?([^\"]*)\"?/\1/"
}

# Bara HTTP-statuskoden, kroppen kastas — för 403/404-kontrollerna.
http_status_for() {
  curl -s -o /dev/null -w "%{http_code}" "$@"
}

# POST/DELETE mot ett Admin-skyddat endpoint där vi bryr oss om att det
# lyckades, inte bara att det svarade något. De körningarna nedan som tidigare
# gjorde "curl ... > /dev/null" utan att kolla statuskoden är precis vad som
# gjorde TEST 4:s första felsökningsrunda förvirrande: POST .../rename
# misslyckades tyst (visade sig bero på icke-ASCII-tecken i testdatan som inte
# överlevde vägen genom git-bash/curl oskadd), och det enda symptomet syntes
# tre rader längre ner som ett obegripligt "totalCount=0" på GET-anropet efter.
# Den här hjälpfunktionen gör motsvarande fel högljudda och tydliga direkt,
# med både statuskod och svarskropp.
authed_request_expect_2xx() {
  local method="$1" url="$2" data="${3:-}"
  local response status body
  if [ -n "$data" ]; then
    response=$(curl -s -w '\nHTTPSTATUS:%{http_code}' -X "$method" "$url" \
      -H "$ADMIN_AUTH_HEADER" -H "Content-Type: application/json" -d "$data")
  else
    response=$(curl -s -w '\nHTTPSTATUS:%{http_code}' -X "$method" "$url" -H "$ADMIN_AUTH_HEADER")
  fi
  status="${response##*HTTPSTATUS:}"
  body="${response%$'\n'HTTPSTATUS:*}"

  if [[ "$status" != 2* ]]; then
    echo "  $method $url misslyckades (HTTP $status). Svar:"
    pretty "$body"
    exit 1
  fi
}

# ---------- 0. Registrera en engångs-Admin och logga in som den ----------
line
echo "Registrerar en engångs-Admin-testanvändare (organizationId=$ORG_ID) ..."
ADMIN_EMAIL="del15.admin.$STAMP@example.test"
ADMIN_PASSWORD="TestPassword123!"
ADMIN_REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"Del15 Test Admin\",\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"organizationId\":\"$ORG_ID\",\"role\":3}")

ADMIN_TOKEN=$(json_field "$ADMIN_REGISTER_RESPONSE" "token")
if [ -z "$ADMIN_TOKEN" ]; then
  echo "Kunde inte registrera testadmin — kontrollera att ORG_ID ($ORG_ID) faktiskt finns i din databas. Svar:"
  pretty "$ADMIN_REGISTER_RESPONSE"
  exit 1
fi
info "Testadmin registrerad: $ADMIN_EMAIL"
ADMIN_AUTH_HEADER="Authorization: Bearer $ADMIN_TOKEN"

# ---------- TEST 1: User skapad ----------
line
echo "TEST 1: User skapad (självregistrering) -> AuditLog 'Created'"
TEST_USER_EMAIL="del15.user.$STAMP@example.test"
USER_REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"Del15 Test User\",\"email\":\"$TEST_USER_EMAIL\",\"password\":\"TestPassword123!\",\"organizationId\":\"$ORG_ID\",\"role\":0}")
TEST_USER_ID=$(json_field "$USER_REGISTER_RESPONSE" "userId")

if [ -z "$TEST_USER_ID" ]; then
  echo "Kunde inte registrera testanvändare. Svar:"
  pretty "$USER_REGISTER_RESPONSE"
  exit 1
fi
info "Testanvändare skapad: $TEST_USER_ID ($TEST_USER_EMAIL)"

AUDIT=$(audit_search "User" "$TEST_USER_ID" "Created")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "en 'Created'-rad skrevs för den nya användaren."
  info "newValuesJson: $(audit_first_field "$AUDIT" "newValuesJson")"
else
  fail "hittade ingen 'Created'-rad för $TEST_USER_ID (totalCount=$COUNT)."
fi

# ---------- TEST 2: User rollbyte ----------
line
echo "TEST 2: Rollbyte (Customer -> Agent) -> AuditLog 'RoleChanged'"
authed_request_expect_2xx POST "$BASE_URL/api/users/$TEST_USER_ID/role" '{"role": 1}'

AUDIT=$(audit_search "User" "$TEST_USER_ID" "RoleChanged")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "en 'RoleChanged'-rad skrevs."
  info "oldValuesJson: $(audit_first_field "$AUDIT" "oldValuesJson")  ->  newValuesJson: $(audit_first_field "$AUDIT" "newValuesJson")"
else
  fail "hittade ingen 'RoleChanged'-rad (totalCount=$COUNT)."
fi

# ---------- TEST 3: User deaktiverad + reaktiverad ----------
line
echo "TEST 3: Deaktivering + reaktivering -> AuditLog 'Deactivated' / 'Reactivated'"
authed_request_expect_2xx POST "$BASE_URL/api/users/$TEST_USER_ID/deactivate"

AUDIT=$(audit_search "User" "$TEST_USER_ID" "Deactivated")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Deactivated' loggades."
else
  fail "ingen 'Deactivated'-rad hittades (totalCount=$COUNT)."
fi

authed_request_expect_2xx POST "$BASE_URL/api/users/$TEST_USER_ID/reactivate"

AUDIT=$(audit_search "User" "$TEST_USER_ID" "Reactivated")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Reactivated' loggades."
else
  fail "ingen 'Reactivated'-rad hittades (totalCount=$COUNT)."
fi

# ---------- TEST 4: Organization skapad + omdöpt + deaktiverad ----------
line
echo "TEST 4: Organization skapad + omdöpt + deaktiverad -> AuditLog 'Created' / 'Renamed' / 'Deactivated'"
ORG_NAME="Del15 Test Org $STAMP"
ORG_CREATE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/organizations" \
  -H "$ADMIN_AUTH_HEADER" -H "Content-Type: application/json" \
  -d "{\"name\":\"$ORG_NAME\",\"type\":1}")
TEST_ORG_ID=$(json_field "$ORG_CREATE_RESPONSE" "id")

if [ -z "$TEST_ORG_ID" ]; then
  echo "Kunde inte skapa testorganisation. Svar:"
  pretty "$ORG_CREATE_RESPONSE"
  exit 1
fi
info "Testorganisation skapad: $TEST_ORG_ID ('$ORG_NAME')"

AUDIT=$(audit_search "Organization" "$TEST_ORG_ID" "Created")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Created' loggades för organisationen."
else
  fail "ingen 'Created'-rad hittades (totalCount=$COUNT)."
fi

RENAMED_ORG_NAME="${ORG_NAME} renamed"
authed_request_expect_2xx POST "$BASE_URL/api/organizations/$TEST_ORG_ID/rename" "{\"name\":\"$RENAMED_ORG_NAME\"}"

AUDIT=$(audit_search "Organization" "$TEST_ORG_ID" "Renamed")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Renamed' loggades."
  info "oldValuesJson: $(audit_first_field "$AUDIT" "oldValuesJson")  ->  newValuesJson: $(audit_first_field "$AUDIT" "newValuesJson")"
else
  fail "ingen 'Renamed'-rad hittades (totalCount=$COUNT)."
fi

authed_request_expect_2xx POST "$BASE_URL/api/organizations/$TEST_ORG_ID/deactivate"

AUDIT=$(audit_search "Organization" "$TEST_ORG_ID" "Deactivated")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Deactivated' loggades (lämnas deaktiverad — det är också städningen för den här testorganisationen)."
else
  fail "ingen 'Deactivated'-rad hittades (totalCount=$COUNT)."
fi

# ---------- TEST 5: Project skapat + arkiverat + avarkiverat ----------
line
echo "TEST 5: Project skapat + arkiverat + avarkiverat -> AuditLog 'Created' / 'Archived' / 'Unarchived'"
PROJECT_CREATE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/projects" \
  -H "$ADMIN_AUTH_HEADER" -H "Content-Type: application/json" \
  -d "{\"organizationId\":\"$ORG_ID\",\"name\":\"Del15 Test Project $STAMP\",\"description\":\"Skapat av test-del15-audit-log.sh\"}")
TEST_PROJECT_ID=$(json_field "$PROJECT_CREATE_RESPONSE" "id")

if [ -z "$TEST_PROJECT_ID" ]; then
  echo "Kunde inte skapa testprojekt. Svar:"
  pretty "$PROJECT_CREATE_RESPONSE"
  exit 1
fi
info "Testprojekt skapat: $TEST_PROJECT_ID (i din riktiga org $ORG_ID, inte i testorganisationen ovan — den är ju redan deaktiverad)"

AUDIT=$(audit_search "Project" "$TEST_PROJECT_ID" "Created")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Created' loggades för projektet."
else
  fail "ingen 'Created'-rad hittades (totalCount=$COUNT)."
fi

authed_request_expect_2xx POST "$BASE_URL/api/projects/$TEST_PROJECT_ID/archive"

AUDIT=$(audit_search "Project" "$TEST_PROJECT_ID" "Archived")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Archived' loggades."
else
  fail "ingen 'Archived'-rad hittades (totalCount=$COUNT)."
fi

authed_request_expect_2xx POST "$BASE_URL/api/projects/$TEST_PROJECT_ID/unarchive"

AUDIT=$(audit_search "Project" "$TEST_PROJECT_ID" "Unarchived")
COUNT=$(audit_total_count "$AUDIT")
if [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "'Unarchived' loggades."
else
  fail "ingen 'Unarchived'-rad hittades (totalCount=$COUNT)."
fi

# ---------- TEST 6: Ticket raderad ----------
line
echo "TEST 6: Ticket raderad -> AuditLog 'Deleted' (den enda kvarvarande spåren efter cascade-raderingen)"
TICKET_CREATE_RESPONSE=$(curl -s -X POST "$BASE_URL/api/tickets" \
  -H "$ADMIN_AUTH_HEADER" -H "Content-Type: application/json" \
  -d "{\"title\":\"Del15 test-ticket $STAMP\",\"description\":\"Skapat av test-del15-audit-log.sh, raderas direkt.\",\"organizationId\":\"$ORG_ID\",\"priority\":0,\"projectId\":null,\"dueAtUtc\":null}")
TEST_TICKET_ID=$(json_field "$TICKET_CREATE_RESPONSE" "id")

if [ -z "$TEST_TICKET_ID" ]; then
  echo "Kunde inte skapa testärende. Svar:"
  pretty "$TICKET_CREATE_RESPONSE"
  exit 1
fi
info "Testärende skapat: $TEST_TICKET_ID"

authed_request_expect_2xx DELETE "$BASE_URL/api/tickets/$TEST_TICKET_ID"

DELETE_CHECK_STATUS=$(http_status_for "$BASE_URL/api/tickets/$TEST_TICKET_ID" -H "$ADMIN_AUTH_HEADER")
info "GET på det raderade ärendet svarar nu: $DELETE_CHECK_STATUS"

AUDIT=$(audit_search "Ticket" "$TEST_TICKET_ID" "Deleted")
COUNT=$(audit_total_count "$AUDIT")
if [ "$DELETE_CHECK_STATUS" = "404" ] && [ -n "$COUNT" ] && [ "$COUNT" -ge 1 ]; then
  pass "ärendet är genuint borta (404), men AuditLog har en ögonblicksbild kvar."
  info "oldValuesJson (sparad precis innan raderingen): $(audit_first_field "$AUDIT" "oldValuesJson")"
else
  fail "antingen svarade GET inte 404 (fick $DELETE_CHECK_STATUS), eller ingen 'Deleted'-rad hittades (totalCount=$COUNT)."
fi

# ---------- TEST 7: Åtkomstkontroll (Admin-only) ----------
line
echo "TEST 7: Åtkomstkontroll — en Manager-token ska INTE komma åt /api/audit-logs (403, inte 200)"
MANAGER_EMAIL="del15.manager.$STAMP@example.test"
MANAGER_REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"Del15 Test Manager\",\"email\":\"$MANAGER_EMAIL\",\"password\":\"TestPassword123!\",\"organizationId\":\"$ORG_ID\",\"role\":2}")
MANAGER_TOKEN=$(json_field "$MANAGER_REGISTER_RESPONSE" "token")

if [ -z "$MANAGER_TOKEN" ]; then
  echo "Kunde inte registrera testmanager. Svar:"
  pretty "$MANAGER_REGISTER_RESPONSE"
  exit 1
fi

MANAGER_STATUS=$(http_status_for "$BASE_URL/api/audit-logs" -H "Authorization: Bearer $MANAGER_TOKEN")
info "GET /api/audit-logs som Manager svarar: $MANAGER_STATUS"
if [ "$MANAGER_STATUS" = "403" ]; then
  pass "Manager nekades (403) — AuditLog.Read är verkligen Admin-only, precis som RolePermissions säger."
else
  fail "förväntade 403, fick $MANAGER_STATUS — behörighetskontrollen fungerar inte som tänkt."
fi

# ---------- Klart ----------
line
echo "Klart. Skapade testresurser (för felsökning eller manuell städning):"
info "Admin-konto:      $ADMIN_EMAIL"
info "Testanvändare:    $TEST_USER_EMAIL ($TEST_USER_ID) — aktiv, roll Agent"
info "Manager-konto:    $MANAGER_EMAIL"
info "Testorganisation: $TEST_ORG_ID — deaktiverad"
info "Testprojekt:      $TEST_PROJECT_ID (i $ORG_ID) — oarkiverat"
info "Testärende $TEST_TICKET_ID — raderat, finns bara kvar som AuditLog-rad."
