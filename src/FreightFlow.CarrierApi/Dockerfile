FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/FreightFlow.CarrierApi/FreightFlow.CarrierApi.csproj",     "src/FreightFlow.CarrierApi/"]
COPY ["src/FreightFlow.SharedKernel/FreightFlow.SharedKernel.csproj", "src/FreightFlow.SharedKernel/"]
RUN dotnet restore "src/FreightFlow.CarrierApi/FreightFlow.CarrierApi.csproj"

COPY . .
RUN dotnet publish "src/FreightFlow.CarrierApi/FreightFlow.CarrierApi.csproj" \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "FreightFlow.CarrierApi.dll"]
