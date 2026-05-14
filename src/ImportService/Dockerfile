FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/ImportService/ImportService.csproj", "src/ImportService/"]
RUN dotnet restore "src/ImportService/ImportService.csproj"
COPY src/ImportService/ src/ImportService/
WORKDIR "/src/src/ImportService"
RUN dotnet publish "ImportService.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ImportService.dll", "--urls", "http://+:8080"]
