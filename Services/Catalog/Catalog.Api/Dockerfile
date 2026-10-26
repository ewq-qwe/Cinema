# Образ Catalog.Api. Збирати з КОРЕНЯ репозиторію (потрібні Directory.Build.props і global.json):
#   docker build -f Services/Catalog/Catalog.Api/Dockerfile -t cinema-catalog-api .
#   docker run --rm -p 8081:8080 -e "ConnectionStrings__CatalogDb=Server=host.docker.internal,1433;Database=CinemaCatalogEf;User Id=sa;Password=...;TrustServerCertificate=True" cinema-catalog-api

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY Services/Catalog/Catalog.Domain/Catalog.Domain.csproj Services/Catalog/Catalog.Domain/
COPY Services/Catalog/Catalog.Dal/Catalog.Dal.csproj       Services/Catalog/Catalog.Dal/
COPY Services/Catalog/Catalog.Bll/Catalog.Bll.csproj       Services/Catalog/Catalog.Bll/
COPY Services/Catalog/Catalog.Api/Catalog.Api.csproj       Services/Catalog/Catalog.Api/
RUN dotnet restore Services/Catalog/Catalog.Api/Catalog.Api.csproj
COPY Services/Catalog/ Services/Catalog/
RUN dotnet publish Services/Catalog/Catalog.Api/Catalog.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Catalog.Api.dll"]
