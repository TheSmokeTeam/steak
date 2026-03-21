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

Steak does not persist Kafka credentials or connection profiles. You can open multiple connection tabs for different Kafka clusters simultaneously, work against each active session, and disconnect individually when you are done.

## What You Can Do

### Connect and discover topics

- Connect to Kafka using SASL authentication (username and password are required).
- Open multiple connection tabs for different clusters at the same time.
- Switch between connections instantly without reloading.
- The client id defaults to your username when not explicitly set.
- Refresh cluster metadata and list all visible topics from the connected broker.
- Use friendly Kafka values such as `SaslPlaintext`, `SaslSsl`, `Plain`, `ScramSha256`, and `ScramSha512`.
- New connections default to `SaslPlaintext` plus `ScramSha512`. Use `Plain` only when your broker is configured for SASL/PLAIN.

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

Windows downloads include:

- `Steak.exe`
- `Steak-win-x64-portable.zip`

The recommended download is `Steak-win-x64-portable.zip`. It contains the full published layout. `Steak.exe` is also available separately.

Use the prebuilt release:

1. Download the latest release assets.
2. Extract `Steak-win-x64-portable.zip` to a folder of your choice.
3. Run `Steak.exe`.
4. Steak opens your default browser automatically at `http://127.0.0.1:4040`.

Notes:

- The Windows build is self-contained. No .NET runtime installation is required.
- Steak auto-opens your default browser when it starts outside Docker.
- Steak stores mutable data in the first writable location it can use:
  `%LOCALAPPDATA%\\Steak`, then `.steak` next to the executable, then `.steak` under the content root, then the system temp folder.

Create the `.exe` yourself from source:

```powershell
dotnet restore
dotnet publish .\src\Steak.Host\Steak.Host.csproj -c Release -p:PublishProfile=Steak-win-x64
```

Output:

```text
artifacts\publish\win-x64\Steak.exe
artifacts\publish\win-x64\*
```

Package a portable zip from the published output:

```powershell
$releaseDir = Join-Path $PWD 'artifacts\release'
$zipPath = Join-Path $releaseDir 'Steak-win-x64-portable.zip'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path .\artifacts\publish\win-x64\* -DestinationPath $zipPath
```

Smoke-test the published executable:

```powershell
.\scripts\verify-standalone.ps1
```

Using the published `.exe`:

1. Keep `Steak.exe` in the published folder, or extract the portable zip without deleting any files next to it.
2. Double-click `Steak.exe`, or launch it from PowerShell:

```powershell
.\artifacts\publish\win-x64\Steak.exe
```

3. Wait for the browser to open on `http://127.0.0.1:4040`.
4. Create a connection. The form defaults to `SaslPlaintext` and `ScramSha512`.
5. If you are using the bundled `docker-compose.yml` Kafka stack, change `SASL Mechanism` to `Plain` before connecting because that sample broker is configured for SASL/PLAIN.

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
- Browser auto-launch stays disabled in Docker mode.
- If Kafka is also running in Docker, put Steak and Kafka on the same network and use the Kafka container hostname.

### 3. Source mode (`dotnet run`)

```powershell
dotnet restore
dotnet run --project .\src\Steak.Host\Steak.Host.csproj
```

Steak opens your default browser automatically at `http://127.0.0.1:4040`.

## Local Kafka Quick Start

The repository includes a `docker-compose.yml` that starts a SASL-authenticated Kafka broker with 10 pre-created topics.

### Start Kafka with docker-compose

```powershell
docker compose up -d
```

This starts Kafka on `localhost:9092` with SASL/PLAIN authentication and creates these topics automatically:

| Topic                   | Partitions |
| ----------------------- | ---------- |
| `orders`                | 3          |
| `payments`              | 2          |
| `users`                 | 2          |
| `notifications`         | 1          |
| `audit-log`             | 3          |
| `inventory.updates`     | 2          |
| `shipping.events`       | 2          |
| `analytics.clickstream` | 4          |
| `platform.health`       | 1          |
| `customer.feedback`     | 2          |

### Connect from Steak

Use these settings:

| Field             | Value                           |
| ----------------- | ------------------------------- |
| Bootstrap Servers | `localhost:9092`                |
| Username          | `admin`                         |
| Password          | `admin`                         |
| Security Protocol | `SaslPlaintext` (auto-detected) |
| SASL Mechanism    | `Plain`                         |

Steak defaults new connections to `SaslPlaintext` and `ScramSha512`.
For this bundled Docker Compose broker, explicitly switch the mechanism to `Plain`.

### Stop Kafka

```powershell
docker compose down
```

## Using The UI

### Connect

1. Click `+ New Connection`.
2. Enter bootstrap servers (e.g. `localhost:9092`).
3. Enter username and password (required for all connections).
4. Security protocol defaults to `SaslPlaintext` and mechanism to `ScramSha512`.
5. If you are connecting to the bundled `docker-compose.yml` broker, change the mechanism to `Plain`.
6. Click `Connect`.
7. Use `Refresh Topics` to reload cluster metadata.

You can open multiple connections by clicking `+ New Connection` again. Each connection appears as a tab. Click the `×` on a tab to disconnect from that cluster.

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

This folder contains the runnable `Steak.exe`. To create the same portable zip that the release workflow attaches, compress the contents into `artifacts\release\Steak-win-x64-portable.zip`.

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
| `/api/connection/all`       | GET    | List all sessions    |
| `/api/connection`           | DELETE | Disconnect all       |
| `/api/connection/{id}`      | DELETE | Disconnect one       |
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

## Release Notes

- Version `1.0.1` is the current stable release line.
- Pushing the Git tag `1.0.1` triggers the Windows release workflow and publishes the GitHub release assets.
- The same `1.0.1` tag also drives Docker image publication and updates the Docker Hub repository description directly from this `README.md`, so Docker Hub stays aligned with the repository docs.

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
