# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ProTraining.sln ./
COPY src/Common/Common.csproj src/Common/
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Application/Application.csproj src/Application/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
COPY src/WebAPI/WebAPI.csproj src/WebAPI/
COPY tests/UnitTests/UnitTests.csproj tests/UnitTests/

RUN dotnet restore ProTraining.sln

COPY . .
RUN dotnet publish src/WebAPI/WebAPI.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000

USER $APP_UID
ENTRYPOINT ["dotnet", "WebAPI.dll"]
