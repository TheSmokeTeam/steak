FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Steak.sln ./
COPY src/Steak.Core/Steak.Core.csproj src/Steak.Core/
COPY src/Steak.Host/Steak.Host.csproj src/Steak.Host/
COPY tests/Steak.Tests/Steak.Tests.csproj tests/Steak.Tests/
RUN dotnet restore src/Steak.Host/Steak.Host.csproj

COPY src ./src
COPY tests ./tests
RUN dotnet publish src/Steak.Host/Steak.Host.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV Steak__Runtime__LaunchBrowser=false
ENV Steak__Storage__DataRoot=/data
EXPOSE 8080
VOLUME ["/data"]

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "Steak.dll"]
