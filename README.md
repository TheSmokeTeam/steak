# Steak

Steak is a self-hosted Kafka operator workspace built on `net10.0`. It gives you one local web UI for four day-to-day Kafka tasks:

- Connect to a Kafka cluster directly from the browser.
- Discover and inspect topics.
- View live messages with decoded payload previews.
- Consume messages to the file system or S3, then publish them back from the file system or S3.

Steak ships in three ways:

- A self-contained Windows build centered on `Steak.exe`
- A Docker image
- A normal `dotnet run` development workflow

Steak does not persist Kafka credentials or connection profiles. You connect for the current session, work against that active session, and disconnect when you are done.

## What You Can Do

### Connect and discover topics

- Connect to plaintext Kafka with only `bootstrap.servers`, for example `localhost:9092`.
- Connect to secured Kafka with SASL and SSL values from the UI.
- Refresh cluster metadata and list all visible topics from the connected broker.
- Use friendly Kafka values such as `Plaintext`, `SaslPlaintext`, `SaslSsl`, `Plain`, `ScramSha256`, and `ScramSha512`.

### View messages

- Select a topic and start a live view session.
- Inspect partition, offset, timestamp, and headers.
- See UTF-8, pretty JSON, base64, and hex previews of message payloads.

### Consume messages

- Export messages from a topic to the local file system.
- Export messages to S3.
- Choose offset mode, partition scope, throughput limit, and maximum message count.

### Publish messages

- Publish a single Steak envelope interactively from the UI.
- Batch publish Steak envelope files from the file system.
- Batch publish Steak envelope files from S3.
- Override the destination topic during batch publish when needed.

## Run Modes

### 1. Windows release mode (`.exe`)

This is the mode to use if you want a Windows executable without installing the .NET runtime or SDK.

Each Git tag produces a GitHub release with:

- `Steak.exe`
- `Steak-win-x64-portable.zip`

The recommended release asset is `Steak-win-x64-portable.zip`. It contains the full published layout. `Steak.exe` is also attached separately from the same build.

Steps:

1. Download the latest release assets.
2. Extract `Steak-win-x64-portable.zip` to a folder of your choice.
3. Run `Steak.exe`.
4. Open `http://127.0.0.1:4040` in your browser.

Notes:

- The Windows build is self-contained. No .NET runtime installation is required.
- Steak no longer auto-opens your browser. It starts in the background and waits for you to open the URL yourself.
- Steak stores mutable data in the first writable location it can use:
  `%LOCALAPPDATA%\\Steak`, then `.steak` next to the executable, then `.steak` under the content root, then the system temp folder.

### 2. Docker mode

Pull from Docker Hub:

```powershell
docker pull <docker-username>/steak:latest
```

Run the container:

```powershell
docker run --rm `
  -p 8080:8080 `
  -v ${PWD}/data:/data `
  <docker-username>/steak:latest
```

Open:

- App: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`

Notes:

- The container stores exported data under `/data` by default.
- Browser auto-launch is disabled in Docker mode.
- If Kafka is also running in Docker, put Steak and Kafka on the same network and use the Kafka container hostname.

### 3. Source mode (`dotnet run`)

```powershell
dotnet restore
dotnet run --project .\src\Steak.Host\Steak.Host.csproj
```

Open `http://127.0.0.1:4040`.

## Local Kafka Quick Start

### Kafka on the host

If Kafka is reachable from Windows directly:

- Use `localhost:9092` in Steak.
- Leave Security Protocol blank for plaintext.
- Leave SASL Mechanism blank for plaintext.

### Kafka in Docker

Create a shared network:

```powershell
docker network create steak-net
```

Start Kafka:

