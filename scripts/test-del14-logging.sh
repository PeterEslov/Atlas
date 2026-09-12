#!/usr/bin/env bash
#
# test-del14-logging.sh — snabbtest av Del 14 (Serilog + strukturerad loggning).
# Körs i git-bash/MINGW64 (eller vilken bash-kompatibel terminal som helst) på
# Peters maskin.
#
# Till skillnad från Del 13/15:s testskript, som förutsätter att Atlas.Api
# redan kör i ett annat fönster, STARTAR OCH STOPPAR det här skriptet
# Atlas.Api åt dig, en enda gång för hela körningen. Anledningen: det som ska
# verifieras den här gången är konsolutskriften själv (att Serilog verkligen
# är loggnings-motorn, inte bara att ett HTTP-anrop svarar rätt), och det
# finns inget sätt att komma åt den om skriptet inte äger processen som
# skriver den. Skriptet bygger och startar den kompilerade DLL:en direkt
# (dotnet bin/.../Atlas.Api.dll) i stället för `dotnet run` — `dotnet run`
# lägger till ett extra process-lager (bygg-processen startar sedan appen som
# ett eget barn), vilket gör den bakgrundsprocess-PID skriptet fångar upp
# opålitlig att döda rent i git-bash på Windows. Att köra DLL:en direkt gör
# `dotnet`-processen skriptet startar till själva appen, så en vanlig `kill`
# på den fångade PID:en faktiskt stänger ner den.
#
# Vad testas:
#
#   TEST 1  Application Insights-grenen kraschar inte appen — appen startar
#           och svarar på /health även med en (påhittad)
#           ApplicationInsights:ConnectionString satt hela körningen igenom.
#           Ingen riktig Azure-resurs finns lokalt att verifiera leverans
#           mot — det här bevisar bara att kodvägen som bygger
#           TelemetryConfiguration och lägger till AI-sinket är säker att
#           slå på.
#   TEST 2  Serilogs konsolformat syns          — "[INF]"/"[WRN]" i stället
#                                                  för det gamla "info:"/"warn:"
#   TEST 3  UseSerilogRequestLogging (Del 14)   — en rad per HTTP-anrop, med
#                                                  rätt statuskod
#   TEST 4  Misslyckad inloggning loggas nu     — fel lösenord -> WRN-rad
#                                                  (fanns INTE innan Del 14:
#                                                  AuthenticationException
#                                                  fångades tyst av
#                                                  ExceptionHandlingMiddleware)
#   TEST 5  Misslyckad inloggning, deaktiverat  — samma sak för ett
#           konto                                deaktiverat konto
#   TEST 6  Lyckad inloggning loggas            — bekräftar att en redan
#                                                  befintlig _logger-rad
#                                                  fortfarande fungerar
#                                                  oförändrad genom Serilog
#
# Fyll i BASE_URL/ORG_ID nedan om dina skiljer sig från default.
# Kräver att .NET SDK och en migrerad databas redan finns (se README.md).
#
# Kör: bash scripts/test-del14-logging.sh

set -uo pipefail

BASE_URL="http://localhost:5080"
ORG_ID="9a6dbc3f-9472-4d0c-b03c-00ae93e1bba3"  # Northstar IT — måste redan finnas i din databas
STAMP=$(date +%s)
API_PROJECT="src/Atlas.Api"
API_DLL_RELATIVE="bin/Debug/net10.0/Atlas.Api.dll"
API_DLL="$API_PROJECT/$API_DLL_RELATIVE"
LOG_FILE="$(mktemp 2>/dev/null || echo "/tmp/atlas-del14-console-$STAMP.log")"
API_PID=""
FAILED=0

# ---------- hjälpfunktioner ----------

if command -v jq >/dev/null 2>&1; then HAS_JQ=1; else HAS_JQ=0; fi

json_field() {
  local json="$1" field="$2"
  if [ "$HAS_JQ" = "1" ]; then
    echo "$json" | jq -r ".$field // empty"
  else
    echo "$json" | grep -o "\"$field\":\"[^\"]*\"\|\"$field\":[^,}]*" \
      | head -n1 | sed -E "s/\"$field\":\"?([^\"]*)\"?/\1/"
  fi
}

pretty() { if [ "$HAS_JQ" = "1" ]; then echo "$1" | jq .; else echo "$1"; fi; }
line() { printf '%s\n' "----------------------------------------------------------------"; }
pass() { printf '  \033[32mPASS\033[0m — %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m — %s\n' "$1"; FAILED=1; }
info() { printf '  %s\n' "$1"; }

http_status_for() { curl -s -o /dev/null -w "%{http_code}" "$@"; }

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
    stop_api
    exit 1
  fi
}

stop_api() {
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then
    kill "$API_PID" 2>/dev/null
    wait "$API_PID" 2>/dev/null
  fi
}
trap stop_api EXIT

