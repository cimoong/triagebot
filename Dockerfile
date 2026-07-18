# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Stage 1 — build: restore + publish the Web app (and its project deps) in Release.
# Uses the full .NET SDK image; nothing from here ends up in the final image.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the full source tree before restoring. Restoring against only the .csproj files (with the
# rest of the source copied in afterward) reliably fails to stage Blazor's framework-provided static
# web assets (wwwroot/_framework/blazor.web.js ends up missing from the publish output, 404 at
# runtime) even though the build itself succeeds — some part of static-web-assets resolution depends
# on the full project layout being present at restore time. Copying everything up front costs the
# restore-layer cache (a source-only edit now invalidates restore too), but that's a fair trade for a
# correct build; this is a small project where restore is a few seconds anyway.
COPY src/ src/
RUN dotnet restore src/TriageBot.Web/TriageBot.Web.csproj
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
