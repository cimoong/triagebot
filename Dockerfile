# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Stage 1 — build: restore + publish the Web app (and its project deps) in Release.
# Uses the full .NET SDK image; nothing from here ends up in the final image.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy ONLY the project files first and restore. This layer is cached and reused
# as long as the .csproj files don't change, so code edits don't trigger a full
# NuGet restore on every build.
COPY src/TriageBot.Core/TriageBot.Core.csproj                     src/TriageBot.Core/
COPY src/TriageBot.Infrastructure/TriageBot.Infrastructure.csproj src/TriageBot.Infrastructure/
COPY src/TriageBot.Web/TriageBot.Web.csproj                       src/TriageBot.Web/
RUN dotnet restore src/TriageBot.Web/TriageBot.Web.csproj

# Now copy the rest of the source and publish. Restore is skipped (already done).
# UseAppHost=false: no native launcher needed, we start via `dotnet TriageBot.Web.dll`.
COPY src/ src/
RUN dotnet publish src/TriageBot.Web/TriageBot.Web.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Stage 2 — runtime: slim ASP.NET image, non-root, listens on 8080.
# Only the published output is copied in — no SDK, no source, no secrets.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 12-factor: all config comes from environment variables at runtime. Bind to 8080
# on all interfaces so a container platform can route to it.
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .

# Run as the non-root user that the .NET base image already provides (UID 1654).
USER $APP_UID

ENTRYPOINT ["dotnet", "TriageBot.Web.dll"]
