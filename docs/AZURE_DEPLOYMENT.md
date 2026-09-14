# Azure-driftsättning (Del 8)

Det här dokumentet är en körbar checklista för att driftsätta Atlas.Api till
Azure App Service, med en Azure Pipelines-pipeline (Azure DevOps) som
bygger, testar och driftsätter automatiskt vid varje push till `main`.
Kommandona nedan kör du
själv — se resonemanget i README/ARCHITECTURE om varför: den här sessionen
kan inte pålitligt köra kommandon på din maskin just nu, men framför allt är
det *dina* beslut (prenumeration, resursnamn, kostnad) som CLI:t utför.

**Vad Del 8 faktiskt ger dig:** en App Service som svarar, och en pipeline
som håller den uppdaterad automatiskt. **Vad Del 8 medvetet inte ger dig:**
en fungerande databas i molnet. Azure App Service kan inte nå din lokala SQL
Server-instans (den sitter bakom ditt hemnätverks NAT, inte en offentlig
IP) — så `/api/auth/login`, `/api/tickets` och alla andra databasberoende
endpoints kommer att svara med ett fel tills Del 9 (Azure SQL) är på plats.
Det enda som är tänkt att fungera efter Del 8 är `/health` och, om du slår
på det, Swagger-UI:t på `/openapi/v1.json`-ytan. Det är inte en bugg i det
du bygger nu — det är precis vad man förväntar sig av en driftsättning som
ännu inte har en molndatabas bakom sig.

## 0. Förutsättningar

- Azure CLI inloggat: `az login`
- .NET 10 SDK lokalt (redan på plats sedan tidigare Delar)
- Ditt lokala repo pushat till ditt Azure DevOps-repo (Azure Repos). Sätt
  de här tre en gång, med dina riktiga värden:
  ```bash
  DEVOPS_ORG_URL=https://dev.azure.com/<din-organisation>
  DEVOPS_PROJECT=<ditt-projektnamn>
  DEVOPS_REPO=<ditt-reponamn>
  ```
  Om koden inte redan ligger där:
  ```bash
  git init
  git add .
  git commit -m "Initial commit"
  git remote add origin "$DEVOPS_ORG_URL/$DEVOPS_PROJECT/_git/$DEVOPS_REPO"
  git push -u origin main
  ```
  (Redan pushat sedan tidigare? Då kan du hoppa över det här — resten av
  dokumentet förutsätter bara att `main` finns i ditt Azure DevOps-repo.)

**Kör du Git Bash (MINGW64) på Windows? Kör den här raden innan du fortsätter:**

```bash
export MSYS_NO_PATHCONV=1
```

