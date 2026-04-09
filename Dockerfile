FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY terraform-api.sln .
COPY src/TerraformApi.Domain/TerraformApi.Domain.csproj src/TerraformApi.Domain/
COPY src/TerraformApi.Application/TerraformApi.Application.csproj src/TerraformApi.Application/
COPY src/TerraformApi.Api/TerraformApi.Api.csproj src/TerraformApi.Api/

RUN dotnet restore src/TerraformApi.Api/TerraformApi.Api.csproj

COPY src/ src/

RUN dotnet publish src/TerraformApi.Api/TerraformApi.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup appuser

COPY --from=build /app/publish .

USER appuser

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "TerraformApi.Api.dll"]
