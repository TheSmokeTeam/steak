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

- Connect to Kafka over `Plaintext`, `Ssl`, `SaslPlaintext`, or `SaslSsl`.
- Open multiple connection tabs for different clusters at the same time.
- Switch between connections instantly without reloading.
- Refresh cluster metadata and list all visible topics from the connected broker.
- Use friendly Kafka values such as `SaslPlaintext`, `SaslSsl`, `Plain`, `ScramSha256`, and `ScramSha512`.
- New connections default to `SaslPlaintext` plus `ScramSha512`.
- Username and password are required only for SASL protocols.

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

### Windows release mode (`.exe`)

This is the mode to use if you want a Windows executable without installing the .NET runtime or SDK.

Run the prebuilt executable:

```powershell
.\artifacts\publish\win-x64\Steak.exe
```

Default behavior:

- Steak binds to `http://127.0.0.1:4040`.
- The UI, API, and Swagger all share that same port.
- Steak opens your default browser automatically unless you disable it.
- The Windows build is self-contained. No .NET runtime installation is required.

### Docker mode

Run the container:

```powershell
docker run --rm `
  -p 8080:8080 `
  -v ${PWD}/data:/data `
  <docker-username>/steak:latest
```

Default behavior:

- The container listens on port `8080`.
- With `-p 8080:8080`, the UI, API, and Swagger are available on `http://localhost:8080`.
- Browser auto-launch stays disabled in Docker mode.
- Exported files are stored under `/data` inside the container.

### Source mode (`dotnet run`)

Run Steak directly from source:

```powershell
dotnet restore
dotnet run --project .\src\Steak.Host\Steak.Host.csproj
```

Default behavior:

- Steak binds to `http://127.0.0.1:4040`.
- The UI, API, and Swagger all share that same port.
- Steak opens your default browser automatically unless you disable it.

## Runtime Flags

Steak supports these CLI flags in `.exe` mode and in `dotnet run` mode.

### `--port`

Overrides only the port while keeping the default host behavior.

- Default outside Docker: `127.0.0.1:4040`
- The UI, API, and Swagger stay on the same port

Examples:

```powershell
.\artifacts\publish\win-x64\Steak.exe --port 5050
dotnet run --project .\src\Steak.Host\Steak.Host.csproj -- --port 5050
```

Those commands bind Steak to `http://127.0.0.1:5050`.

### `--urls`

Overrides the full ASP.NET Core binding URL. Use this when you need something more specific than just changing the port.

Examples:

```powershell
.\artifacts\publish\win-x64\Steak.exe --urls http://127.0.0.1:5055
.\artifacts\publish\win-x64\Steak.exe --urls http://0.0.0.0:5055
```

If you pass both `--urls` and `--port`, `--urls` wins.

### `--log-level`

Controls the minimum console log level.

Supported values:

- `Trace`
- `Debug`
- `Information`
- `Warning`
- `Error`
- `Fatal`

Example:

```powershell
.\artifacts\publish\win-x64\Steak.exe --log-level Debug
```

`Debug` logs intentionally dump the full Kafka connection settings and effective Kafka client config, including raw credentials and PEM values. Use it only in a trusted local terminal session.

### `--open-browser`

Controls whether Steak opens the browser automatically on startup.

- Default outside Docker: `true`
- Effective default in Docker: disabled

Examples:

```powershell
.\artifacts\publish\win-x64\Steak.exe --open-browser false
dotnet run --project .\src\Steak.Host\Steak.Host.csproj -- --open-browser false
```

### Common examples

Run headless on the default local port:

```powershell
.\artifacts\publish\win-x64\Steak.exe --open-browser false
```

Run headless on a custom port with verbose logs:

```powershell
.\artifacts\publish\win-x64\Steak.exe --port 5050 --log-level Debug --open-browser false
```

Expose Steak on a specific network binding:

```powershell
.\artifacts\publish\win-x64\Steak.exe --urls http://0.0.0.0:5055 --log-level Information --open-browser false
```

## Using The UI

### Connect

1. Click `+ New Connection`.
2. Enter bootstrap servers such as `broker-1:9092,broker-2:9092`.
3. Choose the correct security protocol.
4. Enter username and password only when using `SaslPlaintext` or `SaslSsl`.
5. Adjust the SASL mechanism if your cluster requires something other than the default `ScramSha512`.
6. Add SSL PEM values only when your cluster requires them.
7. Click `Connect`.
8. Use `Refresh Topics` to reload cluster metadata.

You can open multiple connections by clicking `+ New Connection` again. Each connection appears as a tab.

### View workspace

1. Connect to Kafka.
2. Open the `View` workspace.
3. Click `Refresh Topics`.
4. Select a topic.
5. Click `Start Live View`.
6. Select a message row to inspect headers and decoded payload previews.

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

This folder contains the runnable `Steak.exe`.

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

- Project files keep `VersionPrefix` at `0.0.0`, and release identity is driven by the Git tag.
- Pushing a stable Git tag such as `1.1.1` triggers the tag CI pipeline.
- That tag pipeline runs build and test first, pushes the Docker image to Docker Hub, then creates the GitHub release only after those jobs succeed.
- Each GitHub release includes `Steak.exe` and `Steak-win-x64-portable.zip` as downloadable assets.
- Tags that include a prerelease suffix such as `-alpha.1` still create a GitHub prerelease, but skip Docker `latest`.

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
