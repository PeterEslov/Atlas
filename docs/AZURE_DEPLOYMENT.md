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

```bash
az group create --name rg-projectatlas --location swedencentral

# F1 (gratis) finns inte i alla region/OS-kombinationer — testa F1 först,
# fall tillbaka till B1 (billig, inte gratis) om Azure svarar att F1 inte
# finns för Linux i swedencentral just nu.
az appservice plan create \
  --name plan-projectatlas \
  --resource-group rg-projectatlas \
  --location swedencentral \
  --sku F1 \
  --is-linux

# Webbappens namn måste vara globalt unikt inom hela Azure (det blir en del
# av URL:en <namn>.azurewebsites.net) — välj något du inte redan sett upptaget.
az webapp list-runtimes --os linux --output table | grep -i dotnet
# ^ kör den här för att se den exakta runtime-identifieraren för .NET 10 —
# formatet har växlat mellan Azure CLI-versioner (t.ex. "DOTNETCORE:8.0"),
# så bekräfta strängen istället för att lita på exemplet nedan.

az webapp create \
  --name <ditt-unika-app-namn> \
  --resource-group rg-projectatlas \
  --plan plan-projectatlas \
  --runtime "DOTNETCORE:10.0"
```

## 2. Application settings (bootstrap-konfiguration)

Detta är *inte* Del 20 (Key Vault) — det är samma mönster som
`appsettings.Development.json` redan använder lokalt, bara flyttat till
App Service Configuration istället för en fil i repot. Värdena blir
miljövariabler för processen; ASP.NET Core:s config-system mappar
dubbla understreck till kolon (`Jwt__SigningKey` → config-nyckeln
`Jwt:SigningKey`), vilket är varför `Jwt.cs`/`Program.cs` aldrig behöver
veta att värdena kom från Azure och inte från en JSON-fil.

```bash
az webapp config appsettings set \
  --name <ditt-app-namn> \
  --resource-group rg-projectatlas \
  --settings \
    Jwt__Issuer=ProjectAtlas \
    Jwt__Audience=ProjectAtlas.Api \
    Jwt__ExpiryMinutes=60 \
    EnableSwaggerUi=true

# Signeringsnyckeln sätter du separat, med ett eget genererat värde —
# klistra aldrig in den riktiga nyckeln i ett delat dokument. Minst 32 bytes
# (256 bitar), t.ex.:
openssl rand -base64 48
az webapp config appsettings set \
  --name <ditt-app-namn> \
  --resource-group rg-projectatlas \
  --settings Jwt__SigningKey="<klistra in värdet från raden ovan>"
```

`EnableSwaggerUi=true` här är ett medvetet demo-val för ett portfolioprojekt
— se kommentaren i `Program.cs` för varför det är en egen config-switch och
inte kopplat till `ASPNETCORE_ENVIRONMENT`. Sätt den till `false` (eller ta
bort den, samma sak som default) om du vill stänga av den igen.

## 3. Health check

Portalen: din Web App → **Monitoring → Health check** → aktivera, sökväg
`/health`. Det pekar mot `MapHealthChecks("/health")` i `Program.cs`, som
medvetet inte är beroende av databasen (se kommentaren där för varför) —
så det här fungerar redan efter steg 1–2, innan Del 9 finns.

## 4. GitHub Actions: lösenordsfri inloggning (OIDC)

Ingen klienthemlighet lagras i GitHub alls — workflowen (`.github/workflows/deploy.yml`)
växlar sin egen GitHub-utfärdade OIDC-token mot en Azure AD-token, begränsat
till just det här repot och just `main`-branchen.

```bash
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
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/rg-projectatlas"

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

## 5. Verifiera

Pusha till `main` (eller kör workflowen manuellt via Actions-fliken →
"Build, test and deploy to Azure" → Run workflow), följ körningen i
GitHub Actions-fliken, och kontrollera sedan:

```bash
curl https://<ditt-app-namn>.azurewebsites.net/health
# förväntat: "Healthy" (200 OK) — fungerar oavsett databas

curl https://<ditt-app-namn>.azurewebsites.net/openapi/v1.json
# förväntat: ett OpenAPI-dokument, om EnableSwaggerUi=true — annars 404
```

Att `/api/auth/login` eller `/api/tickets` svarar med ett databasfel just nu
är väntat (se ingressen ovan) — det är precis den biten Del 9 stänger.

## Nästa: Del 9 — Azure SQL

Byt `ConnectionStrings:AtlasDb` (samma `__`-mönster som `Jwt__SigningKey`
ovan: `ConnectionStrings__AtlasDb`, *eller* — bekvämare — lägg den som en
riktig **Connection string** av typen "SQL Azure" i portalen, vilket App
Service automatiskt exponerar som `ConnectionStrings:AtlasDb` utan
dubbla-understreck-tricket) till en riktig Azure SQL-instans, kör
migrationerna mot den (`dotnet ef database update` — inte
auto-migrate-on-startup, som medvetet stannar kvar som en
`IsDevelopment()`-only-genväg, se `Program.cs`), och hela API:et blir
funktionellt i molnet.
