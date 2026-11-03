# 1. Etapa de compilación (Build)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar archivo de proyecto y restaurar dependencias
COPY backend/backend.csproj backend/
RUN dotnet restore backend/backend.csproj

# Copiar el código fuente
COPY backend/ backend/

# Compilar apuntando directamente a backend/backend.csproj
RUN dotnet build backend/backend.csproj -c Release -o /app/build

# 2. Etapa de publicación
FROM build AS publish
RUN dotnet publish backend/backend.csproj -c Release -o /app/publish /p:UseAppHost=false

# 3. Etapa final (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "backend.dll"]