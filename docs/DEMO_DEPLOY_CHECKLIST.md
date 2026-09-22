# Demo-deploy — checklista (utkast, ej körd i sin helhet än)

**Status:** Det här är en plan, inte bekräftad dokumentation. Ingen av
kommandona nedan har körts mot en skarp prenumeration ännu (till skillnad
från `docs/AZURE_DEPLOYMENT.md`, där varje avsnitt är verifierat). Kör
igenom den här checklistan tillsammans med Claude första gången du faktiskt
ska demoa, verifiera att den stämmer, och flytta sedan in den permanent i
`AZURE_DEPLOYMENT.md` som ett riktigt avsnitt (nästa Del) — samma regel
projektet redan följer för all annan dokumentation: den skrivs efter att
något är bekräftat fungera, inte innan.

**Syfte:** en live, klickbar webbadress att visa en utomstående, utan att
din workstation behöver vara med. Backend (`Atlas.Api`) deployas till Azure
App Service, frontend (`frontend/`) till Azure Static Web Apps.

---

## Steg 0: Kolla om Del 19-resurserna redan lever

`infra/main.bicep` kördes redan en gång mot din riktiga prenumeration under
Del 19 (2026-09-14, se `docs/AZURE_DEPLOYMENT.md` avsnitt 12) för att
bevisa att den fungerar. Om du inte rev ner det efteråt kan halva jobbet
redan vara gjort — kolla innan du kör något annat:

```bash
az group show --name rg-projectatlas-dev-sc --query properties.provisioningState -o tsv
# Finns inte (fel/tomt svar) → allt är rivet, börja från Steg 1.

az webapp show --name app-projectatlas-dev-sc --resource-group rg-projectatlas-dev-sc --query state -o tsv
# "Running"? Testa direkt:
curl https://app-projectatlas-dev-sc.azurewebsites.net/health
```

Svarar `/health` redan `Healthy`? Backend lever. Hoppa till **Steg 3**
(frontend) — men kolla ändå kostnadsläget i Azure Portal (Cost Management)
innan du river ner något, ifall det stått uppe ett tag utan att du tänkt på
det.

**Notera (2026-09-22): Redis är borttaget separat, oavsett vad `/health`
ovan säger.** Den kostade pengar varje dag (Redis går inte att pausa, bara
ta bort — se `docs/AZURE_DEPLOYMENT.md` avsnitt 12) och togs bort för sig:

```bash
az redisenterprise delete --name redis-projectatlas-dev-sc --resource-group rg-projectatlas-dev-sc -y
```

Så: `/health` kan mycket väl svara `Healthy` (appen är fail-open utan
Redis, se `docs/ARCHITECTURE.md`s Del 13-avsnitt) samtidigt som Redis
faktiskt saknas. Kolla separat om du behöver den tillbaka inför demot:

```bash
az redisenterprise show --name redis-projectatlas-dev-sc --resource-group rg-projectatlas-dev-sc --query provisioningState -o tsv
# tomt/fel -> kör om `az deployment group create` mot infra/main.bicep (Steg 1)
# för att få tillbaka bara Redis-delen också
```

## Steg 1: Deploya backend-infran (om den inte redan lever)

```bash
az bicep build --file infra/main.bicep          # syntaxkoll, gratis, inga Azure-anrop

az deployment group what-if \
  --resource-group rg-projectatlas-dev-sc \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json         # förhandsgranska — fyll i dina riktiga värden i parameters-filen först

az deployment group create \
  --resource-group rg-projectatlas-dev-sc \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.json         # faktisk deployment
```

Samma kommandon, samma mall som redan bevisat fungerar i Del 19 — inget
nytt här. **Notera:** mallen skapar Azure Managed Redis (`Balanced_B0`)
automatiskt, det går inte att välja bort utan att ändra en redan
verifierad mall. Kostnaden för några timmars/en dags körning är låg, se
nedmonteringen i Steg 6. (Om du av kostnadsskäl väljer att inte köra hela
Bicep-mallen och istället lappar ihop en enklare manuell deploy utan
Redis — se `Cors:AdditionalOrigins`/`Redis:ConnectionString`-resonemanget
i `Program.cs`/`DependencyInjection.cs`: den enda hårda kravet är att
`Redis:ConnectionString` *finns* som en App Setting, inte att den pekar på
något som faktiskt svarar.)

Efter en lyckad `create`, notera output-värdena (`webAppDefaultHostName`
med mera) — du behöver den riktiga URL:en i Steg 4.

## Steg 2: Seed:a data och registrera ett admin-konto

Samma bootstrapping-problem som lokalt/Docker: den nya `AtlasDb` är tom.