# ---------- 0. Bygg, kontrollera att porten är fri, starta Atlas.Api ----------
line
if curl -s -o /dev/null "$BASE_URL/health"; then
  echo "Något svarar redan på $BASE_URL/health — det här skriptet behöver äga"
  echo "processen självt för att kunna läsa dess konsolutskrift. Stoppa din"
  echo "körande 'dotnet run' (eller vad det nu är som lyssnar på porten) och"
  echo "kör skriptet igen."
  exit 1
fi

echo "Bygger Atlas.Api ..."
if ! dotnet build "$API_PROJECT" -c Debug --nologo -v quiet; then
  echo "Bygget misslyckades — se ovan."
  exit 1
fi

if [ ! -f "$API_DLL" ]; then
  echo "Hittade inte $API_DLL efter en lyckad build — kontrollera TargetFramework"
  echo "i $API_PROJECT/Atlas.Api.csproj (skriptet antar net10.0)."
  exit 1
fi

echo "TEST 1: Startar Atlas.Api med en (påhittad) ApplicationInsights:ConnectionString satt ..."
FAKE_AI_CONNECTION_STRING="InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://swedencentral-1.in.applicationinsights.azure.com/"
: > "$LOG_FILE"
# ContentRootPath, med INGET --contentRoot alls, faller tillbaka på processens
# arbetskatalog (Directory.GetCurrentDirectory()) — inte på DLL:ens egen mapp,
# vilket de två föregående felsökningsrundorna av det här skriptet antog.
# Peters egen diagnostik (körde DLL:en direkt, i förgrunden, från repo-roten)
# bevisade det: appsettings.Development.json fanns med rätt innehåll i
# bin/Debug/net10.0/, ändå kunde appen inte hitta den — eftersom den letade i
# repo-roten (arbetskatalogen), inte i src/Atlas.Api/ där filen faktiskt
# kopierats till. Miljönamnet (Development/Production) spelade aldrig någon
# roll i de två tidigare försöken: fel KATALOG genomsöktes helt oavsett, så
# ingen fil hittades alls, med samma "was not found"-fel i båda fallen.
#
# Fixen är en ABSOLUT --contentRoot, inte ingen alls och inte en relativ (en
# relativ --contentRoot-sträng löses istället upp mot AppContext.BaseDirectory,
# DLL:ens egen mapp — det var den första felsökningsrundans separata, redan
# fixade bugg). pwd -W ger sökvägen i Windows-form (C:/Users/...) i stället
# för git-bashs interna /c/Users/...-form, som .NET (ett vanligt Windows-
# program, inte ett MSYS-program) inte kan lita på att tolka rätt.
WIN_REPO_ROOT="$(pwd -W 2>/dev/null || pwd)"
dotnet "$API_DLL" \
  --urls "$BASE_URL" \
  --contentRoot "$WIN_REPO_ROOT/$API_PROJECT" \
  --environment Development \
  --ApplicationInsights:ConnectionString="$FAKE_AI_CONNECTION_STRING" \
  >>"$LOG_FILE" 2>&1 &
API_PID=$!

waited=0
until curl -s -o /dev/null "$BASE_URL/health"; do
  sleep 1
  waited=$((waited + 1))
  if [ "$waited" -ge 30 ]; then
    echo "Atlas.Api svarade inte på $BASE_URL/health inom 30 sekunder. Konsolutskrift:"
    cat "$LOG_FILE"
    exit 1
  fi
  if ! kill -0 "$API_PID" 2>/dev/null; then
    echo "Atlas.Api-processen dog under uppstart. Konsolutskrift:"
    cat "$LOG_FILE"
    exit 1
  fi
done
pass "Atlas.Api startade och svarar på /health med ApplicationInsights:ConnectionString satt — AI-sinket kopplas in utan att krascha appen."
info "Det här bevisar bara att kodvägen är säker att slå på, inte att telemetri faktiskt når Azure — det kräver en riktig Application Insights-resurs (se docs/AZURE_DEPLOYMENT.md när den sektionen finns)."

# ---------- Registrera en engångs-Admin och en testanvändare ----------
line
echo "Registrerar en engångs-Admin och en testanvändare (organizationId=$ORG_ID) ..."
ADMIN_EMAIL="del14.admin.$STAMP@example.test"
ADMIN_PASSWORD="TestPassword123!"
ADMIN_REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"Del14 Test Admin\",\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"organizationId\":\"$ORG_ID\",\"role\":3}")
ADMIN_TOKEN=$(json_field "$ADMIN_REGISTER_RESPONSE" "token")
if [ -z "$ADMIN_TOKEN" ]; then
  echo "Kunde inte registrera testadmin — kontrollera att ORG_ID ($ORG_ID) finns i din databas. Svar:"
  pretty "$ADMIN_REGISTER_RESPONSE"
  exit 1
fi
ADMIN_AUTH_HEADER="Authorization: Bearer $ADMIN_TOKEN"
info "Testadmin registrerad: $ADMIN_EMAIL"

