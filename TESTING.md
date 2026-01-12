# Testausohje

## Nopea testaus Swagger UI:lla

1. Käynnistä sovellus:
   ```bash
   cd API
   dotnet watch run
   ```

2. Avaa selaimella: `https://localhost:5001`

3. Kokeile endpointteja:
   - **GET /api/receipts/health** - Tarkista että palvelu toimii
   - **GET /api/receipts/providers** - Näe käytettävissä olevat providerit
   - **POST /api/receipts/scan-file** - Lataa kuittikuva ja skannaa

## Testikuitin luominen

Jos sinulla ei ole oikeaa kuittikuvaa, voit luoda testikuitin:

### Vaihtoehto 1: Käytä olemassa olevaa kuittia

Ota valokuva tai skannaa oikea kuitti ja käytä sitä testaukseen.

### Vaihtoehto 2: Luo yksinkertainen testikuitti

Luo tekstieditori + screenshot -yhdistelmä:

```
K-MARKET KESKUSTA
Helsinki

2026-01-12  14:30

MAITO 1L         1.29 €
LEIPÄ x 2        5.00 €
JUUSTO 500G      4.50 €
KAHVI 500G       7.99 €
--------------------------
YHTEENSÄ        18.78 €

Kiitos käynnistä!
```

Tallenna kuvakaappauksena ja lataa Swaggerin kautta.

## Testaus cURL:lla

### 1. Skannaa kuitti tiedostosta

```bash
curl -X POST https://localhost:5001/api/receipts/scan-file \
  -F "file=@path/to/receipt.jpg" \
  -F "language=fi" \
  -F "provider=auto" \
  --insecure
```

### 2. Skannaa kuitti base64:sta

```bash
# Muunna kuva base64:ksi
BASE64_IMAGE=$(base64 -w 0 receipt.jpg)

# Lähetä API:lle
curl -X POST https://localhost:5001/api/receipts/scan \
  -H "Content-Type: application/json" \
  -d "{
    \"imageBase64\": \"$BASE64_IMAGE\",
    \"language\": \"fi\",
    \"provider\": \"auto\"
  }" \
  --insecure
```

### 3. Tarkista providerit

```bash
curl https://localhost:5001/api/receipts/providers --insecure
```

## Odotettu vastaus

Onnistunut skannaus palauttaa:

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
  "total": 18.78,
  "merchantName": "K-Market Keskusta",
  "receiptDate": "2026-01-12T14:30:00",
  "warnings": [],
  "processingTimeMs": 2453
}
```

## Vianetsintä

### Ei yhtään provideria käytettävissä

**Ongelma:** `"No scanner providers are configured"`

**Ratkaisu:**
1. Tarkista `appsettings.Development.json`
2. Varmista että ainakin toinen provider on konfiguroitu:
   - **Azure**: Aseta `Endpoint` ja `ApiKey`
   - **Ollama**: Aseta `BaseUrl` ja varmista että Ollama on käynnissä

### Ollama ei toimi

**Ongelma:** `"Ollama scan error: ..."`

**Ratkaisu:**
1. Tarkista että Ollama on käynnissä: `ollama serve`
2. Tarkista että vision-malli on ladattu: `ollama pull llama3.2-vision`
3. Testaa Ollama erikseen:
   ```bash
   curl http://localhost:11434/api/tags
   ```

### Azure ei toimi

**Ongelma:** `"Azure scan error: ..."`

**Ratkaisu:**
1. Tarkista Azure-resurssin osoite ja API-avain
2. Varmista että Document Intelligence -resurssi on luotu Azure-portaalissa
3. Tarkista että resurssilla on prebuilt-receipt -malli käytössä

### Kuva ei skannaudu oikein

**Vinkkejä:**
- Käytä selkeää, hyvälaatuista kuvaa
- Varmista että teksti on luettavissa
- Kokeile eri providereitä
- Tarkista `warnings`-kenttä vastauksesta
- Pyydä `rawText` mukaan nähdäksesi mitä OCR näki

## Mock-mode testaus

Jos haluat testata ilman oikeita providereitä, voit luoda mock-providerin:

```csharp
// API/Services/MockReceiptProvider.cs
public class MockReceiptProvider : IReceiptScannerProvider
{
    public string ProviderName => "mock";

    public bool IsAvailable() => true;

    public Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language,
        CancellationToken ct)
    {
        return Task.FromResult(new ScannedReceiptResult
        {
            Provider = "mock",
            Lines = new List<ScannedReceiptLine>
            {
                new() { Description = "Test Item 1", Quantity = 1, UnitPrice = 10.0m, LineTotal = 10.0m },
                new() { Description = "Test Item 2", Quantity = 2, UnitPrice = 5.0m, LineTotal = 10.0m }
            },
            Total = 20.0m,
            MerchantName = "Mock Store",
            ProcessingTimeMs = 100
        });
    }
}
```

Rekisteröi mock Program.cs:ssä:
```csharp
builder.Services.AddSingleton<IReceiptScannerProvider, MockReceiptProvider>();
```

## Load Testing

Jos haluat testata suorituskykyä:

```bash
# Apache Bench
ab -n 100 -c 10 -p receipt.json -T application/json \
  https://localhost:5001/api/receipts/scan

# k6 (jos asennettu)
k6 run load-test.js
```

## Integraatiotestaus MokkilanInvoices:n kanssa

Katso yksityiskohdat: [../ReactCoreInvoices/RECEIPT_SCANNER_INTEGRATION.md](../ReactCoreInvoices/RECEIPT_SCANNER_INTEGRATION.md)

Perusidea:
1. Käynnistä Receipt Scanner API portissa 5001
2. Konfiguroi MokkilanInvoices käyttämään `http://localhost:5001`
3. Testaa kuittien lataus MokkilanInvoices-käyttöliittymästä
