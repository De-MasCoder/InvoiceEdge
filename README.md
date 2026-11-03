# InvoiceEdge

InvoiceEdge is a .NET 10 background worker that monitors an invoice drop folder, extracts invoice data with a local Ollama vision model, persists the results to PostgreSQL, and archives successfully processed documents to Amazon S3.

The worker supports:

- PDF first-page rasterization to PNG.
- Native image input such as PNG, JPG, and JPEG.
- AI extraction of vendor, total, invoice date, and line items.
- Relational persistence of invoices and line items with Entity Framework Core.
- Content-hash idempotency to prevent the same file content from being processed more than once.
- Local file routing through Inbound, Processing, Processed, and Failed folders.
- Private S3 uploads using the AWS SDK.
- Automated unit tests for parsing, hashing, PDF conversion, and worker lifecycle behavior.

## Project structure

```text
InvoiceEdge/
├── InvoiceEdge.slnx
├── InvoiceEdgeWorker/
│   ├── Configuration/       Runtime configuration models
│   ├── Data/                EF Core DbContext
│   ├── Entities/            Invoice and invoice line-item models
│   ├── Helpers/             PDF conversion and SHA-256 helpers
│   ├── Migrations/          EF Core database migrations
│   ├── Repositories/        Repository and unit-of-work implementations
│   ├── Services/            Ollama, parsing, and S3 services
│   ├── Worker.cs            Background file-processing service
│   ├── Program.cs           Dependency injection and host setup
│   └── appsettings*.json    Application configuration
└── InvoiceEdgeWorker.Tests/
	└── Unit and lifecycle tests
```

## Processing workflow

1. The worker creates the configured folder structure.
2. A new file is detected in the Inbound folder.
3. The worker waits briefly for the file copy to finish.
4. A SHA-256 hash is calculated from the complete file content.
5. Existing invoices are checked for the same hash. Duplicate content is skipped and moved to Processed.
6. New files are moved to Processing and initially saved with `Processing` status.
7. PDFs are converted to a PNG image in memory. Native images are read directly.
8. The image is sent to the configured Ollama vision model.
9. The JSON response is validated, including invoice line items.
10. The invoice and line items are persisted.
11. The source document is uploaded to the configured private S3 bucket.
12. After a successful S3 upload, the local file is moved to Processed.
13. Files that fail processing remain recoverable through the Failed-folder workflow.

## Prerequisites

Install or provision the following before running locally:

- Windows, Linux, or macOS. The default local paths in configuration are Windows paths and should be changed on other operating systems.
- .NET 10 SDK.
- PostgreSQL 16 or later.
- Ollama.
- The configured Ollama vision model, by default `qwen2.5vl:7b`.
- An AWS account with:
  - An S3 bucket.
  - The bucket's actual AWS region.
  - Credentials that can upload objects to the bucket.
- The Entity Framework Core CLI tool:

```powershell
dotnet tool install --global dotnet-ef
```

If it is already installed, update it when necessary:

```powershell
dotnet tool update --global dotnet-ef
```

Verify the local SDK and tools:

```powershell
dotnet --version
dotnet ef --version
ollama --version
```

## PostgreSQL setup

Create a database and user, or use an existing local PostgreSQL instance. The default development connection string is:

```text
Host=localhost;Port=5432;Database=invoice_db;Username=postgres;Password=postgres;
```

Update `InvoiceEdgeWorker/appsettings.Development.json` or use an environment variable/user secret for a different connection string. Do not commit production credentials.

Apply the current EF Core migrations:

```powershell
dotnet ef database update `
  --project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj `
  --startup-project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj
```

The migrations create the invoice schema, line-item table, and content-hash idempotency index.

To create a new migration after changing the entity model:

```powershell
dotnet ef migrations add MigrationName `
  --project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj `
  --startup-project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj
```

## Ollama setup

Install Ollama, start the Ollama service, and pull the configured vision model:

```powershell
ollama serve
ollama pull qwen2.5vl:7b
```

The default configuration expects Ollama at:

```text
http://localhost:11434
```

Change these values under `InvoiceProcessing` when using a different endpoint or model:

```json
{
  "InvoiceProcessing": {
	"OllamaEndpoint": "http://localhost:11434",
	"OllamaModel": "qwen2.5vl:7b"
  }
}
```

The model must support image input. A text-only Ollama model will not reliably extract invoice data from PDF images or photographs.

## AWS S3 setup

Configure the actual bucket name and region in local configuration:

```json
{
  "AWS": {
	"Region": "eu-west-1",
	"BucketName": "your-real-bucket-name"
  }
}
```

`AWS:Region` must match the bucket's actual region. An S3 endpoint error stating that the bucket must be addressed using a specified endpoint usually means this value is wrong.

