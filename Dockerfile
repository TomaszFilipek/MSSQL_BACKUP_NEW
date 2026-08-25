FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/MssqlBackup.Shared/MssqlBackup.Shared.csproj src/MssqlBackup.Shared/
COPY src/MssqlBackup.Api/MssqlBackup.Api.csproj src/MssqlBackup.Api/
RUN dotnet restore src/MssqlBackup.Api/MssqlBackup.Api.csproj

COPY src/ src/
RUN dotnet publish src/MssqlBackup.Api/MssqlBackup.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV TZ=Europe/Warsaw

EXPOSE 5000

ENTRYPOINT ["dotnet", "MssqlBackup.Api.dll"]
