# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copie du fichier projet puis restore (optimise le cache Docker)
COPY Xrmbox.VoC.Portal/Xrmbox.VoC.Portal.csproj Xrmbox.VoC.Portal/
RUN dotnet restore Xrmbox.VoC.Portal/Xrmbox.VoC.Portal.csproj

# Copie du reste du code et publication
COPY . .
WORKDIR /src/Xrmbox.VoC.Portal
RUN dotnet publish Xrmbox.VoC.Portal.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Railway: écoute sur le port 8080
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Xrmbox.VoC.Portal.dll"]
```

Si vous voulez, je peux aussi générer un `.dockerignore` adapté pour accélérer le build sur Railway.