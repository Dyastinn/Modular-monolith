# syntax=docker/dockerfile:1
# Two images from one build: `app` (YASDA itself) and `migrate` (one-shot schema update, run before each app start).
# Both start from the same ASP.NET runtime image, so a server stores those layers once.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, from the project files only, so a code change doesn't re-download every package.
COPY Directory.Build.props Directory.Packages.props Yasda.slnx ./
COPY .config/ .config/
COPY src/Shared/Yasda.Shared.BuildingBlocks/*.csproj src/Shared/Yasda.Shared.BuildingBlocks/
COPY src/Shared/Yasda.Shared.Infrastructure/*.csproj src/Shared/Yasda.Shared.Infrastructure/
COPY src/Modules/Identity/Yasda.Modules.Identity/*.csproj src/Modules/Identity/Yasda.Modules.Identity/
COPY src/Modules/Identity/Yasda.Modules.Identity.Contracts/*.csproj src/Modules/Identity/Yasda.Modules.Identity.Contracts/
COPY src/Modules/Tickets/Yasda.Modules.Tickets/*.csproj src/Modules/Tickets/Yasda.Modules.Tickets/
COPY src/Modules/Tickets/Yasda.Modules.Tickets.Contracts/*.csproj src/Modules/Tickets/Yasda.Modules.Tickets.Contracts/
COPY src/Modules/Sla/Yasda.Modules.Sla/*.csproj src/Modules/Sla/Yasda.Modules.Sla/
COPY src/Modules/Sla/Yasda.Modules.Sla.Contracts/*.csproj src/Modules/Sla/Yasda.Modules.Sla.Contracts/
COPY src/Yasda.Web/*.csproj src/Yasda.Web/
COPY src/Yasda.Api/*.csproj src/Yasda.Api/
RUN dotnet restore src/Yasda.Api && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/Yasda.Api -c Release -o /out/app --no-restore

# One migration bundle per module (each has its own DbContext and history), built for this image's CPU.
RUN rid=linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) \
 && for m in Identity Tickets Sla; do \
      dotnet ef migrations bundle --configuration Release -r "$rid" \
        --project "src/Modules/$m/Yasda.Modules.$m" --startup-project "src/Modules/$m/Yasda.Modules.$m" \
        --output "/out/migrations/migrate-$m" --force || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrate
WORKDIR /migrations
COPY --from=build /out/migrations/ ./
COPY deploy/compose/migrate.sh ./
USER $APP_UID
ENTRYPOINT ["/bin/sh", "/migrations/migrate.sh"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
WORKDIR /app
COPY --from=build /out/app/ ./
# Never root: the image's built-in unprivileged user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Yasda.Api.dll"]
