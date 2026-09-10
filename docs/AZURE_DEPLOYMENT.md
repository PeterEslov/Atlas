# Azure-driftsättning (Del 8)

Det här dokumentet är en körbar checklista för att driftsätta Atlas.Api till
Azure App Service, med en GitHub Actions-pipeline som bygger, testar och
driftsätter automatiskt vid varje push till `main`. Kommandona nedan kör du
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
- Ditt lokala repo pushat till GitHub. Om det inte redan är det:
  ```
  git init
  git add .
  git commit -m "Initial commit"
  gh repo create ProjectAtlas --private --source=. --remote=origin --push
  ```
  (`gh` är GitHub CLI — alternativt skapa repot i webbgränssnittet och kör
  `git remote add origin <url>` + `git push -u origin main` själv.)

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
KEYVAULT_NAME=mykeyvaultpeter

# 1) System-assigned managed identity på App Service — det är så appen
#    autentiserar mot valvet. Ingen nyckel eller connection string behövs
#    för att komma åt hemligheterna; identiteten ÄR autentiseringen.
az webapp identity assign --name "$WEBAPP_NAME" --resource-group "$RG"
PRINCIPAL_ID=$(az webapp identity show --name "$WEBAPP_NAME" --resource-group "$RG" --query principalId -o tsv)

# 2) Läsrättighet för den identiteten. Vilket kommando som gäller beror på
#    vilken auktoriseringsmodell ditt befintliga valv använder — kolla:
az keyvault show --name "$KEYVAULT_NAME" --query properties.enableRbacAuthorization -o tsv
```

```bash
# "true" -> RBAC-modellen:
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee "$PRINCIPAL_ID" \
  --scope "$(az keyvault show --name "$KEYVAULT_NAME" --query id -o tsv)"
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

## 5. GitHub Actions: lösenordsfri inloggning (OIDC)

Ingen klienthemlighet lagras i GitHub alls — workflowen (`.github/workflows/deploy.yml`)
växlar sin egen GitHub-utfärdade OIDC-token mot en Azure AD-token, begränsat
till just det här repot och just `main`-branchen.

```bash
# Ny terminal sedan steg 1? Sätt om RG=rg-projectatlas-dev-sc också.
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)

APP_ID=$(az ad app create --display-name "github-projectatlas-deploy" --query appId -o tsv)
az ad sp create --id "$APP_ID"

# Contributor begränsat till resursgruppen, inte hela prenumerationen — en
# läckt eller felkonfigurerad pipeline kan då som mest skada det här ena
# projektets resurser.
az role assignment create \
  --assignee "$APP_ID" \
  --role Contributor \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG"

# Byt <ditt-github-anvandarnamn>/<repo-namn> mot ditt faktiska repo.
az ad app federated-credential create \
  --id "$APP_ID" \
  --parameters '{
    "name": "github-projectatlas-main",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:<ditt-github-anvandarnamn>/<repo-namn>:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'

echo "AZURE_CLIENT_ID=$APP_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
```

Lägg de tre värdena som **Repository variables** (inte Secrets — det är
GUID:er, inga hemligheter) i GitHub: repot → Settings → Secrets and
variables → Actions → Variables:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_WEBAPP_NAME` — samma namn du valde i steg 1

## 6. Verifiera

Pusha till `main` (eller kör workflowen manuellt via Actions-fliken →
"Build, test and deploy to Azure" → Run workflow), följ körningen i
GitHub Actions-fliken, och kontrollera sedan:

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
