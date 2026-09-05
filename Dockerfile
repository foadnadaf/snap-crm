# SnapCRM — isolated marketing/CRM service. Runs as its own container on Hetzner.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SnapCrm.Api/SnapCrm.Api.csproj src/SnapCrm.Api/
RUN dotnet restore src/SnapCrm.Api/SnapCrm.Api.csproj
COPY . .
RUN dotnet publish src/SnapCrm.Api/SnapCrm.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8090
ENV TZ=Europe/Vienna
COPY --from=build /app/publish .
EXPOSE 8090
ENTRYPOINT ["dotnet", "SnapCrm.Api.dll"]