TEST_EMAIL="del14.user.$STAMP@example.test"
TEST_PASSWORD="TestPassword123!"
USER_REGISTER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"fullName\":\"Del14 Test User\",\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASSWORD\",\"organizationId\":\"$ORG_ID\",\"role\":0}")
TEST_USER_ID=$(json_field "$USER_REGISTER_RESPONSE" "userId")
if [ -z "$TEST_USER_ID" ]; then
  echo "Kunde inte registrera testanvändare. Svar:"
  pretty "$USER_REGISTER_RESPONSE"
  exit 1
fi
info "Testanvändare skapad: $TEST_USER_ID ($TEST_EMAIL)"

# ---------- TEST 2 + 3: konsolformat + request-loggen ----------
line
echo "TEST 2 + 3: Serilogs konsolformat och UseSerilogRequestLogging"
authed_request_expect_2xx GET "$BASE_URL/api/users/$TEST_USER_ID"

if grep -qE '\[[0-9:]+ INF\]' "$LOG_FILE"; then
  pass "konsolutskriften använder Serilogs eget format ([HH:mm:ss INF]), inte det gamla 'info:'-formatet."
else
  fail "hittade ingen [.. INF]-rad — loggar Serilog verkligen till konsolen?"
fi

if grep -qE 'GET /api/users/[0-9a-fA-F-]+ responded 200' "$LOG_FILE"; then
  pass "UseSerilogRequestLogging skrev en rad för GET-anropet, med rätt statuskod (200)."
else
  fail "hittade ingen 'responded 200'-rad för GET /api/users/... — UseSerilogRequestLogging verkar inte köra."
fi

# ---------- TEST 4: misslyckad inloggning, fel lösenord ----------
line
echo "TEST 4: Fel lösenord -> ny WRN-rad som INTE fanns innan Del 14"
LOGIN_STATUS=$(http_status_for -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$TEST_EMAIL\",\"password\":\"fel-losenord-helt-klart\"}")
info "POST /api/auth/login (fel lösenord) svarade: $LOGIN_STATUS"

if [ "$LOGIN_STATUS" = "401" ] && grep -qE "\[[0-9:]+ WRN\].*Failed login attempt for $TEST_EMAIL: invalid credentials" "$LOG_FILE"; then
  pass "401 mot klienten, och en tydlig WRN-rad i konsolen som identifierar vilket konto — osynligt före Del 14."
else
  fail "förväntade 401 + en matchande WRN-rad, fick status=$LOGIN_STATUS."
fi

# ---------- TEST 5: misslyckad inloggning, deaktiverat konto ----------
line
echo "TEST 5: Deaktiverat konto -> egen WRN-rad"
authed_request_expect_2xx POST "$BASE_URL/api/users/$TEST_USER_ID/deactivate"

LOGIN_STATUS=$(http_status_for -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASSWORD\"}")
info "POST /api/auth/login (deaktiverat konto, rätt lösenord) svarade: $LOGIN_STATUS"

if [ "$LOGIN_STATUS" = "401" ] && grep -qE "\[[0-9:]+ WRN\].*Failed login attempt for $TEST_EMAIL: account $TEST_USER_ID is deactivated" "$LOG_FILE"; then
  pass "401 mot klienten, och en WRN-rad som skiljer det här fallet (deaktiverat) från fel lösenord."
else
  fail "förväntade 401 + en matchande WRN-rad, fick status=$LOGIN_STATUS."
fi

authed_request_expect_2xx POST "$BASE_URL/api/users/$TEST_USER_ID/reactivate"

# ---------- TEST 6: lyckad inloggning loggas fortfarande ----------
line
echo "TEST 6: Lyckad inloggning -> den redan befintliga _logger-raden fungerar oförändrad genom Serilog"
LOGIN_RESPONSE=$(curl -s -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$TEST_EMAIL\",\"password\":\"$TEST_PASSWORD\"}")
LOGIN_TOKEN=$(json_field "$LOGIN_RESPONSE" "token")

if [ -n "$LOGIN_TOKEN" ] && grep -qE "\[[0-9:]+ INF\].*User $TEST_USER_ID logged in" "$LOG_FILE"; then
  pass "inloggningen lyckades och samma AuthService.LoginAsync-rad som fanns före Del 14 syns nu som en Serilog [INF]-rad."
else
  fail "inloggningen misslyckades, eller så hittades ingen matchande [INF]-rad."
fi

# ---------- Klart ----------
line
if [ "$FAILED" = "1" ]; then
  echo "Klart — minst ett test FAILADE, se ovan. Full konsolutskrift finns kvar i $LOG_FILE."
  exit 1
fi
echo "Klart. Samtliga tester PASS. Skapade testresurser (för felsökning eller manuell städning):"
info "Admin-konto:   $ADMIN_EMAIL"
info "Testanvändare: $TEST_EMAIL ($TEST_USER_ID) — aktiv igen (reaktiverad i TEST 5)"
info "Full konsolutskrift från körningen: $LOG_FILE"
