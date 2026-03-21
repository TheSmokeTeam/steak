# Steak

Steak is a self-hosted Kafka operator workspace built on `net10.0` with one codebase and three delivery targets:

- A **standalone Windows `.exe`** that needs nothing installed — no .NET runtime, no dependencies.
- A **Docker image** that serves the same UI and API from one container.
- A standard `dotnet run` developer workflow.

Connect to any Kafka cluster directly from the UI — no saved profiles, no config files. Three first-class workflows:

- **View**: browse topics, inspect offsets, headers, keys, UTF-8, JSON, base64, and hex previews.
- **Consume**: batch export Kafka messages to the local file system or S3.
- **Publish**: batch publish Steak envelope files from the file system or S3 back to Kafka, or publish a single envelope interactively.

## How It Works

Steak uses a **connect-first** model. Enter your Kafka connection details (bootstrap servers, SASL credentials, SSL PEMs) into the connection card and click Connect. All operations use that active session — nothing is persisted to disk.

## Quick Start

### 1. Start Kafka in Docker

```powershell
docker run -d --name steak-kafka -h steak-kafka `
  -p 9092:9092 `
  -e KAFKA_NODE_ID=1 `
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP='CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT' `
  -e KAFKA_ADVERTISED_LISTENERS='PLAINTEXT://steak-kafka:29092,PLAINTEXT_HOST://localhost:9092' `
  -e KAFKA_PROCESS_ROLES='broker,controller' `
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 `
  -e KAFKA_CONTROLLER_QUORUM_VOTERS='1@steak-kafka:29093' `
  -e KAFKA_LISTENERS='PLAINTEXT://steak-kafka:29092,CONTROLLER://steak-kafka:29093,PLAINTEXT_HOST://0.0.0.0:9092' `
  -e KAFKA_INTER_BROKER_LISTENER_NAME='PLAINTEXT' `
  -e KAFKA_CONTROLLER_LISTENER_NAMES='CONTROLLER' `
  -e CLUSTER_ID='MkU3OEVBNTcwNTJENDM2Qk' `
  confluentinc/cp-kafka:7.7.0
```

### 2. Run Steak

**Option A — dotnet run:**

```powershell
dotnet run --project .\src\Steak.Host\Steak.Host.csproj
```

**Option B — standalone .exe:**

```powershell
.\artifacts\publish\win-x64\Steak.exe
```

**Option C — Docker:**

```powershell
docker build -t steak-local .
docker run --rm -p 8080:8080 steak-local
```

Steak binds to `http://127.0.0.1:4040` locally or `http://0.0.0.0:8080` in Docker.

### 3. Connect

In the UI, enter:

- Bootstrap Servers: `localhost:9092`
- Leave Security Protocol and SASL Mechanism blank for a plaintext cluster.
- For secured clusters, friendly values such as `SaslPlaintext`, `SaslSsl`, `Plain`, `ScramSha256`, and `ScramSha512` are accepted.

Click **Connect**.

### 4. Use the Workspaces

- **View**: Refresh topics, select one, click Start Live View.
- **Publish**: Use Load Sample to get a template, click Publish.
- **Consume**: Pick a topic and group, choose a destination (file system or S3), click Start Export.

## Building a Standalone `.exe`

The standalone `.exe` is fully self-contained — it bundles the .NET runtime so nothing needs to be installed on the target machine.

```powershell
dotnet publish .\src\Steak.Host\Steak.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

Or use the publish profile:

```powershell
dotnet publish .\src\Steak.Host\Steak.Host.csproj -c Release -p:PublishProfile=Steak-win-x64
```

Output: `artifacts\publish\win-x64\`

Copy the output folder contents to any Windows x64 machine and run `Steak.Host.exe`. No .NET SDK or runtime installer needed.

## Docker

Build and run:

```powershell
docker build -t steak-local .
docker run --rm -p 8080:8080 -v ${PWD}/data:/data steak-local
```

Open `http://localhost:8080`. Swagger is at `/swagger`.

If Kafka is in Docker too, put them on the same network and use the container hostname as the bootstrap server.

## CI/CD

GitHub Actions automatically build, test, and push the Docker image to DockerHub on every push to `main`.

The workflow is defined in `.github/workflows/ci.yml` and pushes to the `smoketeam/steak` Docker Hub repository.

Required GitHub repository secrets:

- `DOCKERHUB_USERNAME`
- `DOCKERHUB_TOKEN`

## API

Steak exposes Minimal APIs under `/api`:

| Endpoint                    | Method | Description      |
| --------------------------- | ------ | ---------------- |
| `/api/connection`           | POST   | Connect to Kafka |
| `/api/connection`           | DELETE | Disconnect       |
| `/api/connection`           | GET    | Session status   |
| `/api/topics`               | GET    | List topics      |
| `/api/topics/{name}`        | GET    | Topic detail     |
| `/api/view-sessions`        | POST   | Start live view  |
| `/api/view-sessions`        | DELETE | Stop live view   |
| `/api/view-sessions/events` | GET    | SSE stream       |
| `/api/consume-jobs`         | POST   | Start export     |
| `/api/consume-jobs`         | DELETE | Stop export      |
| `/api/consume-jobs`         | GET    | Export status    |
| `/api/publish`              | POST   | Single publish   |
| `/api/batch-publish`        | POST   | Start batch      |
| `/api/batch-publish`        | DELETE | Stop batch       |
| `/api/batch-publish`        | GET    | Batch status     |

Use Swagger at `/swagger` for exploration.

## Steak Envelope Format

```json
{
  "app": "Steak",
  "connectionSessionId": "abc123",
  "topic": "orders",
  "keyBase64": "b3JkZXItNDI=",
  "valueBase64": "eyJvcmRlcklkIjo0Mn0=",
  "headers": [
    { "key": "content-type", "valueBase64": "YXBwbGljYXRpb24vanNvbg==" }
  ]
}
```

Key and value bytes are always base64. UTF-8, JSON, and hex previews are derived.

## Tests

```powershell
dotnet test .\tests\Steak.Tests\Steak.Tests.csproj
```

Covers: API endpoints, config building and masking, envelope normalization, preview decoding, file naming, key-value parsing, batch publish/consume lifecycle.
