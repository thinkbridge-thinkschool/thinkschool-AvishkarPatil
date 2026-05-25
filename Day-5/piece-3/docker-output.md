# Day 5 · Piece 2 — `docker-output`

Three things, one file: csproj container properties, `docker run` output, `curl` to the health endpoint.

---

## 1. csproj container properties

From [QuotesApi.csproj](QuotesApi.csproj#L9-L13):

```xml
<PropertyGroup>
  <ContainerRepository>quotes-api</ContainerRepository>
  <ContainerImageTag>0.1.0</ContainerImageTag>
  <ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0</ContainerBaseImage>
</PropertyGroup>
```

Image built with:

```pwsh
dotnet publish QuotesApi.csproj --os linux --arch x64 -t:PublishContainer -c Release
```

Tail of build output:

```
QuotesApi -> .../bin/Release/net10.0/linux-x64/publish/
Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0'.
Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

---

## 2. `docker run` output

```pwsh
docker run -d --name quotes-api -p 8080:8080 `
  -e KeyVault__Uri= `
  -e ConnectionStrings__Default="Data Source=/tmp/quotes.db" `
  quotes-api:0.1.0
```

Output:

```
$ docker run -d --name quotes-api -p 8080:8080 ...
90c6c2e05cb4775aac1cd79246d6943b7bad0def71be45d2dc90382456b417be

$ docker ps --filter name=quotes-api --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"
NAMES        IMAGE              STATUS         PORTS
quotes-api   quotes-api:0.1.0   Up 5 seconds   0.0.0.0:8080->8080/tcp, [::]:8080->8080/tcp
```

Startup logs (head):

```
[13:30:49 INF] Now listening on: http://[::]:8080
[13:30:49 INF] Application started. Press Ctrl+C to shut down.
[13:30:49 INF] Hosting environment: Production
[13:30:49 INF] Content root path: /app
```

---

## 3. `curl` to `/health`

```
$ curl -sS -i http://localhost:8080/health
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Mon, 25 May 2026 13:30:52 GMT
Server: Kestrel
Transfer-Encoding: chunked

{"status":"ok"}
```

Matching request-log line from inside the container:

```
[13:30:52 INF] HTTP GET /health responded 200 in 34.25 ms
  {"TraceId": "0HNLQC7BLIV01:00000001"}
```
