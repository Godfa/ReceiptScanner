# MokkilanInvoices Integration Guide

Complete guide for integrating ReceiptScanner API with MokkilanInvoices frontend and backend.

## Architecture Overview

```
MokkilanInvoices Frontend (React)
           ↓
MokkilanInvoices Backend API (C#)
           ↓
ReceiptScanner API (this service)
           ↓
Azure / Ollama (AI providers)
```

## Integration Strategy

### Option 1: Direct Frontend → ReceiptScanner (Simple)

Frontend calls ReceiptScanner API directly.

**Pros:**
- Simple, no backend changes
- Fast, direct connection
- Easy to implement

**Cons:**
- CORS must be configured
- No centralized logging
- Harder to add auth later

### Option 2: Frontend → Backend → ReceiptScanner (Recommended)

Frontend calls MokkilanInvoices backend, which proxies to ReceiptScanner.

**Pros:**
- Centralized control
- Easy to add auth/logging
- Better security
- Can cache/transform results

**Cons:**
- Extra hop (minimal latency)
- More code to write

---

## Option 1: Direct Frontend Integration

### 1. Frontend Component (React/TypeScript)

Create a receipt scanner component with provider and model selection:

```typescript
// src/components/ReceiptScanner.tsx
import React, { useState } from 'react';
import axios from 'axios';

interface ReceiptScannerProps {
  onScanComplete: (result: ScannedReceiptResult) => void;
}

interface ScannedReceiptResult {
  provider: string;
  lines: Array<{
    description: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    confidence?: number;
  }>;
  total?: number;
  merchantName?: string;
  receiptDate?: string;
  warnings: string[];
  processingTimeMs: number;
}

const RECEIPT_SCANNER_API = 'http://localhost:5205';

export const ReceiptScanner: React.FC<ReceiptScannerProps> = ({ onScanComplete }) => {
  const [file, setFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(false);
  const [provider, setProvider] = useState<'auto' | 'azure' | 'ollama'>('auto');
  const [ollamaModel, setOllamaModel] = useState('llama3.2-vision');
  const [language, setLanguage] = useState('fi');
  const [result, setResult] = useState<ScannedReceiptResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      setFile(e.target.files[0]);
      setResult(null);
      setError(null);
    }
  };

  const scanReceipt = async () => {
    if (!file) return;

    setLoading(true);
    setError(null);

    try {
      // Option A: File upload (multipart/form-data)
      const formData = new FormData();
      formData.append('file', file);

      const response = await axios.post<ScannedReceiptResult>(
        `${RECEIPT_SCANNER_API}/api/receipts/scan-file`,
        formData,
        {
          params: {
            language,
            provider,
            ollamaModel: provider === 'ollama' ? ollamaModel : undefined
          },
          headers: {
            'Content-Type': 'multipart/form-data'
          }
        }
      );

      setResult(response.data);
      onScanComplete(response.data);
    } catch (err: any) {
      const message = err.response?.data?.error || err.message || 'Scan failed';
      setError(message);
      console.error('Receipt scan error:', err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="receipt-scanner">
      <h2>Skannaa kuitti</h2>

      {/* File Input */}
      <div className="form-group">
        <label>Valitse kuittikuva:</label>
        <input
          type="file"
          accept="image/jpeg,image/jpg,image/png,image/webp"
          onChange={handleFileChange}
          disabled={loading}
        />
      </div>

      {/* Provider Selection */}
      <div className="form-group">
        <label>Skannauspalvelu:</label>
        <select
          value={provider}
          onChange={(e) => setProvider(e.target.value as any)}
          disabled={loading}
        >
          <option value="auto">Automaattinen (Azure → Ollama)</option>
          <option value="azure">Azure AI (pilvi, maksullinen)</option>
          <option value="ollama">Ollama (paikallinen, ilmainen)</option>
        </select>
      </div>

      {/* Ollama Model Selection (only when provider is ollama) */}
      {provider === 'ollama' && (
        <div className="form-group">
          <label>Ollama-malli:</label>
          <select
            value={ollamaModel}
            onChange={(e) => setOllamaModel(e.target.value)}
            disabled={loading}
          >
            <option value="llama3.2-vision">llama3.2-vision (suositeltu)</option>
            <option value="llava">llava (nopea, pienempi)</option>
            <option value="llava:13b">llava:13b (tarkempi)</option>
            <option value="llava:34b">llava:34b (paras laatu)</option>
            <option value="bakllava">bakllava (vaihtoehto)</option>
          </select>
          <small>Vaihtoehto muuttaa tarkkuutta ja nopeutta</small>
        </div>
      )}

      {/* Language Selection */}
      <div className="form-group">
        <label>Kieli:</label>
        <select
          value={language}
          onChange={(e) => setLanguage(e.target.value)}
          disabled={loading}
        >
          <option value="fi">Suomi</option>
          <option value="en">English</option>
          <option value="sv">Svenska</option>
        </select>
      </div>

      {/* Scan Button */}
      <button
        onClick={scanReceipt}
        disabled={!file || loading}
        className="btn btn-primary"
      >
        {loading ? 'Skannataan...' : 'Skannaa kuitti'}
      </button>

      {/* Results */}
      {result && (
        <div className="scan-results">
          <h3>Skannaustulos</h3>
          <p>Palvelu: <strong>{result.provider}</strong></p>
          <p>Aika: {result.processingTimeMs}ms</p>

          {result.merchantName && <p>Kauppias: {result.merchantName}</p>}
          {result.receiptDate && <p>Päivämäärä: {new Date(result.receiptDate).toLocaleDateString()}</p>}

          <table>
            <thead>
              <tr>
                <th>Tuote</th>
                <th>Määrä</th>
                <th>Hinta</th>
                <th>Yhteensä</th>
              </tr>
            </thead>
            <tbody>
              {result.lines.map((line, idx) => (
                <tr key={idx}>
                  <td>{line.description}</td>
                  <td>{line.quantity}</td>
                  <td>{line.unitPrice.toFixed(2)} €</td>
                  <td>{line.lineTotal.toFixed(2)} €</td>
                </tr>
              ))}
            </tbody>
          </table>

          {result.total && (
            <p className="total"><strong>Loppusumma: {result.total.toFixed(2)} €</strong></p>
          )}

          {result.warnings.length > 0 && (
            <div className="warnings">
              <h4>Huomautukset:</h4>
              <ul>
                {result.warnings.map((warning, idx) => (
                  <li key={idx}>{warning}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* Error Display */}
      {error && (
        <div className="error-message">
          <strong>Virhe:</strong> {error}
        </div>
      )}
    </div>
  );
};
```