Det här dokumentet har flera kommandon längre ner vars värde börjar med `/`
(t.ex. `--scope "$VAULT_ID"` i avsnitt 3, och
`--scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG"` i avsnitt 5)
— fullständiga Azure-resurs-ID:n, inte filsökvägar. Git Bash bygger på
MSYS2, och MSYS2:s shell skriver **automatiskt** om varje kommandoradsargument
som ser ut som en Unix-sökväg (börjar med `/`) till en Windows-sökväg innan
den når ett "riktigt" Windows-program som `az.exe` — även när argumentet
egentligen är en URL-liknande resurs-ID-sträng, inte en fil. Resultatet är
att `az` aldrig ser det `/subscriptions/...`-värde du satte i variabeln,
utan något som redan är omskrivet till en Windows-sökväg, vilket ger just
felet `(MissingSubscription) The request did not have a subscription or a
valid tenant level resource provider.` — även när prenumerationen, valvet
och variabeln alla är korrekta (så här kan det se helt rätt ut i ett
`echo`, och ändå fela). `MSYS_NO_PATHCONV=1` stänger av just den
auto-konverteringen för resten av terminalsessionen. Kör du PowerShell
eller `cmd.exe` istället för Git Bash berör det här dig inte alls — det är
specifikt en MSYS/Git-Bash-grej.
(Källa: [Azure CLI:s egen dokumentation om Git Bash](https://github.com/Azure/azure-cli/blob/dev/doc/use_cli_with_git_bash.md).)

## 1. Resursgrupp, App Service-plan och Web App

Region: **Sweden Central**, billigaste nivån (enligt din instruktion).
Resursnamnen följer din egen standard (`<typ>-<projekt>-<miljö>-<region>`) —
sätt dem en gång här som variabler, så byter du bara på en rad om du vill
ändra något, istället för att jaga upp varje förekomst längre ner i
dokumentet.

```bash
RG=rg-projectatlas-dev-sc
PLAN=plan-projectatlas-dev-sc
LOCATION=swedencentral

# Webbappens namn är det enda som INTE bara behöver vara unikt inom din
# prenumeration — det blir en del av <namn>.azurewebsites.net, en DNS-zon
# alla Azure-kunder delar, så det måste vara globalt unikt i hela Azure.
# app-projectatlas-dev-sc följer din namnstandard och verkar ledigt (ingen
# DNS-post hittad när jag kollade) — men "ledigt nu" är inte samma sak som
# "ledigt när du kör az webapp create", så ha ett par reserver redo:
#   app-projectatlas-dev-sc     (försök först)
#   app-atlas-northstar-dev-sc  (om upptaget — "Northstar" från projektets
#                                fiktiva bolag, Northstar IT)
#   app-projectatlas-dev-sc-01  (enklaste sättet att göra ett namn unikt
#                                om båda ovan är tagna)
WEBAPP_NAME=app-projectatlas-dev-sc

az group create --name "$RG" --location "$LOCATION"

# F1 (gratis) finns inte i alla region/OS-kombinationer — testa F1 först,
# fall tillbaka till B1 (billig, inte gratis) om Azure svarar att F1 inte
# finns för Linux i swedencentral just nu.
az appservice plan create \
  --name "$PLAN" \
  --resource-group "$RG" \
  --location "$LOCATION" \
  --sku F1 \
  --is-linux

az webapp list-runtimes --os linux --output table | grep -i dotnet
# ^ kör den här för att se den exakta runtime-identifieraren för .NET 10 —
# formatet har växlat mellan Azure CLI-versioner (t.ex. "DOTNETCORE:8.0"),
# så bekräfta strängen istället för att lita på exemplet nedan.

az webapp create \
  --name "$WEBAPP_NAME" \
  --resource-group "$RG" \
  --plan "$PLAN" \
  --runtime "DOTNETCORE:10.0"
```

## 2. Application settings (bootstrap-konfiguration)

Samma mönster som `appsettings.Development.json` redan använder lokalt,
bara flyttat till App Service Configuration istället för en fil i repot.
Värdena blir miljövariabler för processen; ASP.NET Core:s config-system
mappar dubbla understreck till kolon (`Jwt__Issuer` → config-nyckeln
`Jwt:Issuer`). Det här är för de icke-hemliga inställningarna — själva
signeringsnyckeln flyttar till Key Vault i nästa steg, inte hit.

Ny terminal sedan steg 1? `$RG`/`$WEBAPP_NAME` är då tomma — sätt om dem
(`RG=rg-projectatlas-dev-sc`, `WEBAPP_NAME=<namnet du faktiskt fick>`)
innan du kör nedanstående.

```bash
az webapp config appsettings set \
  --name "$WEBAPP_NAME" \
  --resource-group "$RG" \
  --settings \
    Jwt__Issuer=ProjectAtlas \
    Jwt__Audience=ProjectAtlas.Api \
    Jwt__ExpiryMinutes=60 \
    EnableSwaggerUi=true
```

`EnableSwaggerUi=true` här är ett medvetet demo-val för ett portfolioprojekt
— se kommentaren i `Program.cs` för varför det är en egen config-switch och
inte kopplat till `ASPNETCORE_ENVIRONMENT`. Sätt den till `false` (eller ta
bort den, samma sak som default) om du vill stänga av den igen.

## 3. Key Vault: JWT-signeringsnyckeln (draget in från Del 20)

Del 20 i roadmapen är egentligen "flytta hemligheter till Key Vault" —
men du har redan ett Key Vault i prenumerationen, så det finns ingen
anledning att först bygga den osäkra mellanversionen (nyckeln som en vanlig
Application Setting, i klartext) bara för att riva upp den igen när Del 20
formellt kommer. Nyckeln går till valvet direkt här i Del 8 istället; Del 9:s
Azure SQL-connection string ansluter sig till samma mönster när den landar
— se `Program.cs`s kommentar ovanför Key Vault-blocket.

Lägg först till de två paketen som låter appen läsa från Key Vault. Jag har
medvetet inte skrivit in ett specifikt versionsnummer i `.csproj`-filen —
den här sessionen har ingen pålitlig internetåtkomst mot NuGet just nu för
att verifiera vilken version som faktiskt är aktuell, och att gissa fel
version är värre än att låta `dotnet add package` slå upp den självt:

```bash
cd src/Atlas.Api
dotnet add package Azure.Identity
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
cd ../..
```

Sedan själva Azure-sidan — byt `<namnet på ditt befintliga Key Vault>` mot
det riktiga namnet:

```bash
# Ny terminal sedan steg 1? Sätt om RG/WEBAPP_NAME också.
KEYVAULT_NAME=<namnet på ditt befintliga Key Vault>

# 1) System-assigned managed identity på App Service — det är så appen
#    autentiserar mot valvet. Ingen nyckel eller connection string behövs
#    för att komma åt hemligheterna; identiteten ÄR autentiseringen.
az webapp identity assign --name "$WEBAPP_NAME" --resource-group "$RG"
PRINCIPAL_ID=$(az webapp identity show --name "$WEBAPP_NAME" --resource-group "$RG" --query principalId -o tsv)

# Skriv alltid ut det du precis fångade i en variabel innan du använder den
# i nästa kommando — ett tomt värde här ger annars ett kommando med ett
# ofullständigt --scope/--assignee längre ner, och Azure svarar med ett
# kryptiskt fel (t.ex. "MissingSubscription") som inte alls pekar på att
# variabeln var tom.
echo "PRINCIPAL_ID=$PRINCIPAL_ID"   # ska vara ett GUID, inte tomt

# 2) Läsrättighet för den identiteten. Vilket kommando som gäller beror på
#    vilken auktoriseringsmodell ditt befintliga valv använder — kolla:
az keyvault show --name "$KEYVAULT_NAME" --query properties.enableRbacAuthorization -o tsv
```

```bash
# "true" -> RBAC-modellen. Fångar valvets resurs-id i en egen variabel och
# skriver ut den FÖRST — samma anledning som ovan: en tom
# kommandosubstitution rätt in i --scope är exakt vad som ger
# "(MissingSubscription) The request did not have a subscription..." — ARM
# tolkar en tom/ofullständig scope-sträng som att /subscriptions/-delen av
# URL:en saknas helt, vilket är precis vad felet säger, bokstavligen.
VAULT_ID=$(az keyvault show --name "$KEYVAULT_NAME" --query id -o tsv)
echo "VAULT_ID=$VAULT_ID"   # ska se ut som /subscriptions/.../vaults/<namn>

# Git Bash (MINGW64)? Om du fick (MissingSubscription) här trots att
# VAULT_ID ovan ser helt rätt ut: se Git Bash-notisen i avsnitt 0 —
# `export MSYS_NO_PATHCONV=1` och kör om kommandot.
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee "$PRINCIPAL_ID" \
  --scope "$VAULT_ID"
```

```bash
# "false" -> klassiska access policies:
az keyvault set-policy \
  --name "$KEYVAULT_NAME" \
  --object-id "$PRINCIPAL_ID" \
  --secret-permissions get list
```

```bash
# 3) Själva hemligheten. Notera -- (dubbelt bindestreck), INTE __ (dubbelt
#    understreck) som för Application Settings — Key Vault-namn får bara
#    innehålla bokstäver, siffror och bindestreck; understreck är inte
#    tillåtna alls i ett Key Vault-secretnamn. Config-biblioteket vet att
#    göra om -- till : (Jwt--SigningKey -> config-nyckeln Jwt:SigningKey),
#    på exakt samma sätt som __ görs om till : för miljövariabler.
az keyvault secret set \
  --vault-name "$KEYVAULT_NAME" \
  --name "Jwt--SigningKey" \
  --value "$(openssl rand -base64 48)"

# 4) Säg åt appen vilket valv den ska fråga. Det är inte hemligt i sig
#    (bara ett namn) så det är en vanlig Application Setting, precis som
#    Jwt__Issuer ovan — inte en Key Vault-hemlighet.
az webapp config appsettings set \
  --name "$WEBAPP_NAME" \
  --resource-group "$RG" \
  --settings KeyVault__Name="$KEYVAULT_NAME"
```

## 4. Health check

Portalen: din Web App → **Monitoring → Health check** → aktivera, sökväg
`/health`. Det pekar mot `MapHealthChecks("/health")` i `Program.cs`, som
medvetet inte är beroende av databasen (se kommentaren där för varför) —
så det här fungerar redan efter steg 1–3, innan Del 9 finns.

## 5. Azure Pipelines: lösenordsfri inloggning (workload identity federation)

Samma idé som OIDC för GitHub Actions — ingen klienthemlighet lagras någonstans,
pipelinen växlar en tillfällig token mot ett Azure AD-token vid varje körning —
men Azure DevOps är ett **förstapartssystem** ur Azures perspektiv (till
skillnad från GitHub, som Azure bara litar på efter att du manuellt registrerat
förtroendet via `az ad app federated-credential create`, som i den tidigare
GitHub-versionen av det här avsnittet). Det gör att hela uppsättningen görs i
Azure DevOps-portalen istället för via `az`-kommandon: du kör i praktiken
`az ad app create` + `az ad sp create` + `az role assignment create` +
`az ad app federated-credential create` från förra avsnittets GitHub-flöde,
men som EN guidad wizard som gör allt åt dig.

**Vad du behöver:** tillräckliga Entra ID-rättigheter för att skapa en app-
registrering (antingen rollen *Application Administrator* i Entra ID, eller
att du är *Owner* på prenumerationen — de flesta personliga/test-prenumerationer,
som din, uppfyller det automatiskt eftersom du är den som skapade dem). Om
steget nedan misslyckas med ett behörighetsfel är det nästan alltid det här.

1. I Azure DevOps: **Project Settings** (nere till vänster) →
   **Service connections** → **New service connection** → **Azure Resource
   Manager** → **Workload Identity federation (automatic)**.
2. Scope level: **Resource Group** (inte Subscription) — samma
   minsta-möjliga-behörighet-princip som Contributor-rollen hade i GitHub-
   versionen: en läckt eller felkonfigurerad pipeline kan då som mest skada
   det här ena projektets resurser, inte hela prenumerationen. Välj din
   prenumeration (`PetersSubscriptionForTest`) och resursgrupp
   (`$RG`, dvs. `rg-projectatlas-dev-sc`).
3. Ge den ett namn du känner igen och spara. Peters faktiska service
   connection i det här projektet heter `project-atlas-connectionname` — vad
   du än väljer att kalla din, det namnet är det enda pipelinen behöver
   referera till. Azure DevOps lagrar och hanterar App Registration, service
   principal *och* det federerade förtroendet bakom den namngivna service
   connection-posten. Inga client-id/tenant-id/subscription-id-värden att
   kopiera någonstans, till skillnad från GitHub-flödet.
4. **Pipelines** → **New pipeline** → **Azure Repos Git** → välj ditt repo →
   **Existing Azure Pipelines YAML file** → `/azure-pipelines.yml` (filen
   som redan ligger i repots rot, se nedan) → **Save** (kör inte än om du
   vill dubbelkolla service connection-namnet i filen först).

`azure-pipelines.yml` i repots rot refererar till service connection-namnet
från steg 3 via variabeln `azureServiceConnection` överst i filen — öppna den
och sätt den till exakt det namn du valde. Ett första körningsförsök gav en
konkret påminnelse om varför det här steget är värt att dubbelkolla: filen
låg kvar med en generisk platshållare (`sc-projectatlas-dev-sc`) från när
Del 8 skrevs, medan service connection-posten i Azure DevOps faktiskt döptes
till `project-atlas-connectionname` — Deploy-stadiet failade tills variabeln
uppdaterades till att matcha den verkliga posten. Namnet i pipelinen och
namnet i Azure DevOps måste vara exakt lika, tecken för tecken:

```yaml
variables:
  azureServiceConnection: 'project-atlas-connectionname'   # namnet från steg 3
  webAppName: 'app-projectatlas-dev-sc'
```

## 6. Verifiera

Pusha till `main` (eller kör pipelinen manuellt via **Pipelines**-fliken →
välj pipelinen → **Run pipeline**), följ körningen där, och kontrollera
sedan:

```bash
curl "https://$WEBAPP_NAME.azurewebsites.net/health"
# förväntat: "Healthy" (200 OK) — fungerar oavsett databas

curl "https://$WEBAPP_NAME.azurewebsites.net/openapi/v1.json"
# förväntat: ett OpenAPI-dokument, om EnableSwaggerUi=true — annars 404
```

Att `/api/auth/login` eller `/api/tickets` svarar med ett databasfel just nu
är väntat (se ingressen ovan) — det är precis den biten Del 9 stänger.

## 7. Del 9: Azure SQL — koppla in din befintliga server

Del 9:s enda egentliga jobb är att ge `ConnectionStrings:AtlasDb` ett
riktigt värde i molnet — allt annat (Key Vault-kopplingen, managed
identity-autentiseringen, App Service) finns redan på plats sedan Del 8.
Du har redan en Azure SQL-server, så vi skapar bara en ny, tom databas på
den för Atlas — samma "återanvänd resursen, håll projektets data för sig
själv"-princip som Key Vault i avsnitt 3.

En sak värd att känna till innan du börjar: `DependencyInjection.cs` har
redan `sqlOptions.EnableRetryOnFailure(...)` inställt på `AtlasDbContext`
— det är inte nytt för Del 9, det stod där redan från Fas 1, som
förberedelse för just det här. Transienta nätverksfel (en kort omkoppling,
en serverless-databas som vaknar från auto-pause) är normalt för Azure SQL
på ett sätt de aldrig är mot en lokal SQL Server-instans, och utan
automatisk omförsök hade en helt frisk databas kunnat ge sporadiska 500:or.

```bash
# Ny terminal sedan tidigare avsnitt? Sätt om RG/WEBAPP_NAME/KEYVAULT_NAME också.
SQL_SERVER_NAME=<namnet på din befintliga SQL-server, utan .database.windows.net>
SQL_RG=<resursgruppen där den servern faktiskt ligger>
SQL_DB_NAME=sqldb-projectatlas-dev-sc
```

### 7.1 Skapa databasen

Serverless General Purpose med Azures "free limit": 100 000 vCore-sekunder
och 32 GB lagring gratis per månad, upp till tio sådana databaser per
prenumeration. Auto-pause gör att den pausar sig själv (och slutar
debitera beräkningskraft) efter en period av inaktivitet — passar ett
portfolioprojekt som inte körs kontinuerligt precis.

```bash
az sql db create \
  --resource-group "$SQL_RG" \
  --server "$SQL_SERVER_NAME" \
  --name "$SQL_DB_NAME" \
  --edition GeneralPurpose \
  --family Gen5 \
  --capacity 2 \
  --compute-model Serverless \
  --auto-pause-delay 60 \
  --use-free-limit \
  --free-limit-exhaustion-behavior AutoPause
```

Redan använt gratiskvoten på den här prenumerationen (t.ex. av en annan
databas)? Ta bort de tre sista flaggorna (`--use-free-limit` osv.) och byt
`--edition GeneralPurpose ... --compute-model Serverless`-raderna mot
`--service-objective Basic` — enklaste betalda alternativet, runt
$5/månad, ingen auto-pause men förutsägbart och billigt.

### 7.2 Brandvägg

App Service måste komma in, och (tillfälligt) din egen dator för att köra
migrationerna i nästa steg.

```bash
az sql server firewall-rule create \
  --resource-group "$SQL_RG" \
  --server "$SQL_SERVER_NAME" \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

`0.0.0.0`–`0.0.0.0` är inte "alla IP-adresser" trots hur det ser ut — det
är Azures dokumenterade specialvärde för just "tillåt andra Azure-tjänster
i samma prenumeration", inte ett brandväggshål mot hela internet.

Lägg till din egen dators IP via **portalen** istället för CLI:t här —
enklast och minst risk för fel: din SQL-server → **Networking** → **Add
your client IPv4 address** → Save. Du behöver den bara tillfälligt för
nästa steg; ta gärna bort regeln igen efteråt om du vill hålla brandväggen
så snäv som möjligt.

### 7.3 Kör migrationerna mot Azure SQL

**Kör bara det här — kör aldrig `sql/001_InitialSchema.sql` mot Azure SQL,
trots att det kan kännas som en genväg.** Den filen är en handskriven
referens, inte en körbar sanning, och den drev genuint isär från EF-
modellen en gång (saknade `PasswordHash` efter Del 5, ingen märkte det
förrän den råkade köras mot en skarp databas — se `ARCHITECTURE.md`). Bara
`dotnet ef database update` nedan garanterar ett schema som faktiskt
matchar koden.

Sätt anslutningssträngen som en miljövariabel för just den här
terminalsessionen — aldrig i en committad fil — kör migrationerna, och
nollställ den direkt efteråt så att ett vanligt `dotnet run` efteråt inte
råkar peka mot Azure SQL av misstag:

```bash
# Byt ut <admin-login> och <losenord> mot din serveradmins riktiga
# inloggning (den du redan har sedan servern skapades). Enkla citattecken
# runt HELA strängen skyddar mot att Bash tolkar tecken som ; $ ! i
# lösenordet som något annat än bokstäver.
export ConnectionStrings__AtlasDb='Server=tcp:'"$SQL_SERVER_NAME"'.database.windows.net,1433;Initial Catalog='"$SQL_DB_NAME"';User ID=<admin-login>;Password=<losenord>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

# Ny terminal sedan avsnitt 7 började? $SQL_SERVER_NAME är då tom, och du
# får en anslutningssträng som "Server=tcp:.database.windows.net,..." — ett
# värdnamn som inte går att slå upp alls ("No such host is known"). Kolla
# alltid innan du litar på den:
echo "ConnectionStrings__AtlasDb=$ConnectionStrings__AtlasDb"

cd src/Atlas.Api
dotnet ef database update --project ../Atlas.Infrastructure --startup-project .
cd ../..

unset ConnectionStrings__AtlasDb
```

`ConnectionStrings__AtlasDb` (dubbelt understreck, inte dubbelt
bindestreck — det är en miljövariabel, inte ett Key Vault-secretnamn) läses
automatiskt av `AtlasDbContextFactory` via `.AddEnvironmentVariables()`,
och vinner över `appsettings.Development.json` så länge variabeln är satt
— exakt samma konfigurationsmekanik som Key Vault-hemligheterna använder,
bara en annan källa.

### 7.4 Spara anslutningssträngen i Key Vault

Samma mönster som `Jwt--SigningKey` i avsnitt 3 — dubbelt bindestreck,
och ingen ny App Setting behövs, eftersom `KeyVault:Name` redan pekar App
Service mot rätt valv sedan Del 8:

```bash
az keyvault secret set \
  --vault-name "$KEYVAULT_NAME" \
  --name "ConnectionStrings--AtlasDb" \
  --value 'Server=tcp:'"$SQL_SERVER_NAME"'.database.windows.net,1433;Initial Catalog='"$SQL_DB_NAME"';User ID=<admin-login>;Password=<losenord>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

az webapp restart --name "$WEBAPP_NAME" --resource-group "$RG"
```

Omstarten är inte strikt nödvändig (App Service läser Key Vault-hemligheter
vid uppstart ändå), men gör att du inte behöver vänta på nästa naturliga
omstart för att verifiera direkt.

**En medveten genväg värd att känna till:** det här återanvänder din
SQL-servers admin-inloggning i Key Vault-hemligheten, i stället för att
skapa en separat, snävare SQL-inloggning med rättigheter enbart mot
`$SQL_DB_NAME` (samma minsta-möjliga-behörighet-princip som RBAC-rollen i
avsnitt 3 följer). Att skapa en sådan inloggning kräver att köra `CREATE
LOGIN`/`CREATE USER`-T-SQL mot servern via ett klientverktyg (t.ex.
portalens Query Editor, se 7.5) snarare än `az`, vilket är fullt görbart
men ett eget litet steg — ett bra kandidat-städjobb för Del 20 när den
generaliserar hela Key Vault-uppsättningen.

### 7.5 Seeda den nya databasen och verifiera

Migrationerna skapar schemat, men den nya databasen är helt tom — samma
kluck-och-ägg-problem som lokal dev hade (se README): att registrera en
användare kräver ett giltigt `organizationId`, men det första organization-
id:t måste in via SQL. Enklast utan att installera något lokalt: din
SQL-databas i portalen → **Query editor (preview)** → logga in med
admin-inloggningen → klistra in och kör `sql/002_SeedData.sql`.

`002_SeedData.sql` genererar id:na med `NEWID()`, så de är olika varje gång
— det finns inget fast värde att kopiera från den här texten. Kör den här
frågan i samma Query editor direkt efteråt för att hämta det riktiga
`organizationId`:t:

```sql
SELECT Id, Name FROM dbo.Organizations WHERE Name = N'Northstar IT';
```

Sedan, mot App Service (inte längre lokalt):

```bash
curl "https://$WEBAPP_NAME.azurewebsites.net/api/auth/register" -X POST \
  -H "Content-Type: application/json" \
  -d '{"email":"demo@example.com","password":"Test1234!","organizationId":"<organizationId från seed-datan>","role":"Admin"}'
```

Ett 200/201-svar med en JWT i svaret betyder att hela kedjan fungerar
end-to-end: App Service → Key Vault → managed identity → Azure SQL. Det är
den riktiga bekräftelsen på att Del 9 är klar — mer talande än vilken
enskild logg som helst, av samma anledning som `/health` var det för
Del 8.

## 8. Del 10: Azure Blob Storage — filuppladdning för Attachments

Samma grundmönster en tredje gång: koppla in en Azure-resurs och autentisera
App Service mot den via samma managed identity som redan pratar med Key
Vault (avsnitt 3) och Azure SQL (avsnitt 7), i stället för ännu en lagrad
hemlighet. Till skillnad från de två föregående valde du (till skillnad
från den befintliga SQL-servern) att skapa ett **nytt** Storage-konto här
snarare än att återanvända ett befintligt — enklare att resonera om vilka
behörigheter kontot faktiskt behöver när det bara innehåller det här
projektets bilagor.

Koden är redan skriven och pushad till repot: `IBlobStorageService`
(Atlas.Application), `AzureBlobStorageService` (Atlas.Infrastructure) och de
två nya endpointerna på `TicketsController`
(`POST .../attachments`, `GET .../attachments/{id}/download`) — se
`DependencyInjection.cs`s "Blob Storage"-block för hela resonemanget bakom
den dubbla lokal/molnvägen. Det här avsnittet är bara Azure-sidan; testa
lokalt med Azurite (README steg 4) innan du deployar, om du inte redan gjort
det.

**Ett fynd värt att nämna innan du börjar:** när jag skrev om koden märkte
jag att `Atlas.Api.csproj` i molnspegeln aldrig fick `Azure.Identity` och
`Azure.Extensions.AspNetCore.Configuration.Secrets` tillagda som explicita
`PackageReference`, trots att `Program.cs` använder båda sedan Del 8 (du
körde `dotnet add package` direkt på din maskin då, vilket uppdaterar din
riktiga `.csproj` men inte spegeln jag jobbar mot här). Jag har lagt till
dem nu (version 1.21.0 respektive 1.5.2, de senaste stabila enligt NuGet
just nu) tillsammans med `Azure.Storage.Blobs` (12.29.2) i
`Atlas.Infrastructure.csproj`. Om din maskin redan har andra versioner
installerade löser `dotnet restore` det mesta av sig själv, men säg till om
du får en versionskonflikt så pinnar vi om till exakt det du redan har.

```bash
# Ny terminal sedan tidigare avsnitt? Sätt om RG/WEBAPP_NAME/KEYVAULT_NAME/
# PRINCIPAL_ID också (PRINCIPAL_ID sattes i avsnitt 3 — samma App Service,
# samma identitet, återanvänds rakt av här).
STORAGE_ACCOUNT_NAME=<ett globalt unikt namn, t.ex. stprojectatlasdevsc — bara gemener/siffror, 3-24 tecken>
STORAGE_RG="$RG"
```

### 8.1 Skapa Storage-kontot och containern

Ett vanligt General Purpose v2-konto med lokal redundans (LRS, billigast,
gott nog för ett portfolioprojekt — ingen anledning att betala för
geo-replikering av testfiler) och `--allow-blob-public-access false`, så
att containerns egen `PublicAccessType.None` (satt i koden) inte kan
undermineras av kontots inställningar:

```bash
az storage account create \
  --name "$STORAGE_ACCOUNT_NAME" \
  --resource-group "$STORAGE_RG" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --allow-blob-public-access false \
  --min-tls-version TLS1_2
```

Containern (`attachments`, samma namn koden defaultar till) skapas inte
här via `az` — `AddInfrastructure` skapar den själv vid uppstart
(`CreateIfNotExists`, se `DependencyInjection.cs`), exakt samma
"appen säkerställer sitt eget schema/sina egna resurser vid start"-idé som
`dotnet ef database update` för tabeller. Det betyder också att App Service
behöver skrivrättighet mot kontot redan vid första starten efter den här
sektionen — se 8.2 innan du sätter App Setting i 8.3, annars kraschar
appen vid uppstart med ett auktoriseringsfel (samma sorts "fail fast, tydligt
fel" som en saknad `Jwt:SigningKey` ger, se `Program.cs`).

### 8.2 Ge App Service rättighet till kontot

```bash
STORAGE_ACCOUNT_ID=$(az storage account show --name "$STORAGE_ACCOUNT_NAME" --resource-group "$STORAGE_RG" --query id -o tsv)
echo "STORAGE_ACCOUNT_ID=$STORAGE_ACCOUNT_ID"   # ska se ut som /subscriptions/.../storageAccounts/<namn>

az role assignment create \
  --role "Storage Blob Data Contributor" \
  --assignee "$PRINCIPAL_ID" \
  --scope "$STORAGE_ACCOUNT_ID"
```

"Storage Blob Data Contributor" (inte bara "Reader" eller kontots klassiska
access keys) ger läs+skriv+radera på blob-nivå via Azure AD-identiteten
själv — samma RBAC-baserade, nyckelfria mönster som "Key Vault Secrets
User" i avsnitt 3.

### 8.3 Peka App Service mot kontot

Kontots URL är, precis som `KeyVault:Name`, inte i sig hemlig — att känna
till adressen ger ingen åtkomst utan en identitet Azure litar på, så det
är en vanlig Application Setting, inte en Key Vault-hemlighet:

```bash
az webapp config appsettings set \
  --name "$WEBAPP_NAME" \
  --resource-group "$RG" \
  --settings BlobStorage__AccountUrl="https://$STORAGE_ACCOUNT_NAME.blob.core.windows.net"

az webapp restart --name "$WEBAPP_NAME" --resource-group "$RG"
```

### 8.4 Verifiera

```bash
TOKEN="<en giltig JWT — se avsnitt 7.5 för hur du loggar in mot App Service>"
TICKET_ID="<ett riktigt ticket-id — skapa ett via POST /api/tickets om du inte redan har ett>"

curl -X POST "https://$WEBAPP_NAME.azurewebsites.net/api/tickets/$TICKET_ID/attachments" \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@screenshot.png"
```

Ett 201-svar med attachment-metadata (id, filnamn, storlek) betyder att
hela kedjan fungerar: App Service → managed identity → Storage-kontot.
Hämta filen tillbaka med `id`:t från svaret ovan:

```bash
curl "https://$WEBAPP_NAME.azurewebsites.net/api/tickets/$TICKET_ID/attachments/<attachment-id>/download" \
  -H "Authorization: Bearer $TOKEN" \
  -o downloaded-screenshot.png
```

## 9. Del 12: Azure Service Bus — molnsidan (öppen, se nedan)

**Det här avsnittet skiljer sig från 3/7/8 ovan på ett viktigt sätt:** de
stegen är redan utförda och verifierade mot din skarpa prenumeration.
Service Bus-molnsidan för Del 12 är det *inte* — den står som en öppen punkt
i `docs/ARCHITECTURE.md`s "Current known simplifications". Det här avsnittet
är alltså en checklista att följa **när** du är redo, inte en logg över vad
som redan hänt, till skillnad från resten av den här filen.

Anledningen att det ändå finns något kvar att göra här, trots att Del 12 är
klar och verifierad: till skillnad från SQL (avsnitt 7) och Blob Storage
(avsnitt 8) — där lokal utveckling pratar mot en *emulator* (LocalDB,
Azurite) och Azure-resursen provisioneras separat, här, som ett eget steg —
har Service Bus ingen emulator alls. Del 12 löste det genom att låta lokal
utveckling prata mot **samma riktiga Azure-namespace** produktionen skulle
använda (se README steg 4 och `AddMessaging`s doc comment). Namespacet,
topicet (`atlas-ticket-events`) och subscriptionen (`atlas-notifications`)
finns alltså redan i Azure — skapade under lokal utveckling, inte som ett
separat "deploya till Azure"-steg. Det som saknas är att ge den
**driftsatta** `Atlas.Api`s egen managed identity samma sorts behörighet
Peters `az login`-identitet redan har lokalt.

### 9.1 Ge App Service (Atlas.Api) rättighet till Service Bus-namespacet

```bash
# Ny terminal? Sätt om RG/WEBAPP_NAME/PRINCIPAL_ID från avsnitt 1/3 igen —
# PRINCIPAL_ID är samma App Service-identitet som redan används i 3.x/8.2.
SERVICEBUS_NAMESPACE="nspl-sb-core-dev-sc"   # samma namespace README steg 4 redan pekar lokal utveckling mot
SERVICEBUS_RG=<resursgruppen namespacet ligger i>

SERVICEBUS_NAMESPACE_ID=$(az servicebus namespace show --resource-group "$SERVICEBUS_RG" --name "$SERVICEBUS_NAMESPACE" --query id -o tsv)
echo "SERVICEBUS_NAMESPACE_ID=$SERVICEBUS_NAMESPACE_ID"

az role assignment create \
  --role "Azure Service Bus Data Sender" \
  --assignee "$PRINCIPAL_ID" \
  --scope "$SERVICEBUS_NAMESPACE_ID"
```

"Data **Sender**", inte "Data Owner" (rollen Peters egna identitet har
lokalt för att både kunna skicka och ta emot under test): den driftsatta
`Atlas.Api` bara publicerar, den ska aldrig behöva ta emot eller
administrera ett namespace den inte konsumerar från — minsta-privilegium,
samma tanke som "Storage Blob Data Contributor" i 8.2 snävare än en
kontonyckel hade varit.

`ServiceBus:FullyQualifiedNamespace` behöver också sättas som en App
Setting — precis som `BlobStorage__AccountUrl` i 8.3 är den ett värde, inte
en hemlighet (att känna till ett namespace-hostnamn ger ingen åtkomst utan
en identitet Azure litar på), så den hör hemma som en vanlig Application
Setting, inte i Key Vault:

```bash
az webapp config appsettings set \
  --name "$WEBAPP_NAME" \
  --resource-group "$RG" \
  --settings ServiceBus__FullyQualifiedNamespace="$SERVICEBUS_NAMESPACE.servicebus.windows.net"

az webapp restart --name "$WEBAPP_NAME" --resource-group "$RG"
```

### 9.2 Verifiera (delvis)

Eftersom `Atlas.Worker` — mottagarsidan — inte är driftsatt i Azure än (se
docs/ARCHITECTURE.md: en medvetet öppen fråga om Container App Job, WebJob,
Functions med timer-trigger, eller en egen App Service), går det **inte**
att göra en fullständig rundturs-verifiering som 7.5/8.4 fick — det finns
ingen konsument i molnet ännu som kan bekräfta att ett meddelande faktiskt
plockades upp. Det som går att verifiera nu är bara att den driftsatta
`Atlas.Api` lyckas publicera utan fel:

```bash
TOKEN="<en giltig JWT mot App Service — se 7.5 för hur du loggar in>"
TICKET_ID="<ett riktigt ticket-id i molnet>"

curl -i -X POST "https://$WEBAPP_NAME.azurewebsites.net/api/tickets/$TICKET_ID/assign" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"userId":"<en riktig user-guid>"}'
```

Ett `200 OK` plus frånvaron av en `Azure.Messaging.ServiceBus`-relaterad
`LogWarning` i `az webapp log tail` (publish-anropet loggar bara en varning
vid fel, kastar aldrig ett fel till klienten — se
`ServiceBusTicketEventPublisher`) är den bekräftelse som finns tillgänglig
just nu. Meddelandet hamnar i `atlas-ticket-events`-topicet, men ingen
konsument i Azure läser det förrän `Atlas.Worker` faktiskt driftsätts dit.

## 10. Del 13: Redis — Azure Cache for Redis (stängd av Del 19, se nedan)

**Uppdatering, Del 19 (2026-09-14): det här avsnittets manuella `az`-väg är
inte längre den som faktiskt användes.** En riktig Redis-instans finns nu
provisionerad — via `infra/main.bicep`, inte via kommandona nedan — se
avsnitt 12. Ett genuint fynd på vägen dit, värt att känna till innan du
någonsin kör kommandona i det här avsnittet för hand: **klassiska Azure
Cache for Redis (resurstypen `az redis create` nedan skapar) håller på att
fasas ut** — ett första försök att deploya exakt den resurstypen via Bicep
gav felet "Azure Cache for Redis is retiring, create Azure Managed Redis
instance instead." Kommandona nedan är kvar oförändrade som referens (de
fungerade fram tills nyligen, och kan fortfarande fungera i vissa
prenumerationer under en övergångsperiod), men avsnitt 12 är den väg som
faktiskt användes och är värd att följa istället.

Ursprungligt resonemang, oförändrat: det här är samma sak som avsnitt 9 —
en plan snarare än en logg, till dess Del 19 kom och faktiskt körde den.
Del 13 (cache-aside-logiken i C#) är sedan tidigare klar och verifierad
**lokalt**, mot en Docker-container (se README och
`scripts/test-del13-redis-cache.sh`), exakt samma "riktig lokal motsvarighet
i stället för en emulator eller produktion självt"-idé som LocalDB och
Azurite redan följer (och som Service Bus i avsnitt 9 ovan, ovanligt nog,
*inte* kunde följa).

### 10.1 Skapa en Azure Cache for Redis-instans

```bash
REDIS_NAME=<ett globalt unikt namn, t.ex. redis-projectatlas-dev-sc>

az redis create \
  --name "$REDIS_NAME" \
  --resource-group "$RG" \
  --location swedencentral \
  --sku Basic \
  --vm-size c0
```

`Basic`/`C0` (billigast, ingen SLA, ingen replikering) räcker gott för ett
portfolioprojekts cache-lager — precis samma "billigast som fortfarande
bevisar mönstret" avvägning som `Standard_LRS` fick för Storage-kontot i
8.1. Etableringen tar typiskt 15–20 minuter, ovanligt långsamt jämfört med
resten av den här filens `az`-kommandon — vänta ut den (`az redis show
--name "$REDIS_NAME" --resource-group "$RG" --query provisioningState`
växlar från `Creating` till `Succeeded`) innan nästa steg.

### 10.2 Peka App Service mot instansen

Till skillnad från Key Vault, Azure SQL, Blob Storage och Service Bus ovan
finns det **ingen managed-identity/RBAC-väg för klassisk (icke-Enterprise)
Azure Cache for Redis** — bara nyckelbaserad autentisering. Det är därför
`Redis:ConnectionString` (till skillnad från `BlobStorage:AccountUrl`/
`ServiceBus:FullyQualifiedNamespace`) faktiskt innehåller en hemlighet i
molnet, och hör hemma i Key Vault, inte som en vanlig Application Setting —
se den dokumenterade luckan i `docs/ARCHITECTURE.md` (en Del 20-kandidat,
tillsammans med den återanvända SQL-admin-inloggningen).

```bash
REDIS_KEY=$(az redis list-keys --name "$REDIS_NAME" --resource-group "$RG" --query primaryKey -o tsv)
REDIS_CONNECTION_STRING="$REDIS_NAME.redis.cache.windows.net:6380,password=$REDIS_KEY,ssl=True,abortConnect=False"

az keyvault secret set \
  --vault-name "$KEYVAULT_NAME" \
  --name "Redis--ConnectionString" \
  --value "$REDIS_CONNECTION_STRING"

az webapp restart --name "$WEBAPP_NAME" --resource-group "$RG"
```

(`abortConnect=False` är StackExchange.Redis-specifikt, inte en Azure-grej:
det säger åt klienten att köa anrop och försöka återansluta istället för att
kasta ett undantag direkt om anslutningen inte är uppe i exakt det ögonblick
en request kommer in — rimligt för en cache vars hela poäng är att vara
valfri, se `RedisTicketStatsCache`s "fail open"-resonemang.)

### 10.3 Verifiera

```bash
TOKEN="<en giltig JWT mot App Service>"

curl -s "https://$WEBAPP_NAME.azurewebsites.net/api/tickets/stats" \
  -H "Authorization: Bearer $TOKEN"
```

Anropa det två gånger snabbt efter varandra och jämför `generatedAtUtc` i
svaret — identiskt värde betyder att den driftsatta `Atlas.Api` faktiskt
cachar mot den riktiga Azure Cache for Redis-instansen, inte bara att
endpointen svarar. Samma metodik som `scripts/test-del13-redis-cache.sh`
redan använder lokalt, bara mot `$WEBAPP_NAME` istället för `localhost`.

## 11. Del 14: Application Insights (stängd av Del 19, se nedan)

**Uppdatering, Del 19 (2026-09-14): samma sak som avsnitt 10 ovan — det
här avsnittets manuella `az`-väg är inte längre den som faktiskt
användes.** En riktig Application Insights-resurs (workspace-baserad, med
ett eget Log Analytics-workspace) finns nu provisionerad via
`infra/main.bicep` — se avsnitt 12. Kommandona nedan är kvar oförändrade
som referens och fungerar fortfarande utmärkt om du någon gång vill skapa
en till Application Insights-resurs för hand.

Ursprungligt resonemang, oförändrat: samma sak som avsnitt 9 och 10 — en
plan snarare än en logg, till dess Del 19 kom och faktiskt körde den. Del
14 (Serilog + valfritt Application Insights-sink, se `docs/ARCHITECTURE.md`s
Del 14-avsnitt) är sedan tidigare klar och verifierad **lokalt** via
`scripts/test-del14-logging.sh` — men bara med en påhittad
`ApplicationInsights:ConnectionString` som bevisar att koden inte kraschar
när sinket slås på; huruvida telemetri faktiskt når den nu riktiga
Azure-resursen är fortfarande overifierat (se avsnitt 12).

### 11.1 Skapa en Application Insights-resurs

```bash
# Ny terminal sedan tidigare avsnitt? Sätt om RG också.
APPINSIGHTS_NAME=appi-projectatlas-dev-sc

az monitor app-insights component create \
  --app "$APPINSIGHTS_NAME" \
  --location swedencentral \
  --resource-group "$RG" \
  --application-type web
```

(Kräver Azure CLI-tillägget `application-insights` — `az extension add
--name application-insights` om kommandot ovan säger att det inte
hittas.)

### 11.2 Peka App Service mot resursen

Till skillnad från Redis anslutningssträng (avsnitt 10.2) är en Application
Insights-**connection string** inte en hemlighet i samma mening som en
databas- eller cache-lösenord: den ger ingen läsbehörighet till något Atlas
själv skyddar, bara skrivbehörighet att skicka *ny* telemetri till just den
här resursen — värsta tänkbara missbruk är någon som skickar in skräptelemetri,
inte ett dataintrång. Samma resonemang som `BlobStorage:AccountUrl` (avsnitt
8.3) och `ServiceBus:FullyQualifiedNamespace` (avsnitt 9.1): en vanlig
Application Setting, inte Key Vault.

```bash
APPINSIGHTS_CONNECTION_STRING=$(az monitor app-insights component show \
  --app "$APPINSIGHTS_NAME" --resource-group "$RG" \
  --query connectionString -o tsv)
echo "APPINSIGHTS_CONNECTION_STRING=$APPINSIGHTS_CONNECTION_STRING"   # ska inte vara tom

az webapp config appsettings set \
  --name "$WEBAPP_NAME" \
  --resource-group "$RG" \
  --settings ApplicationInsights__ConnectionString="$APPINSIGHTS_CONNECTION_STRING"

az webapp restart --name "$WEBAPP_NAME" --resource-group "$RG"
```

Ingen kodändring behövs för det här steget — `Program.cs`s
`UseSerilog`-block läser redan `ApplicationInsights:ConnectionString` ur
konfigurationen och kopplar bara in Application Insights-sinket när värdet
faktiskt är satt (se `docs/ARCHITECTURE.md`). Att sätta App Setting:en ovan
och starta om App Service är hela driftsättningen.

### 11.3 Verifiera

```bash
curl "https://$WEBAPP_NAME.azurewebsites.net/health"
# generera lite trafik att leta efter i Application Insights
```

I Azure-portalen: din Application Insights-resurs → **Live metrics** (näst
intill omedelbart, bra för att bekräfta att telemetri över huvud taget
kommer in) eller **Logs** → en enkel KQL-fråga som `traces | order by
timestamp desc | take 20` för att se Serilogs egna loggrader (inte bara
ASP.NET Core:s inbyggda request-telemetri, som skulle synas där även utan
Serilog-sinket). En rad som matchar en riktig `_logger.LogInformation`/
`LogWarning`-anrop i koden (t.ex. "User {UserId} logged in" efter en
inloggning mot den driftsatta appen) är den faktiska bekräftelsen på att
hela kedjan — Serilog → sinket → Application Insights-resursen — fungerar,
samma "ett konkret, verkligt anrop är mer övertygande än att bara läsa
koden"-princip som `/health` var för Del 8 och en lyckad `stats`-cache-träff
var för Del 13.

## Del 15: Audit Log — inget nytt avsnitt behövs här

Del 15 (systemomfattande `AuditLog`) är klar och bekräftad fungerande
lokalt, end-to-end (se `docs/ARCHITECTURE.md`s Del 15-avsnitt och
`scripts/test-del15-audit-log.sh`) — men den får medvetet **inget eget
numrerat avsnitt i den här filen**, till skillnad från Del 9/10/12/13 ovan.
Anledningen är själva poängen: `AuditLogs` är bara ännu en tabell i samma
Azure SQL-databas som Del 9 redan satte upp (avsnitt 7 ovan) — ingen ny
Azure-resurs att provisionera, ingen ny hemlighet att lägga i Key Vault,
ingen ny anslutningssträng eller managed-identity-roll att bevilja.
Driftsättningen av Del 15 *är* driftsättningen av Del 9: så fort en ny EF
Core-migrering med `AuditLogs`-tabellen körs mot samma Azure SQL-databas
(`dotnet ef database update` mot den, precis som för alla tidigare Del)
finns funktionen i molnet. Det är ett lika viktigt lärdomsmoment som alla de
Del som *fick* ett eget avsnitt här: inte varje Del i koden motsvarar ett
nytt steg i infrastrukturen.

Fas 4:s molnsida har därför fortfarande bara de två öppna punkterna Del 13
lämnade efter sig, oförändrade av Del 15 — *var* `Atlas.Worker` ska köras i
Azure (avsnitt 9 ovan), och att faktiskt provisionera en Azure Cache for
Redis-instans (avsnitt 10 ovan). Fas 3s enda kvarvarande punkt är
fortsatt Del 20 (generalisera Key Vault-uppsättningen, byta ut den
återanvända SQL-admin-inloggningen mot en snävare — nu med
Redis-anslutningssträngen som ytterligare en hemlighet den
generaliseringen får ta hand om). Fas 5 har nu sin första egna öppna
molnpunkt också: Del 14 (avsnitt 11 ovan) är klar och verifierad lokalt,
men väntar fortfarande på att en riktig Application Insights-resurs faktiskt
provisioneras — samma "kod klar, Azure-resurs kvar"-mönster som Del 12 och
Del 13 redan har.

## Del 16: Tester — inget nytt avsnitt behövs här, av ett annat skäl än Del 15

Del 16 (`Atlas.Application.Tests` + `Atlas.Api.IntegrationTests`, se
`docs/ARCHITECTURE.md`s Del 16-avsnitt) är klar och bekräftad fungerande
lokalt — 93 enhetstester och 19 integrationstester, alla gröna. Den får
medvetet **inget eget numrerat avsnitt här heller**, men av ett annat skäl
än Del 15 ovan: Del 15 rörde en riktig produktionstabell i samma Azure SQL-
databas allt annat redan använder, så "driftsättningen är redan gjord" var
den intressanta poängen. Del 16 rör sig aldrig i närheten av Azure
överhuvudtaget — `Atlas.Api.IntegrationTests` kör mot en egen, lokal
`AtlasDb_Test`-databas i LocalDB (se `appsettings.Testing.json`), inte mot
Azure SQL, och testerna själva deployas aldrig till App Service; de körs
bara av en utvecklare (eller CI, för den icke-integrationsdelen — se
`azure-pipelines.yml`s `--filter "Category!=Integration"`) före en push. Det
finns med andra ord ingenting här att provisionera, konfigurera eller
bevilja en roll för — testsviten är en kvalitetsgrind runt koden, inte en
ny molnresurs.

Fas 4:s och Fas 5:s öppna molnpunkter är därför exakt desamma som innan Del
16: *var* `Atlas.Worker` ska köras i Azure, en riktig Azure Cache for
Redis-instans, och en riktig Application Insights-resurs (avsnitt 9–11
ovan). Fas 5:s enda kvarvarande *kod*-punkt, i motsats till dess öppna
*molnpunkt*, var Del 17 (Docker) — se nästa avsnitt.

## Del 17: Docker — inget nytt avsnitt behövs här

Del 17 (`src/Atlas.Api/Dockerfile` + `docker-compose.yml`, se
`docs/ARCHITECTURE.md`s Del 17-avsnitt) är klar och bekräftad fungerande
end-to-end lokalt (2026-09-14) — men den får medvetet **inget eget
numrerat avsnitt här**, av samma grundskäl som Del 16 ovan: Del 17 rör sig
aldrig i närheten av Azure. `docker-compose.yml` startar sin egen
SQL Server-, Redis- och Azurite-container lokalt, exakt som steg 2/5/6 i
README.md:s "Getting started" redan gjorde en och en — det är samma
utvecklarmaskin, bara paketerad annorlunda. Inget nytt att provisionera,
ingen ny Key Vault-hemlighet, ingen ny managed-identity-roll.

Värt att vara tydlig med, eftersom det *låter* som ett driftsättningssteg:
en `Dockerfile` är i sig bara en byggritning, inte en Azure-resurs. Den
containeriserade avbildningen `docker compose up --build` bygger lokalt
körs aldrig i Azure av Del 17 — App Service (avsnitt 1–3 ovan) fortsätter
köra `Atlas.Api` precis som förut, direkt på Kudu/Oryx-plattformen, inte
via den här `Dockerfile`n. Den kopplingen kommer först med Del 18 (CI/CD):
frågan då blir om `azure-pipelines.yml` ska bygga *den här* `Dockerfile`n
och pusha resultatet till ett Azure Container Registry, och om App Service
i så fall byter från Oryx-baserad driftsättning till "Web App for
Containers" — ett medvetet öppet vägval, inte en brist i Del 17. Del 17:s
enda jobb var att bevisa att `Dockerfile`n och `docker-compose.yml` faktiskt
fungerar som en lokal utvecklarupplevelse; om/hur den återanvänds i Azure är
Del 18:s fråga att svara på.

Fas 5 är därmed helt klar (Del 14, Del 16 och Del 17, alla bekräftade
fungerande end-to-end lokalt) — se README.md:s Roadmap. Fas 4:s och Fas 3:s
öppna molnpunkter är oförändrade av Del 17: *var* `Atlas.Worker` ska köras i
Azure, en riktig Azure Cache for Redis-instans, en riktig Application
Insights-resurs, och Del 20:s Key Vault-generalisering (avsnitt 9–11 och
punkten om Fas 3 ovan).

## Del 18: CI/CD — ingen ny Azure-resurs, bara en pipelineutökning

Del 18 (`DockerBuild`-jobbet i `azure-pipelines.yml`s `BuildAndTest`-stadium,
se `docs/ARCHITECTURE.md`s Del 18-avsnitt) är klar och bekräftad fungerande
mot den skarpa pipelinen (2026-09-14). Precis som Del 17 får den medvetet
**inget eget numrerat provisioneringsavsnitt här** — men av ett tredje skäl,
skilt från både Del 15:s ("redan samma databas") och Del 16:s/Del 17:s
("rör sig aldrig i närheten av Azure"): Del 18 rör sig visserligen mitt i
den befintliga Azure-pipelinen från avsnitt 5, men *lägger inte till* någon
ny Azure-resurs där. `DockerBuild` kör `docker build` på Microsoft-hostade
`ubuntu-latest`-agenter — samma slags byggmaskin `Build`-jobbet redan kör
på — bygger avbildningen, och kastar bort den igen. Inget nytt Container
Registry, ingen ny roll, ingen ny hemlighet i Key Vault. Steg 4 ovan
("Pipelines → New pipeline → ... → Save") är fortfarande hela
etableringssteget — `azure-pipelines.yml` i repot är redan den fil som
gäller, och Del 18:s ändring i den filen kräver ingen ny åtgärd i Azure
DevOps portalen utöver att pusha koden.

Den enda verkliga Azure-relaterade läxan Del 18 gav var inte
containerrelaterad alls: `azureServiceConnection`-variabeln överst i
`azure-pipelines.yml` hade sedan Del 8 stått kvar med ett generiskt
exempelvärde (`sc-projectatlas-dev-sc`) i stället för det namn Peter
faktiskt gav sin service connection när han skapade den
(`project-atlas-connectionname`) — se steg 3 ovan, som nu visar det
verkliga namnet. Ingenting hade körts igenom hela pipelinen från början
till slut sedan Del 8/9, så mismatchen låg dold tills Del 18:s första
körning nådde `Deploy`-stadiet och failade där, med ett felmeddelande som
inte kunde hitta service connection-posten. Rättat i både filen och det här
dokumentet; efter det gick både `BuildAndTest` (`Build` och `DockerBuild`)
och `Deploy` gröna.

Om/när `DockerBuild` någon gång utökas till att faktiskt pusha till ett
Azure Container Registry, och App Service i så fall byter till "Web App for
Containers" (det öppna vägvalet Del 17:s och Del 18:s egna avsnitt i
`docs/ARCHITECTURE.md` båda flaggar), *då* får det ett eget nytt
provisioneringsavsnitt här — ett nytt Container Registry att skapa, en ny
roll (`AcrPush` för pipelinens service connection, `AcrPull` för App
Service:s managed identity) att bevilja. Det är dock inte längre Fas 6:s
enda återstående punkt — se avsnitt 12 nedan, och README.md:s Roadmap.

## 12. Del 19: Infrastructure as Code (Bicep) — hela miljön omskriven som kod

Varje tidigare Azure-Del (8 till 14) provisionerade sin resurs för hand,
ett `az`-kommando i taget, dokumenterat som en checklista i den här filen.
Del 19 gör om den checklistan till kod: `infra/main.bicep` plus en
`infra/modules/`-mapp, en fil per resurstyp. Från och med nu är avsnitt
1/3/7/8/9.1 ovan (App Service-plan/Web App, Key Vault-koppling, SQL-databas,
Storage-konto, Service Bus-rolltilldelning) och avsnitt 10/11 (Redis,
Application Insights — se uppdateringarna där) alla omdeploybara med ett
enda kommando istället för en lång rad `az`-kommandon. De manuella
kommandona i alla dessa avsnitt är kvar oförändrade som referens/fallback,
inte borttagna — men `infra/`-mallarna är den väg som faktiskt användes för
Del 19 och är den du bör utgå från härifrån.

**Så här kör du det** — fullständig förklaring, inklusive vad mallen
medvetet *inte* gör, står i `infra/main.bicep`s egen header-kommentar; den
korta versionen:

```bash
az bicep build --file infra/main.bicep          # bara syntaxkoll, inga Azure-anrop — samma sak pipelinens BicepValidate-jobb kör på varje push
az deployment group what-if `
  --resource-group rg-projectatlas-dev-sc `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.json         # förhandsgranska, inget deployas
az deployment group create `
  --resource-group rg-projectatlas-dev-sc `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.json         # faktisk deployment
```

(Windows/PowerShell-syntax ovan, med backtick-radbrytningar — byt mot `\`
i Git Bash/WSL.) `infra/main.parameters.json` har platshållarvärden för
ditt Key Valv, din SQL-server och ditt Service Bus-namespace (namn +
resursgrupp för varje) samt ett Storage-kontonamn — fyll i dina riktiga
värden innan du kör `what-if`/`create`.

**Bekräftat fungerande end-to-end mot den skarpa prenumerationen
2026-09-14 — men inte på första, andra eller tredje försöket.** Fyra
riktiga, distinkta fel dök upp under vägen, vart och ett diagnostiserat
från Azures egna felmeddelande istället för gissat i förväg — se
`docs/ARCHITECTURE.md`s Del 19-avsnitt för den fullständiga, tekniska
genomgången av alla fyra. Kort sammanfattat här:

1. `what-if` visade att `alwaysOn` skulle sättas till `true` på Web
   App:en — vilket inte stöds alls på F1 (gratisnivån, den faktiska
   plan-SKU:n). Upptäckt innan någon riktig resurs rördes, tack vare att
   `what-if` kördes först. Rättat genom att göra `alwaysOn` till en
   parameter som defaultar till `false`.
2. Den första riktiga `create`-körningen failade två resurser med
   `RoleAssignmentExists` — Key Vault- och Storage-rolltilldelningarna som
   redan fanns sedan Del 8/10, skapade för hand. Inget att rätta i
   koden: Azure upptäckte helt korrekt att arbetet redan var gjort. De
   två modulanropen togs bort ur `main.bicep` (modulerna finns kvar i
   repot, som referens).
3. Samma körning failade Redis helt: klassiska Azure Cache for Redis
   håller på att fasas ut — en plattformsförändring varken jag eller du
   kände till i förväg, upptäckt bara för att en riktig deployment
   faktiskt försöktes mot en riktig prenumeration. `redisCache.bicep`
   skrevs om mot den nya resurstypen (`Microsoft.Cache/redisEnterprise`,
   Azure Managed Redis), vilket i sin tur avslöjade att den första
   API-versionen jag valde inte var registrerad i din prenumeration/
   region, och att `publicNetworkAccess` var ett obligatoriskt fält den
   ursprungliga (preview-baserade) mallen jag utgick från saknade helt.
4. Efter att Redis väl skapades framgångsrikt failade *samma* deployment
   ett steg senare: nyckelbaserad åtkomst är avstängd som default på nya
   Azure Managed Redis-databaser (Azure styr mot Entra ID-autentisering
   istället) — men den här approachen (StackExchange.Redis-klient +
   anslutningssträng med lösenord i Key Vault, samma mönster Del 13 redan
   använde mot klassiska Redis) behöver nycklar påslagna. Rättat genom
   att explicit sätta `accessKeysAuthentication: 'Enabled'`.

`azure-pipelines.yml` fick också ett nytt jobb, `BicepValidate`, bredvid
`DockerBuild` — samma "bevisa att den fortfarande kompilerar på varje
push"-logik, bara `az bicep build`, inga Azure-anrop, ingen kostnad.
Medvetet **inte** en `what-if`/`create` i pipelinen: det skulle kräva
riktiga miljöspecifika parametervärden och skulle provisionera eller ändra
betalresurser (Application Insights, Redis) obevakat på varje push — exakt
den sortens risk det här projektet undviker överallt annars (se
resonemanget i avsnitt 0/5 ovan, och `infra/main.bicep`s egen
header-kommentar).

**Verifiera:**

```bash
az role assignment list --scope $(az servicebus namespace show --name nspl-sb-core-dev-sc --resource-group rg-core-dev-sc --query id -o tsv) --query "[].roleDefinitionName" -o table
# förväntat: "Azure Service Bus Data Sender" i listan — den enda genuint
# nya rolltilldelningen Del 19 gav; Key Vault/Storage-rollerna fanns redan

az monitor app-insights component show --app appi-projectatlas-dev-sc --resource-group rg-projectatlas-dev-sc --query connectionString -o tsv
# förväntat: en riktig connection string, inte tomt

curl "https://app-projectatlas-dev-sc.azurewebsites.net/health"
# förväntat: "Healthy" — bekräftar att Web App:en fortfarande svarar efter
# deploymentets ~/Modify-steg på httpsOnly
```

Fas 6 är därmed helt klar (Del 18 och Del 19, båda bekräftade fungerande
end-to-end mot skarp Azure) — se README.md:s Roadmap. Fas 4:s öppna punkt
är nu bara *var* `Atlas.Worker` ska köras i Azure (avsnitt 9 ovan) — Redis
och Application Insights, de två andra molnpunkterna Fas 4/5 lämnat efter
sig, är stängda av Del 19. Fas 3:s enda kvarvarande punkt är fortsatt
Del 20 (generalisera Key Vault-uppsättningen).
