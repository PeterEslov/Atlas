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
3. Ge den ett namn du känner igen, t.ex. `sc-projectatlas-dev-sc`, och spara.
   Det namnet är det enda pipelinen behöver referera till — Azure DevOps
   lagrar och hanterar App Registration, service principal *och* det
   federerade förtroendet bakom den namngivna service connection-posten. Inga
   client-id/tenant-id/subscription-id-värden att kopiera någonstans, till
   skillnad från GitHub-flödet.
4. **Pipelines** → **New pipeline** → **Azure Repos Git** → välj ditt repo →
   **Existing Azure Pipelines YAML file** → `/azure-pipelines.yml` (filen
   som redan ligger i repots rot, se nedan) → **Save** (kör inte än om du
   vill dubbelkolla service connection-namnet i filen först).

`azure-pipelines.yml` i repots rot refererar till service connection-namnet
från steg 3 via variabeln `azureServiceConnection` överst i filen — öppna den
och sätt den till exakt det namn du valde:

```yaml
variables:
  azureServiceConnection: 'sc-projectatlas-dev-sc'   # namnet från steg 3
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

## Nästa: Del 9 — Azure SQL

Sätt `ConnectionStrings:AtlasDb` till en riktig Azure SQL-instans — och nu
när valvet redan är kopplat in (avsnitt 3), gå direkt dit i stället för
vägen via en vanlig Application Setting: samma `az keyvault secret set`-
mönster som `Jwt--SigningKey`, fast med secret-namnet
`ConnectionStrings--AtlasDb`. Kör migrationerna mot den nya databasen
(`dotnet ef database update` — inte auto-migrate-on-startup, som medvetet
stannar kvar som en `IsDevelopment()`-only-genväg, se `Program.cs`), och
hela API:et blir funktionellt i molnet.
