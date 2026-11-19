FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TeamManager.Web/TeamManager.Web.csproj src/TeamManager.Web/
RUN dotnet restore src/TeamManager.Web/TeamManager.Web.csproj
COPY src/TeamManager.Web/ src/TeamManager.Web/
COPY database/ database/
RUN dotnet publish src/TeamManager.Web/TeamManager.Web.csproj --no-restore -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends libxml2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /publish/ .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV LD_LIBRARY_PATH=/app/clidriver/lib
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "TeamManager.Web.dll"]
