FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

# Claude Code CLI: SidecarClient shells out to `claude --print` for AI drafting.
# Installed as root in the base layer so it is on PATH before the USER switch.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && npm install -g @anthropic-ai/claude-code \
 && apt-get clean && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY src/PBA.Domain/PBA.Domain.csproj PBA.Domain/
COPY src/PBA.Application/PBA.Application.csproj PBA.Application/
COPY src/PBA.Infrastructure/PBA.Infrastructure.csproj PBA.Infrastructure/
COPY src/PBA.Api/PBA.Api.csproj PBA.Api/
RUN dotnet restore PBA.Api/PBA.Api.csproj

COPY src/PBA.Domain/ PBA.Domain/
COPY src/PBA.Application/ PBA.Application/
COPY src/PBA.Infrastructure/ PBA.Infrastructure/
COPY src/PBA.Api/ PBA.Api/
RUN dotnet publish PBA.Api/PBA.Api.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Writable HOME so the claude CLI can create ~/.claude (cache/config) as non-root.
ENV HOME=/tmp
USER $APP_UID
ENTRYPOINT ["dotnet", "PBA.Api.dll"]
