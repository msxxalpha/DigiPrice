FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/DigiPrice.Core/DigiPrice.Core.csproj src/DigiPrice.Core/
COPY src/DigiPrice.Api/DigiPrice.Api.csproj src/DigiPrice.Api/
RUN dotnet restore src/DigiPrice.Api/DigiPrice.Api.csproj
COPY . .
RUN dotnet publish src/DigiPrice.Api/DigiPrice.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DigiPrice.Api.dll"]
