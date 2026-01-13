# Configuration Guide

## Development Environment (User Secrets)

For local development, use .NET User Secrets to store sensitive configuration like API keys. **Never commit API keys to source control.**

### Setting up Azure Document Intelligence

```bash
cd API
dotnet user-secrets set "Azure:DocumentIntelligence:Endpoint" "https://your-resource.cognitiveservices.azure.com/"
dotnet user-secrets set "Azure:DocumentIntelligence:ApiKey" "your-api-key-here"
```

### Setting up Ollama (Optional)

Ollama settings are in `appsettings.json` since they're not sensitive:
- Default URL: `http://localhost:11434`
- Default model: `llama3.2-vision`

**Changing Ollama model:**

Edit `appsettings.json` or `appsettings.Development.json`:
```json
{
  "Ollama": {
    "Enabled": true,
    "BaseUrl": "http://localhost:11434",
    "Model": "llava"  // Change this
  }
}
```

**Available vision models:**
- `llama3.2-vision` - Recommended, best quality (11B params, ~7GB)
- `llava` - Smaller, faster (7B params, ~4.7GB)
- `llava:13b` - Better quality, slower (13B params, ~8GB)
- `llava:34b` - Best quality, slowest (34B params, ~20GB)
- `bakllava` - Alternative vision model (~5GB)

**Install a model:**
```bash
ollama pull llama3.2-vision  # or any other model
ollama list                   # See installed models
```

To override in User Secrets or environment variables:
```bash
dotnet user-secrets set "Ollama:BaseUrl" "http://your-ollama-host:11434"
dotnet user-secrets set "Ollama:Model" "llava"
```

### Viewing Your Secrets

```bash
cd API
dotnet user-secrets list
```

### Removing Secrets

```bash
dotnet user-secrets remove "Azure:DocumentIntelligence:ApiKey"
```

## Production Environment (Environment Variables)

In production, use environment variables or a secure configuration provider (Azure Key Vault, AWS Secrets Manager, etc.).

### Environment Variable Names

```bash
Azure__DocumentIntelligence__Endpoint=https://your-resource.cognitiveservices.azure.com/
Azure__DocumentIntelligence__ApiKey=your-api-key-here
Ollama__BaseUrl=http://ollama-host:11434
Ollama__Model=llama3.2-vision
```

**Note:** ASP.NET Core uses double underscores (`__`) to represent configuration hierarchy in environment variables.

### Docker Example

```dockerfile
ENV Azure__DocumentIntelligence__Endpoint="https://your-resource.cognitiveservices.azure.com/"
ENV Azure__DocumentIntelligence__ApiKey="your-api-key"
```

### Azure App Service

Configure these in the Application Settings section of your App Service.

### Kubernetes

Use Kubernetes Secrets:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: receipt-scanner-secrets
type: Opaque
stringData:
  azure-endpoint: "https://your-resource.cognitiveservices.azure.com/"
  azure-apikey: "your-api-key"
```

## Configuration Priority

ASP.NET Core loads configuration in this order (later sources override earlier):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User Secrets (Development only)
4. Environment Variables
5. Command-line arguments

## Security Best Practices

- ✅ Use User Secrets for local development
- ✅ Use environment variables or Key Vault for production
- ✅ Never commit `appsettings.json` with real API keys
- ✅ Rotate API keys regularly
- ❌ Never hardcode secrets in source code
- ❌ Never commit User Secrets (they're stored outside the project)

## Verifying Configuration

Check which providers are available:

```bash
curl http://localhost:5205/api/receipts/providers
```

Expected response:
```json
{
  "providers": ["azure", "ollama"],
  "count": 2
}
```

If a provider is missing, check its configuration using the health endpoint:

```bash
curl http://localhost:5205/api/receipts/health
```