### 2. Configure CORS in ReceiptScanner

Update `Program.cs` to allow MokkilanInvoices frontend:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMokkilanInvoices", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3000",      // React dev server
            "http://localhost:5173",      // Vite dev server
            "https://your-production-domain.com"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

// Use the policy
app.UseCors("AllowMokkilanInvoices");
```

---

## Option 2: Backend Proxy Integration (Recommended)

### 1. MokkilanInvoices Backend Service (C#)

```csharp
// Services/ReceiptScannerService.cs
using System.Net.Http.Headers;

public interface IReceiptScannerService
{
    Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language = "fi",
        string provider = "auto",
        string? ollamaModel = null);
}

public class ReceiptScannerService : IReceiptScannerService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReceiptScannerService> _logger;

    public ReceiptScannerService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ReceiptScannerService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var baseUrl = configuration["ReceiptScanner:BaseUrl"] ?? "http://localhost:5205";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<ScannedReceiptResult> ScanReceiptAsync(
        byte[] imageData,
        string language = "fi",
        string provider = "auto",
        string? ollamaModel = null)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageData);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            content.Add(fileContent, "file", "receipt.jpg");

            var queryParams = $"?language={language}&provider={provider}";
            if (!string.IsNullOrEmpty(ollamaModel) && provider == "ollama")
            {
                queryParams += $"&ollamaModel={ollamaModel}";
            }

            var response = await _httpClient.PostAsync(
                $"/api/receipts/scan-file{queryParams}",
                content);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ScannedReceiptResult>();
            return result ?? throw new Exception("Empty response from scanner");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Receipt scanner API call failed");
            throw new Exception("Receipt scanning service unavailable", ex);
        }
    }
}

public record ScannedReceiptResult
{
    public string Provider { get; init; } = "";
    public List<ScannedReceiptLine> Lines { get; init; } = new();
    public decimal? Total { get; init; }
    public string? MerchantName { get; init; }
    public DateTime? ReceiptDate { get; init; }
    public List<string> Warnings { get; init; } = new();
    public long ProcessingTimeMs { get; init; }
}

public record ScannedReceiptLine
{
    public string Description { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public decimal? Confidence { get; init; }
}
```

### 2. Register Service in Program.cs

```csharp
// Program.cs
builder.Services.AddHttpClient<IReceiptScannerService, ReceiptScannerService>();
```

### 3. Add Configuration

```json
// appsettings.json
{
  "ReceiptScanner": {
    "BaseUrl": "http://localhost:5205"
  }
}
```

### 4. Controller Endpoint

```csharp
// Controllers/ReceiptsController.cs
[ApiController]
[Route("api/[controller]")]
public class ReceiptsController : ControllerBase
{
    private readonly IReceiptScannerService _scannerService;

    public ReceiptsController(IReceiptScannerService scannerService)
    {
        _scannerService = scannerService;
    }

