FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/ProductService/ProductService.csproj", "src/ProductService/"]
RUN dotnet restore "src/ProductService/ProductService.csproj"
COPY src/ProductService/ src/ProductService/
WORKDIR "/src/src/ProductService"
RUN dotnet publish "ProductService.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProductService.dll"]
