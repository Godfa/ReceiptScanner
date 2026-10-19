# Receipt Scanner API - Projektin yhteenveto

## Projektin tiedot

- **Nimi:** Receipt Scanner API
- **Teknologia:** ASP.NET Core 8.0 / .NET 10
- **Tyyppi:** Mikropalvelu / REST API
- **Tarkoitus:** Kuittien automaattinen skannaus ja rivitietojen purkaminen AI:lla
- **Luotu:** 2026-01-12

## Arkkitehtuuri

### Clean Architecture -periaatteet

Projekti noudattaa yksinkertaistettua Clean Architecture -mallia:

```
API/
├── Controllers/        # REST API endpoints (esityskerros)
├── Models/            # DTOs (data transfer objects)
├── Services/          # Liiketoimintalogiikka ja integraatiot
└── Program.cs         # Konfiguraatio ja DI
```

### Provider-pohjainen arkkitehtuuri

Järjestelmä käyttää **Strategy Pattern** -mallia, jossa:
- `IReceiptScannerProvider` = yksittäinen skannauspalvelun toteutus (Azure, Ollama)
- `IReceiptScannerService` = pääpalvelu, joka hallinnoi providereitä ja fallback-logiikkaa

### Providerit

1. **AzureDocumentIntelligenceProvider**
   - Käyttää Azure AI Document Intelligence (Form Recognizer)
   - Esiharjoiteltu "prebuilt-receipt" -malli
   - Korkea tarkkuus, maksullinen

2. **OllamaVisionProvider**
   - Käyttää paikallista Ollama vision -mallia (esim. llama3.2-vision)
   - Ilmainen, vaatii paikallisen asennuksen
   - Vaihtelevanpaa laatua

3. **ReceiptScannerService** (composite)
   - Hallinnoi providereitä
   - Automaattinen fallback Azure → Ollama
   - Arvioi tulosten laadun

## API Endpoints

| Method | Endpoint | Kuvaus |
|--------|----------|--------|
| POST | `/api/receipts/scan` | Skannaa base64-koodattu kuva |
| POST | `/api/receipts/scan-file` | Skannaa ladattu tiedosto |
| GET | `/api/receipts/providers` | Lista käytettävistä providereista |
| GET | `/api/receipts/health` | Health check |

## Keskeiset ominaisuudet

✅ **Useita AI-providereitä** - Azure + Ollama + helposti laajennettavissa
✅ **Automaattinen fallback** - Jos ensimmäinen provider epäonnistuu, kokeillaan seuraavaa
✅ **Tulosten validointi** - Arvioi tulosten laatua ennen palauttamista
✅ **Swagger/OpenAPI** - Interaktiivinen API-dokumentaatio
✅ **CORS-tuki** - Valmis frontend-integraatiolle
✅ **Strukturoitu vastaus** - JSON-vastaus rivitiedoilla, hinnoilla ja metadatalla
✅ **Varoitusjärjestelmä** - Palauttaa varoituksia alhaisesta luottamuksesta tai virheistä

## Teknologiat ja kirjastot

### NuGet-paketit

- **Azure.AI.DocumentIntelligence** (1.0.0) - Azure Document Intelligence SDK
- **Swashbuckle.AspNetCore** (7.2.0) - Swagger/OpenAPI tools
- **Microsoft.AspNetCore.OpenApi** (10.0.1) - OpenAPI spec generation

### Frameworkit

- **ASP.NET Core 8.0** - Web API framework
- **System.Text.Json** - JSON serialization
- **.NET HttpClient** - HTTP-pyyntöjen tekeminen Ollamalle

## Konfiguraatio

### Ympäristömuuttujat / appsettings.json

```json
{
  "Azure": {
    "DocumentIntelligence": {
      "Endpoint": "https://xxx.cognitiveservices.azure.com/",
      "ApiKey": "***"
    }
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2-vision"
  }
}
```

## Integraatio MokkilanInvoices-järjestelmään

Tämä mikropalvelu on suunniteltu integroitavaksi MokkilanInvoices-sovellukseen.

### Integraatioprosessi

1. **Receipt Scanner API** (tämä projekti)
   - Toimii itsenäisenä palveluna
   - Kuuntelee HTTP-pyyntöjä

2. **MokkilanInvoices API**
   - Kutsuu Receipt Scanneria HTTP-clientilla
   - Luo ExpenseItem + LineItem -objektit tuloksista
   - Tallentaa tietokantaan

3. **MokkilanInvoices Frontend**
   - Käyttöliittymä kuitin lataamiseen
   - Esikatselu skannatuista riveistä
   - Mahdollisuus muokata ennen tallennusta

