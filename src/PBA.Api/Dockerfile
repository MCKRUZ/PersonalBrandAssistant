FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

# git + openssh-client: BlogConnector shells out to `git push` to publish the blog
# to the matthewkruczek-ai repo (Netlify auto-deploys).
RUN apt-get update \
 && apt-get install -y --no-install-recommends git openssh-client \
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
USER $APP_UID
ENTRYPOINT ["dotnet", "PBA.Api.dll"]
