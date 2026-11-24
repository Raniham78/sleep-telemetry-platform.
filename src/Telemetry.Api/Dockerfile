FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Telemetry.Api.csproj .
RUN dotnet restore Telemetry.Api.csproj
COPY . .
RUN dotnet publish Telemetry.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Telemetry.Api.dll"]
