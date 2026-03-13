FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj src/PersonalBrandAssistant.Domain/
COPY src/PersonalBrandAssistant.Application/PersonalBrandAssistant.Application.csproj src/PersonalBrandAssistant.Application/
COPY src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj src/PersonalBrandAssistant.Infrastructure/
COPY src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj src/PersonalBrandAssistant.Api/

RUN dotnet restore src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj

COPY src/ src/
RUN dotnet publish src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS dirs
RUN mkdir -p /data-protection-keys && chown 1654:1654 /data-protection-keys

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=dirs /data-protection-keys /data-protection-keys

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PersonalBrandAssistant.Api.dll"]
