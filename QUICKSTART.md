# Quick Start Guide

Get the Receipt Scanner API up and running in 5 minutes.

## 1. Prerequisites

- .NET 10 SDK installed
- (Optional) Azure AI Document Intelligence resource
- (Optional) Ollama with llama3.2-vision model

## 2. Clone and Build

```bash
cd ReceiptScanner/API
dotnet build
```

## 3. Configure Secrets (Development)

### Option A: Azure Document Intelligence (Recommended)

```bash
cd API
dotnet user-secrets set "Azure:DocumentIntelligence:Endpoint" "https://your-resource.cognitiveservices.azure.com/"
dotnet user-secrets set "Azure:DocumentIntelligence:ApiKey" "your-api-key"
```

### Option B: Ollama (Local/Free)

1. Install Ollama: https://ollama.ai/
2. Pull vision model: `ollama pull llama3.2-vision`
3. Start Ollama: `ollama serve`

Ollama is pre-configured in `appsettings.json` to use `http://localhost:11434`

## 4. Run the API

```bash
cd API
dotnet run
```

API starts at: **http://localhost:5205** (or the port shown in the console)

## 5. Test it

Open your browser and navigate to:
```
http://localhost:5205
```

You'll see the Swagger UI where you can test all endpoints.

### Quick Health Check

```bash
curl http://localhost:5205/api/receipts/health
```

Expected response:
```json
{
  "status": "healthy",
  "availableProviders": ["azure", "ollama"],
  "providerCount": 2,
  "timestamp": "2026-01-13T15:41:47Z"
}
```

### Test with a Receipt Image

1. Take a photo of a receipt
2. Open Swagger UI at http://localhost:5205
3. Try the `POST /api/receipts/scan-file` endpoint
4. Upload your receipt image
5. Get structured JSON with line items!

## 6. What's Next?

- Read [CONFIGURATION.md](CONFIGURATION.md) for detailed configuration options
- Read [README.md](README.md) for full API documentation
- Integrate with your application using the REST API

## Troubleshooting

### "No providers available"

- Check that you configured at least one provider (Azure or Ollama)
- Verify your User Secrets: `dotnet user-secrets list`
- For Ollama: ensure it's running (`ollama serve`)

### "Cannot connect to Ollama"

```bash
# Check if Ollama is running
curl http://localhost:11434/api/version

# Start Ollama if not running
ollama serve

# Verify model is installed
ollama list
```

### Port already in use

The API will use the port defined in `Properties/launchSettings.json`. If the default port is in use, it will automatically try another port.

## Example Integration Code

### C# / .NET

```csharp
var client = new HttpClient { BaseAddress = new Uri("http://localhost:5205") };

var request = new
{
    imageBase64 = Convert.ToBase64String(File.ReadAllBytes("receipt.jpg")),
    language = "fi",
    provider = "auto"
};

var response = await client.PostAsJsonAsync("/api/receipts/scan", request);
var result = await response.Content.ReadFromJsonAsync<ScannedReceiptResult>();

foreach (var line in result.Lines)
{
    Console.WriteLine($"{line.Description}: {line.LineTotal:C}");
}
```

### JavaScript / TypeScript

```typescript
const imageFile = document.getElementById('receiptInput').files[0];
const formData = new FormData();
formData.append('file', imageFile);

const response = await fetch('http://localhost:5205/api/receipts/scan-file?language=fi&provider=auto', {
    method: 'POST',
    body: formData
});

const result = await response.json();
console.log('Total:', result.total);
console.log('Items:', result.lines);
```

### Python

```python
import requests
import base64

with open('receipt.jpg', 'rb') as f:
    image_data = base64.b64encode(f.read()).decode('utf-8')

response = requests.post('http://localhost:5205/api/receipts/scan', json={
    'imageBase64': image_data,
    'language': 'fi',
    'provider': 'auto'
})

result = response.json()
for line in result['lines']:
    print(f"{line['description']}: {line['lineTotal']}")
```

## Need Help?

- Check the logs in the console output
- Read the full documentation in [README.md](README.md)
- Configuration issues? See [CONFIGURATION.md](CONFIGURATION.md)
