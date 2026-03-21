# Steak

Steak is a self-hosted Kafka operator workspace for day-to-day message inspection and replay.

It gives you one browser UI for:

- connecting to Kafka and discovering visible topics
- viewing live messages with decoded previews
- consuming/exporting messages to the file system or S3
- publishing single messages or batch republishing exported envelopes

## Quick start

Pull the latest image:

```powershell
docker pull thesmoketeam/steak:latest
```

Run the container:

```powershell
docker run --rm `
  -p 8080:8080 `
  -v ${PWD}/data:/data `
  thesmoketeam/steak:latest
```

Open:

- App: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`

## Local Kafka with Docker

If Kafka is also running in Docker, put Steak and Kafka on the same network and use the Kafka container hostname from the Steak UI.

Example bootstrap server:

```text
steak-kafka:29092
```

Example run command:

```powershell
docker run --rm `
  --network steak-net `
  -p 8080:8080 `
  -v ${PWD}/data:/data `
  thesmoketeam/steak:latest
```

## Notes

- Browser auto-launch is disabled.
- Container exports are written under `/data` by default.
- The Windows self-contained `.exe` release and full usage guide live in the GitHub repository.
