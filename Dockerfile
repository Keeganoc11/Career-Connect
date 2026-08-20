# Builds the React client, then the API, then combines them into one runtime
# image: the API serves the client's static files (see Program.cs) so there's
# a single deployed service with no cross-origin calls in production.

FROM node:22-alpine AS client-build
WORKDIR /client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src
COPY api/CareerConnect.Api/CareerConnect.Api.csproj api/CareerConnect.Api/
RUN dotnet restore api/CareerConnect.Api/CareerConnect.Api.csproj
COPY api/CareerConnect.Api/ api/CareerConnect.Api/
RUN dotnet publish api/CareerConnect.Api/CareerConnect.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=client-build /client/dist ./wwwroot

ENV ASPNETCORE_ENVIRONMENT=Production
# Railway assigns the listen port at runtime via $PORT; default to 8080 for
# any other host that doesn't set it.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet CareerConnect.Api.dll"]