Katso yksityiskohtaiset ohjeet: [RECEIPT_SCANNER_INTEGRATION.md](../ReactCoreInvoices/RECEIPT_SCANNER_INTEGRATION.md)

## Tietoturva

### Toteutettu

- ✅ Tiedostokoon rajaus (10MB)
- ✅ MIME-tyypin validointi
- ✅ Base64-dekoodauksen virheenkäsittely
- ✅ CORS-konfiguraatio
- ✅ Exception handling kaikissa endpointeissa

### Suositukset tuotantoon

- 🔐 Bearer token -autentikointi
- 🔐 Rate limiting (esim. AspNetCoreRateLimit)
- 🔐 API key -rotaatio Azure:ssa
- 🔐 HTTPS-pakotus
- 🔐 Input sanitization kuvadiagnostiikoille

## Testaus

### Manuaalinen testaus

1. **Swagger UI** - Käynnistä sovellus ja avaa `https://localhost:5001`
2. **cURL** - Katso esimerkit [TESTING.md](TESTING.md)
3. **Postman** - Tuo OpenAPI spec Postmaniin

### Automaattinen testaus (TODO)

- Unit-testit providereille
- Integraatiotestit API-endpointeille
- Mock-providerit testausta varten
- Load testing Apache Bench:llä tai k6:lla

## Jatkokehitysideat

### Prioriteetti: Korkea

- [ ] Unit-testit (xUnit + Moq)
- [ ] Docker-tuki (Dockerfile + docker-compose.yml)
- [ ] CI/CD pipeline (GitHub Actions)

### Prioriteetti: Keskitaso

- [ ] Rate limiting
- [ ] Bearer token -autentikointi
- [ ] Webhook-tuki pitkäkestoisille skannauksille
- [ ] Batch processing (useita kuitteja kerralla)

### Prioriteetti: Matala

- [ ] Kuitin kuvan tallennus blob storageen
- [ ] A/B-testaus providereiden välillä
- [ ] Käyttäjien korjausten tallennus (learning loop)
- [ ] Lisää AI-providereitä (Google Vision, AWS Textract)
- [ ] GraphQL API -tuki

## Tiedostorakenne

```
ReceiptScanner/
├── .gitignore                      # Git ignore rules
├── README.md                       # Pääohje
├── TESTING.md                      # Testausohjeet
├── PROJECT_SUMMARY.md             # Tämä tiedosto
├── ReceiptScanner.sln             # Solution file
└── API/
    ├── ReceiptScanner.API.csproj  # Project file
    ├── Program.cs                 # Entry point + DI setup
    ├── appsettings.json           # Base configuration
    ├── appsettings.Development.json  # Dev config
    ├── Controllers/
    │   └── ReceiptsController.cs  # REST endpoints
    ├── Models/
    │   ├── ScanReceiptRequest.cs  # Request DTO
    │   ├── ScannedReceiptLine.cs  # Line item model
    │   └── ScannedReceiptResult.cs  # Response DTO
    └── Services/
        ├── IReceiptScannerProvider.cs        # Provider interface
        ├── IReceiptScannerService.cs         # Service interface
        ├── AzureDocumentIntelligenceProvider.cs  # Azure impl
        ├── OllamaVisionProvider.cs           # Ollama impl
        └── ReceiptScannerService.cs          # Composite service
```

## Käynnistys

### Kehitysympäristö

```bash
cd API
dotnet watch run
```

API käynnistyy: `https://localhost:5001`
Swagger UI: `https://localhost:5001/`

### Tuotanto

```bash
dotnet publish -c Release -o ./publish
cd publish
dotnet ReceiptScanner.API.dll
```

## Ylläpito

### Lokit

- Konsoli-logitus (Development)
- Strukturoitu logging JSON-muodossa (Production, lisää Serilog)

### Monitorointi

- Health check endpoint: `/api/receipts/health`
- Provider availability tracking
- Processing time metrics

### Skaalaus

- Stateless design → helppo horisontaalinen skaalaus
- HttpClient pool → tehokas HTTP-yhteyksien käyttö
- Singleton services → jaettu tila minimoitu

## Yhteenveto

Receipt Scanner API on **tuotantovalmis pohja** kuittien skannaukseen. Projekti:

✅ Toimii itsenäisenä mikropalveluna
✅ Tukee useita AI-providereitä
✅ Sisältää automaattisen fallback-logiikan
✅ On dokumentoitu ja testattavissa
✅ Integroituu helposti MokkilanInvoices-järjestelmään

**Seuraavat askeleet:**
1. Konfiguroi Azure tai Ollama
2. Testaa Swagger UI:lla
3. Integroituu MokkilanInvoices-sovellukseen
4. Lisää yksikkötestit ja Docker-tuki

---

*Luotu Claude Code:lla 2026-01-12*
