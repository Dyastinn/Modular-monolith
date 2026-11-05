# syntax=docker/dockerfile:1
# Two images from one build: `app` (the ERP) and `migrate` (one-shot schema update, run before each app start).
# Both start from the same ASP.NET runtime image, so a server stores those layers once.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first, from the project files only, so a code change doesn't re-download every package.
COPY Directory.Build.props Directory.Packages.props Erp.slnx ./
COPY .config/ .config/
COPY src/Shared/Erp.Shared.BuildingBlocks/*.csproj src/Shared/Erp.Shared.BuildingBlocks/
COPY src/Shared/Erp.Shared.Infrastructure/*.csproj src/Shared/Erp.Shared.Infrastructure/
COPY src/Modules/Identity/Erp.Modules.Identity/*.csproj src/Modules/Identity/Erp.Modules.Identity/
COPY src/Modules/Sales/Erp.Modules.Sales/*.csproj src/Modules/Sales/Erp.Modules.Sales/
COPY src/Modules/Sales/Erp.Modules.Sales.Contracts/*.csproj src/Modules/Sales/Erp.Modules.Sales.Contracts/
COPY src/Modules/Inventory/Erp.Modules.Inventory/*.csproj src/Modules/Inventory/Erp.Modules.Inventory/
COPY src/Modules/Inventory/Erp.Modules.Inventory.Contracts/*.csproj src/Modules/Inventory/Erp.Modules.Inventory.Contracts/
COPY src/Modules/Purchasing/Erp.Modules.Purchasing/*.csproj src/Modules/Purchasing/Erp.Modules.Purchasing/
COPY src/Modules/Invoicing/Erp.Modules.Invoicing/*.csproj src/Modules/Invoicing/Erp.Modules.Invoicing/
COPY src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts/*.csproj src/Modules/Invoicing/Erp.Modules.Invoicing.Contracts/
COPY src/Erp.Web/*.csproj src/Erp.Web/
COPY src/Erp.Api/*.csproj src/Erp.Api/
RUN dotnet restore src/Erp.Api && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/Erp.Api -c Release -o /out/app --no-restore

# One migration bundle per module (each has its own DbContext and history), built for this image's CPU.
RUN rid=linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) \
 && for m in Identity Sales Inventory Purchasing Invoicing; do \
      dotnet ef migrations bundle --configuration Release -r "$rid" \
        --project "src/Modules/$m/Erp.Modules.$m" --startup-project "src/Modules/$m/Erp.Modules.$m" \
        --output "/out/migrations/migrate-$m" --force || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrate
WORKDIR /migrations
COPY --from=build /out/migrations/ ./
COPY deploy/docker/migrate.sh ./
USER $APP_UID
ENTRYPOINT ["/bin/sh", "/migrations/migrate.sh"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS app
# The invoice PDF embeds a font; DejaVu Sans comes with the operating system image, not with this source.
RUN apt-get update \
 && apt-get install -y --no-install-recommends fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/app/ ./
# Never root: the image's built-in unprivileged user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Erp.Api.dll"]
