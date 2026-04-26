FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/FreightFlow.Gateway/FreightFlow.Gateway.csproj", "src/FreightFlow.Gateway/"]
RUN dotnet restore "src/FreightFlow.Gateway/FreightFlow.Gateway.csproj"

COPY . .
RUN dotnet publish "src/FreightFlow.Gateway/FreightFlow.Gateway.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "FreightFlow.Gateway.dll"]