    [HttpPost("scan")]
    [RequestSizeLimit(10_000_000)] // 10MB
    public async Task<ActionResult<ScannedReceiptResult>> ScanReceipt(
        IFormFile file,
        [FromQuery] string language = "fi",
        [FromQuery] string provider = "auto",
        [FromQuery] string? ollamaModel = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var imageData = ms.ToArray();

        var result = await _scannerService.ScanReceiptAsync(
            imageData,
            language,
            provider,
            ollamaModel);

        return Ok(result);
    }
}
```

### 5. Frontend (Simplified)

```typescript
// Now call your own backend instead
const BACKEND_API = '/api';  // Your MokkilanInvoices API

const scanReceipt = async () => {
  const formData = new FormData();
  formData.append('file', file);

  const response = await axios.post(
    `${BACKEND_API}/receipts/scan`,
    formData,
    {
      params: { language, provider, ollamaModel }
    }
  );

  setResult(response.data);
};
```

---

## Dynamic Model Selection from Frontend

### Backend Enhancement: Pass Ollama Model to ReceiptScanner

The ReceiptScanner API currently reads the model from `appsettings.json`. To make it dynamic:

**Option A: Add model parameter to API (requires ReceiptScanner changes)**

Modify `OllamaVisionProvider.cs`:

```csharp
public async Task<ScannedReceiptResult> ScanReceiptAsync(
    byte[] imageData,
    string language,
    string? modelOverride = null,  // NEW
    CancellationToken cancellationToken = default)
{
    var model = modelOverride
        ?? _configuration["Ollama:Model"]
        ?? "llama3.2-vision";

    // Use 'model' instead of reading from config
}
```

**Option B: Use appsettings per environment (simpler)**

Different models for different purposes:
- Dev: `llava` (fast testing)
- Staging: `llama3.2-vision` (production-like)
- Prod: `llama3.2-vision` (best quality)

**Option C: Restart ReceiptScanner when changing model**

Frontend can provide model selection UI, but actual change requires:
1. User selects model in frontend
2. Frontend tells backend to update appsettings
3. Backend restarts ReceiptScanner API

---

## Provider & Model Selection UI Examples

### Simple Dropdown

```typescript
<select value={provider} onChange={(e) => setProvider(e.target.value)}>
  <option value="auto">🤖 Automaattinen (paras tulos)</option>
  <option value="azure">☁️ Azure AI (nopea, pilvi)</option>
  <option value="ollama">💻 Ollama (ilmainen, paikallinen)</option>
</select>
```

### Advanced Settings Panel

```typescript
const [showAdvanced, setShowAdvanced] = useState(false);

<>
  <button onClick={() => setShowAdvanced(!showAdvanced)}>
    {showAdvanced ? '▼' : '▶'} Lisäasetukset
  </button>

  {showAdvanced && (
    <div className="advanced-settings">
      <label>
        Provider:
        <select value={provider} onChange={(e) => setProvider(e.target.value)}>
          <option value="auto">Automaattinen</option>
          <option value="azure">Azure AI</option>
          <option value="ollama">Ollama</option>
        </select>
      </label>

      {provider === 'ollama' && (
        <>
          <label>
            Malli:
            <select value={ollamaModel} onChange={(e) => setOllamaModel(e.target.value)}>
              <option value="llama3.2-vision">llama3.2-vision (suositeltu)</option>
              <option value="llava">llava (nopein)</option>
              <option value="llava:13b">llava:13b (tarkempi)</option>
            </select>
          </label>
          <p className="help-text">
            Nopeampi malli = vähemmän tarkkuutta, hitaampi = parempi tarkkuus
          </p>
        </>
      )}
    </div>
  )}
</>
```

---

## Testing the Integration

### 1. Start Services

```bash
# Terminal 1: ReceiptScanner API
cd ReceiptScanner/API
dotnet run

# Terminal 2: MokkilanInvoices Backend
cd MokkilanInvoices/API
dotnet run

# Terminal 3: MokkilanInvoices Frontend
cd MokkilanInvoices/ClientApp
npm start
```

### 2. Test with cURL

```bash
# Direct to ReceiptScanner
curl -X POST http://localhost:5205/api/receipts/scan-file \
  -F "file=@receipt.jpg" \
  -F "language=fi" \
  -F "provider=ollama"

# Through MokkilanInvoices backend
curl -X POST http://localhost:5000/api/receipts/scan \
  -F "file=@receipt.jpg" \
  -F "language=fi" \
  -F "provider=ollama" \
  -F "ollamaModel=llava"
```

---

## Recommendation

**Use Option 2 (Backend Proxy)** for production:
- ✅ Better security
- ✅ Centralized logging
- ✅ Easy to add caching
- ✅ Can transform/validate results
- ✅ Easier to add auth later

**Frontend provides:**
- Provider selection (auto/azure/ollama)
- Model selection (when provider=ollama)
- Language selection (fi/en/sv)

**Backend handles:**
- API communication
- Error handling
- Logging
- Result transformation

This gives users full control while keeping the architecture clean! 🚀
