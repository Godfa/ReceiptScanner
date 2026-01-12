# Receipt Scanner API

Mikropalvelu kuittien skannaukseen käyttäen Azure AI Document Intelligence ja Ollama vision -malleja.

## Yleiskatsaus

Receipt Scanner on itsenäinen ASP.NET Core 8.0 -mikropalvelu, joka:
- Vastaanottaa kuittikuvia (base64 tai file upload)
- Purkaa rivitiedot käyttäen AI vision -malleja
- Palauttaa strukturoidun JSON-vastauksen
- Tukee useita providereitä (Azure, Ollama) fallback-logiikalla

## Projektirakenne

```
ReceiptScanner/
├── API/
│   ├── Controllers/
│   │   └── ReceiptsController.cs          # REST API endpoints
│   ├── Models/
│   │   ├── ScanReceiptRequest.cs          # Request DTO
│   │   ├── ScannedReceiptLine.cs          # Line item model
│   │   └── ScannedReceiptResult.cs        # Response DTO
│   ├── Services/
│   │   ├── IReceiptScannerProvider.cs     # Provider interface
│   │   ├── IReceiptScannerService.cs      # Main service interface
│   │   ├── AzureDocumentIntelligenceProvider.cs  # Azure provider
│   │   ├── OllamaVisionProvider.cs        # Ollama provider
│   │   └── ReceiptScannerService.cs       # Composite service with fallback
│   ├── Program.cs                         # Application entry point
│   └── appsettings.json                   # Configuration
└── README.md
```

## Asennus ja käynnistys

### Esivalmistelut

1. .NET 8.0 SDK tai uudempi
2. (Valinnainen) Azure AI Document Intelligence -resurssit
3. (Valinnainen) Ollama vision -malli (esim. llama3.2-vision)

### Konfiguraatio

Muokkaa `API/appsettings.json` tai `API/appsettings.Development.json`:

```json
{
  "Azure": {
    "DocumentIntelligence": {
      "Endpoint": "https://your-resource.cognitiveservices.azure.com/",
      "ApiKey": "your-azure-api-key-here"
    }
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2-vision"
  }
}
```

**Huom:** Voit jättää toisen providerin tyhjäksi, palvelu käyttää vain konfiguroituja providereitä.

### Käynnistys

```bash
# Kehitysympäristö (hot reload)
cd API
dotnet watch run

# Tai tavallinen käynnistys
dotnet run --project API
```

API käynnistyy oletuksena osoitteeseen: `https://localhost:5001`

Swagger UI löytyy osoitteesta: `https://localhost:5001/` (dev-moodissa)

## API-dokumentaatio

### POST /api/receipts/scan

Skannaa kuitin base64-koodatusta kuvasta.

**Request Body:**
```json
{
  "imageBase64": "iVBORw0KGgoAAAANSUhEUg...",
  "language": "fi",
  "provider": "auto"
}
```

**Parametrit:**
- `imageBase64` (string, pakollinen): Base64-koodattu kuva
- `language` (string): Kielikoodi (fi, en, sv) - oletuksena "fi"
- `provider` (string): Provider-valinta ("auto", "azure", "ollama") - oletuksena "auto"

**Response:**
```json
{
  "provider": "azure",
  "lines": [
    {
      "description": "MAITO 1L",
      "quantity": 1,
      "unitPrice": 1.29,
      "lineTotal": 1.29,
      "confidence": 0.95
    },
    {
      "description": "LEIPÄ",
      "quantity": 2,
      "unitPrice": 2.50,
      "lineTotal": 5.00,
      "confidence": 0.92
    }
  ],
  "total": 6.29,
  "merchantName": "K-Market",
  "receiptDate": "2026-01-12T00:00:00",
  "rawText": null,
  "warnings": [],
  "processingTimeMs": 2453
}
```

### POST /api/receipts/scan-file

Skannaa kuitin suoraan ladatusta tiedostosta (multipart/form-data).

**Query Parameters:**
- `language` (string): Kielikoodi - oletuksena "fi"
- `provider` (string): Provider-valinta - oletuksena "auto"

**Form Data:**
- `file` (file): Kuittikuva (jpeg, jpg, png, webp)

**Response:** Sama kuin `/scan`

### GET /api/receipts/providers

Palauttaa listan käytettävissä olevista providereista.

**Response:**
```json
{
  "providers": ["azure", "ollama"],
  "count": 2
}
```

### GET /api/receipts/health

Health check -endpoint.

**Response:**
```json
{
  "status": "healthy",
  "availableProviders": ["azure", "ollama"],
  "providerCount": 2,
  "timestamp": "2026-01-12T10:30:00Z"
}
```

## Provider-logiikka

### Auto Mode (provider: "auto")

Käyttää providereitä prioriteettijärjestyksessä fallback-logiikalla:

