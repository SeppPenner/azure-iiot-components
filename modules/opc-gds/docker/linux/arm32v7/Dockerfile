FROM microsoft/dotnet:2.2-runtime-bionic-arm32v7 AS base
WORKDIR /app

FROM microsoft/dotnet:2.2-sdk-bionic-arm32v7 AS build
WORKDIR /src
COPY src/Microsoft.Azure.IIoT.Modules.OpcUa.Gds.csproj src/
COPY NuGet.Config NuGet.Config
COPY *.props /
RUN dotnet restore --configfile NuGet.Config -nowarn:msb3202,nu1503 src/Microsoft.Azure.IIoT.Modules.OpcUa.Gds.csproj
COPY . .
WORKDIR /src/src
RUN dotnet build Microsoft.Azure.IIoT.Modules.OpcUa.Gds.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish Microsoft.Azure.IIoT.Modules.OpcUa.Gds.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
EXPOSE 58850-58852
ENTRYPOINT ["dotnet", "Microsoft.Azure.IIoT.Modules.OpcUa.Gds.dll"]