The application does not store AWS access keys in `appsettings.json`. The AWS SDK uses its standard credential provider chain. Use one of the following approaches locally:

### AWS CLI profile

```powershell
aws configure --profile invoiceedge
$env:AWS_PROFILE = "invoiceedge"
```

### Environment variables

```powershell
$env:AWS_ACCESS_KEY_ID = "your-access-key"
$env:AWS_SECRET_ACCESS_KEY = "your-secret-key"
$env:AWS_REGION = "eu-west-1"
```

### IAM role

When running on AWS infrastructure, prefer an IAM role instead of long-lived access keys.

The IAM identity needs permission to upload objects to the configured bucket, for example:

```json
{
  "Version": "2012-10-17",
  "Statement": [
	{
	  "Effect": "Allow",
	  "Action": ["s3:PutObject"],
	  "Resource": "arn:aws:s3:::your-real-bucket-name/invoices/*"
	}
  ]
}
```

Invoice objects are uploaded under `invoices/yyyy/MM/` and are configured as private objects.

## Local folder configuration

The default folder configuration is:

```json
{
  "InvoiceProcessing": {
	"InboundPath": "C:\\InvoiceDropZone\\Inbound",
	"ProcessingPath": "C:\\InvoiceDropZone\\Processing",
	"ProcessedPath": "C:\\InvoiceDropZone\\Processed",
	"FailedPath": "C:\\InvoiceDropZone\\Failed"
  }
}
```

The worker creates these directories if they do not exist. To use a different location, update the configuration before starting the worker. For Linux or macOS, use paths such as `/var/lib/invoice-edge/inbound`.

To test the worker locally:

1. Start PostgreSQL.
2. Apply the EF migrations.
3. Start Ollama and verify the model is available.
4. Configure AWS credentials, bucket name, and bucket region.
5. Start the worker.
6. Copy a supported invoice file into the configured Inbound folder.
7. Monitor the console logs and inspect the Processed or Failed folder.

## Run locally

Restore dependencies and build the solution:

```powershell
dotnet restore InvoiceEdge.slnx
dotnet build InvoiceEdge.slnx
```

Start the worker using the Development environment:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj
```

On Windows PowerShell, the worker can also be started from the repository root with:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"; dotnet run --project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj
```

## Run tests

Run all tests through the solution:

```powershell
dotnet test InvoiceEdge.slnx
```

The test project covers:

- Invoice JSON parsing and validation.
- Markdown-fenced AI JSON responses.
- Invoice line-item extraction.
- Malformed extraction responses.
- SHA-256 content hashing.
- PDF-to-image conversion.
- Worker directory initialization and shutdown.

## Configuration and secrets

Configuration is loaded from `appsettings.json`, environment-specific settings, environment variables, and user secrets according to the standard .NET host configuration order.

Do not commit any of the following:

- Production PostgreSQL passwords.
- AWS access keys or secret keys.
- Production S3 bucket names if they are sensitive.
- Private Ollama endpoints or credentials.

For local development, user secrets can be used for connection strings:

```powershell
dotnet user-secrets --project InvoiceEdgeWorker\InvoiceEdgeWorker.csproj set "ConnectionStrings:PostgresConnection" "Host=localhost;Port=5432;Database=invoice_db;Username=postgres;Password=your-password"
```

## Docker notes

A Dockerfile is included for container builds:

```powershell
docker build -t invoice-edge-worker -f InvoiceEdgeWorker\Dockerfile .
docker run --rm --name invoice-edge-worker invoice-edge-worker
```

Before running in a container, configure the following explicitly:

- Container-mounted input, processing, processed, and failed directories.
- PostgreSQL network connectivity.
- Ollama network connectivity. `localhost` inside a container refers to the container, not the host machine.
- AWS region, bucket name, and credentials.

For Ollama running on the host, use a host-reachable endpoint such as `http://host.docker.internal:11434` where supported, or place Ollama and the worker on the same Docker network.

The default Docker image runs as a non-root user. Ensure mounted directories are writable by the container user.

## Troubleshooting

### S3 endpoint or region error

Verify that `AWS:Region` exactly matches the bucket's region. Also verify that the configured bucket name is the real bucket name and not a placeholder.

### File not found during S3 upload

The worker uploads the file while it is still in the Processing folder and moves it to Processed only after the upload succeeds. If this error occurs, verify that the Processing directory is writable and that another process is not moving or deleting the file.

### Ollama connection failure

Verify that Ollama is running and that the configured endpoint is reachable:

```powershell
ollama list
```

### PostgreSQL connection failure

Verify PostgreSQL is running, the database exists, the connection string is correct, and migrations have been applied.

### Files remain in Failed

Inspect the worker log for the original exception. Common causes include invalid AI JSON, unavailable Ollama, inaccessible PostgreSQL, unavailable S3, or file permissions.

## License

MIT