1. **Azure AI Document Intelligence** (ensisijainen)
   - Korkea tarkkuus
   - Esiharjoiteltu kuittimalli
   - Vaatii maksullisen Azure-resurssin

2. **Ollama Vision** (toissijainen)
   - Paikallinen/ilmainen
   - Vaatii Ollaman ja vision-mallin asennuksen
   - Vaihtelevanpaa laatua

**Fallback-ehdot:**
- Tulos hyväksytään, jos:
  - Ainakin yksi rivitiedosto löytyy
  - Ei kriittisiä virheitä
  - Vähintään 50% riveistä sisältää hintatiedot

- Jos provider ei tuota riittävää tulosta, kokeillaan seuraavaa

### Specific Provider Mode

Voit valita tietyn providerin:
- `provider: "azure"` - Käytä vain Azurea
- `provider: "ollama"` - Käytä vain Ollamaa

## Varoitukset ja virheenkäsittely

API palauttaa `warnings`-listan, joka voi sisältää:
- `low_confidence_on_item_X` - Alhainen luottamus rivitiedolle
- `Skipped item without description` - Ohitettu rivi ilman kuvausta
- `scanner_error: <viesti>` - Tekninen virhe skannauksessa
- `All providers tried: ...` - Kaikki providerit kokeiltu

**HTTP-statuskoodit:**
- `200 OK` - Skannaus onnistui (varoituksista huolimatta)
- `400 Bad Request` - Virheellinen pyyntö (puuttuva/virheellinen kuva)
- `500 Internal Server Error` - Palvelinvirhe
- `503 Service Unavailable` - Providerit eivät käytettävissä

## Kehitys ja testaus

### Buildi ja testaus

```bash
# Käännä projekti
dotnet build

# Suorita testit (kun lisätty)
dotnet test

# Formatoi koodi
dotnet format
```

### Testaaminen cURL:lla

```bash
# Skannaa kuitti base64:sta
curl -X POST https://localhost:5001/api/receipts/scan \
  -H "Content-Type: application/json" \
  -d '{
    "imageBase64": "'$(base64 -w 0 receipt.jpg)'",
    "language": "fi",
    "provider": "auto"
  }'

# Skannaa kuitti tiedostosta
curl -X POST https://localhost:5001/api/receipts/scan-file \
  -F "file=@receipt.jpg" \
  -F "language=fi" \
  -F "provider=auto"

# Tarkista käytettävissä olevat providerit
curl https://localhost:5001/api/receipts/providers

# Health check
curl https://localhost:5001/api/receipts/health
```

## Integraatio MokkilanInvoices-järjestelmään

Tämä palvelu on suunniteltu integroitavaksi MokkilanInvoices-sovellukseen. Katso yksityiskohtaiset integraatio-ohjeet tiedostosta:

[RECEIPT_SCANNER_INTEGRATION.md](../ReactCoreInvoices/RECEIPT_SCANNER_INTEGRATION.md)

### Pikalinkki integraatioon

MokkilanInvoices kutsuu tätä palvelua:

```csharp
// MokkilanInvoices API/Services/ReceiptScannerService.cs
var response = await _httpClient.PostAsJsonAsync(
    "/api/receipts/scan",
    new { imageBase64, language = "fi", provider = "auto" });

var result = await response.Content.ReadFromJsonAsync<ScannedReceiptResult>();
```

## Turvallisuusnäkökohdat

- **Tiedostokoko**: Max 10MB per kuitti
- **CORS**: Konfiguroitavissa (oletuksena sallii kaikki dev-moodissa)
- **Rate limiting**: Suositellaan lisättäväksi tuotantoon
- **Autentikointi**: Suositellaan Bearer token -autentikointia tuotantoon

## Jatkokehitysideoita

- [ ] Unit- ja integraatiotestit
- [ ] Rate limiting
- [ ] Bearer token -autentikointi
- [ ] Docker-tuki
- [ ] Batch processing (useita kuitteja kerralla)
- [ ] Webhook-tuki pitkille prosesseille
- [ ] Kuitin kuvan tallennus blob storageen
- [ ] A/B-testaus providereiden välillä
- [ ] Oppiminen käyttäjän korjauksista

## Lisenssit

Projekti käyttää seuraavia kirjastoja:
- **Azure.AI.FormRecognizer** - Azure SDK for .NET (MIT)
- **Swashbuckle.AspNetCore** - Swagger/OpenAPI tools (MIT)

## Tuki

Ongelmatilanteissa tarkista:
1. `appsettings.json` konfiguraatio on oikein
2. Azure API key ja endpoint ovat voimassa
3. Ollama on käynnissä ja vision-malli ladattu (`ollama pull llama3.2-vision`)
4. Tiedostokoko alle 10MB
5. Kuva on tuettu muodossa (jpeg, png, webp)

Logit löytyvät konsolista (dev-moodissa Debug-tasolla).
