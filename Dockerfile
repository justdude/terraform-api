FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer caching on restore
COPY terraform-api.slnx .
COPY src/TerraformApi.Domain/TerraformApi.Domain.csproj src/TerraformApi.Domain/
COPY src/TerraformApi.Application/TerraformApi.Application.csproj src/TerraformApi.Application/
COPY src/TerraformApi.Api/TerraformApi.Api.csproj src/TerraformApi.Api/

RUN dotnet restore src/TerraformApi.Api/TerraformApi.Api.csproj

# Copy source (tests excluded via .dockerignore)
COPY src/ src/

RUN dotnet publish src/TerraformApi.Api/TerraformApi.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL maintainer="terraform-api"
LABEL description="OpenAPI to Azure APIM Terraform Converter"

WORKDIR /app

# Non-root user for security
RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup appuser

COPY --from=build /app/publish .

USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check using wget (available in aspnet base image, unlike curl)
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "TerraformApi.Api.dll"]