1. Koppla SSMS/Azure Data Studio mot den nya Azure SQL-databasen (samma
   sak du redan gjort mot LocalDB och Docker-containern, bara med Azure
   SQL:s anslutningsuppgifter den här gången).
2. Kör `sql/002_SeedData.sql` mot den.
3. Hämta ett `organizationId`:
   ```sql
   SELECT Id, Name FROM dbo.Organizations;
   ```
4. Registrera ett riktigt konto via API:et direkt (innan frontend ens är
   uppe, för att bekräfta att backend fungerar isolerat):
   ```bash
   curl -X POST "https://<din-webapp>.azurewebsites.net/api/auth/register" \
     -H "Content-Type: application/json" \
     -d '{"organizationId":"<id från steg 3>", "fullName":"Demo Admin", "email":"demo@example.com", "password":"...", ...}'
   ```
   (se `docs/AZURE_DEPLOYMENT.md` avsnitt 6 för fältnamnen — samma body
   som redan används där för att verifiera Del 8.)

## Steg 3: Deploya frontend till Azure Static Web Apps

Inte byggt än någonstans i projektet — Del 21 kom efter att
`AZURE_DEPLOYMENT.md` skrevs. Static Web Apps gratisnivå passar bra: en
statisk Vite/React-app, egen `https://...azurestaticapps.net`-URL.

```bash
# Skapa själva resursen (tom, utan GitHub-koppling — vi pushar filerna
# direkt med CLI:t nedan istället, eftersom repot inte ligger på GitHub)
az staticwebapp create \
  --name swa-projectatlas-dev-sc \
  --resource-group rg-projectatlas-dev-sc \
  --location westeurope \
  --sku Free

# Bygg frontend mot den RIKTIGA backend-URL:en från Steg 1
cd frontend
echo "VITE_API_BASE_URL=https://<din-webapp>.azurewebsites.net" > .env.production
npm run build          # skapar dist/

# Hämta deployment-token och pusha den byggda dist/-mappen
TOKEN=$(az staticwebapp secrets list --name swa-projectatlas-dev-sc --resource-group rg-projectatlas-dev-sc --query properties.apiKey -o tsv)
npx @azure/static-web-apps-cli deploy ./dist --deployment-token "$TOKEN" --env production
```

Notera den riktiga URL:en kommandot ger tillbaka (något i stil med
`https://<slumpat-namn>.azurestaticapps.net`) — det är länken du faktiskt
skickar/visar på demot.

## Steg 4: Koppla ihop CORS

Backend måste veta om den nya frontend-domänen — annars blockerar
webbläsaren varje anrop trots att båda sidor är uppe. `Program.cs` har
redan stödet (`Cors:AdditionalOrigins`, se kommentaren i CORS-blocket),
så det räcker med en App Setting, ingen ny kodändring/redeploy:

```bash
az webapp config appsettings set \
  --name app-projectatlas-dev-sc \
  --resource-group rg-projectatlas-dev-sc \
  --settings Cors__AdditionalOrigins="https://<riktig-swa-url>.azurestaticapps.net"

az webapp restart --name app-projectatlas-dev-sc --resource-group rg-projectatlas-dev-sc
```

## Steg 5: Testa hela vägen

Öppna Static Web App-URL:en i en **privat/inkognitoflik** (undviker att
webbläsaren visar en gammal cachead version), logga in med
demo-kontot från Steg 2, klicka runt i tickets/organizations/teams precis
som lokalt.

## Steg 6: Efter demot — riv ner igen

**Viktigt:** `rg-projectatlas-dev-sc` innehåller INTE allt. Key Vault,
SQL-servern och Service Bus-namespacet är medvetet återanvända befintliga
resurser i andra resursgrupper (se `infra/main.bicep`s header-kommentar) —
bara själva SQL-*databasen*, hemligheten och rolltilldelningen som skapades
för just den här deployen ligger där, inte resurserna de hänger på.

```bash
# Tar bort App Service-plan, Web App, Storage, Redis, App Insights,
# Static Web App — allt som faktiskt lever i den här resursgruppen
az group delete --name rg-projectatlas-dev-sc --yes --no-wait

# Databasen ligger kvar på den delade SQL-servern annars
az sql db delete --name <sqlDatabaseName> --server <sqlServerName> --resource-group <sqlServerResourceGroup> --yes

# Valfritt men bra städning — döda hemligheten/rolltilldelningen som annars
# bara blir skräp i de delade resurserna (ingen löpande kostnad, men ändå)
az keyvault secret delete --vault-name <keyVaultName> --name "Redis--ConnectionString"
```

Dubbelkolla i Azure Portal → Cost Management att inget oväntat står kvar
och tickar efter det här.