```powershell
docker run -d --name steak-kafka -h steak-kafka `
  --network steak-net `
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

If Steak is running:

- On Windows host: use `localhost:9092`
- In Docker on `steak-net`: use `steak-kafka:29092`

## Using The UI

### Connect

1. Enter bootstrap servers.
2. Fill in SASL or SSL settings only if your cluster needs them.
3. Click `Connect`.
4. Use `Refresh Topics` to reload cluster metadata.

If your local Docker Kafka is reachable, Steak should automatically discover all topics visible to the broker you connected to.

### View workspace

1. Connect to Kafka.
2. Open the `View` workspace.
3. Click `Refresh Topics`.
4. Select a topic.
5. Click `Start Live View`.
6. Select a message row to inspect headers and decoded payload previews.

You can stop the live session at any time from the same workspace.

### Consume workspace

1. Connect to Kafka.
2. Open the `Consume` workspace.
3. Select a topic.
4. Enter a consumer group.
5. Choose partition scope, offset mode, max messages, and throughput if needed.
6. Choose `FileSystem` or `S3` as the destination.
7. Click `Start Export`.

For file system exports:

- Use any writable folder path.
- Steak writes one envelope file per message.

For S3 exports:

- Set bucket, region, prefix, access key, and secret key.

### Publish workspace

#### Single publish

1. Connect to Kafka.
2. Open the `Publish` workspace.
3. Click `Refresh Topics`.
4. Choose a topic.
5. Click `Load Sample` to load a valid Steak envelope.
6. Edit the envelope if needed.
7. Click `Preview` to validate the decoded payload.
8. Click `Publish`.

#### Batch publish

1. Connect to Kafka.
2. Open the `Publish` workspace.
3. Choose `FileSystem` or `S3` as the source transport.
4. Point Steak at a folder or S3 prefix that contains Steak envelope files.
5. Optionally choose `Topic Override`.
6. Set max messages and throughput if needed.
7. Click `Start Batch Publish`.

## Building From Source

### Build and test

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

### Publish the Windows standalone build

```powershell
dotnet publish .\src\Steak.Host\Steak.Host.csproj -c Release -p:PublishProfile=Steak-win-x64
```

Publish output:

```text
artifacts\publish\win-x64\
```

### Build the Docker image locally

```powershell
docker build -t steak-local:test .
```

Run it:

```powershell
docker run --rm -p 8080:8080 -v ${PWD}/data:/data steak-local:test
```

## API

Swagger is available at `/swagger`.

Key endpoints:

| Endpoint                    | Method | Purpose              |
| --------------------------- | ------ | -------------------- |
| `/api/connection`           | POST   | Connect to Kafka     |
| `/api/connection`           | GET    | Inspect session      |
| `/api/connection`           | DELETE | Disconnect           |
| `/api/topics`               | GET    | List topics          |
| `/api/topics/{name}`        | GET    | Topic details        |
| `/api/view-sessions`        | POST   | Start live view      |
| `/api/view-sessions`        | DELETE | Stop live view       |
| `/api/view-sessions/events` | GET    | Receive SSE events   |
| `/api/consume-jobs`         | POST   | Start export         |
| `/api/consume-jobs`         | GET    | Export status        |
| `/api/consume-jobs`         | DELETE | Stop export          |
| `/api/publish`              | POST   | Publish one envelope |
| `/api/batch-publish`        | POST   | Start batch publish  |
| `/api/batch-publish`        | GET    | Batch status         |
| `/api/batch-publish`        | DELETE | Stop batch publish   |

## Steak Envelope Format

```json
{
  "app": "Steak",
  "connectionSessionId": "abc123",
  "topic": "orders",
  "keyBase64": "b3JkZXItNDI=",
  "valueBase64": "eyJvcmRlcklkIjo0Mn0=",
  "headers": [
    {
      "key": "content-type",
      "valueBase64": "YXBwbGljYXRpb24vanNvbg=="
    }
  ]
}
```

Notes:

- `keyBase64` and `valueBase64` are raw bytes encoded as base64.
- Preview fields are derived inside Steak.
- Batch publish can respect the envelope topic or override it from the UI.

## Verification Scripts

### Full Kafka smoke

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\local-kafka-smoke.ps1
```

This validates:

- topic discovery
- view sessions
- single publish
- consume export
- batch publish
- end-to-end Kafka round-trips

### Headless UI smoke

```powershell
npm install
npm run ui-smoke
```

This validates the main UI workflows without opening a visible browser window.

Optional environment variables:

- `BASE_URL` to target a different Steak host
- `KAFKA_BOOTSTRAP_SERVERS` to target Kafka reachable from that host
- `UI_BACKEND_EXPORT_DIR` for the path Steak should write to
- `UI_EXPECTED_EXPORT_DIR` for the path the smoke test should inspect on the machine running Playwright

### Standalone executable smoke

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-standalone.ps1
```

This validates:

- the published `artifacts\publish\win-x64\Steak.exe`
- an isolated copy of only `Steak.exe`

### Full `.exe` workflow smoke against local Kafka

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-exe-smoke.ps1
```

This starts the published `Steak.exe`, runs the Kafka API smoke, runs the headless UI smoke, and stops the process again.

## CI And Releases

### Continuous integration

`.github/workflows/ci.yml` runs on pushes to `main`, pull requests to `main`, and tag pushes.

It:

- restores, builds, and tests the solution
- publishes Docker images to `<DOCKER_USERNAME>/steak`
- uses Docker Hub credentials from:
  - `DOCKER_USERNAME`
  - `DOCKER_PASSWORD`

Docker tags:

- `latest`
- the GitHub Actions run number
- the Git tag name on tag builds

### GitHub releases

`.github/workflows/release.yml` runs on every pushed tag.

It:

- builds and tests the app on Windows
- publishes the self-contained Windows executable
- runs the standalone smoke test
- attaches `Steak.exe` to the GitHub release
- attaches `Steak-win-x64-portable.zip` to the GitHub release
