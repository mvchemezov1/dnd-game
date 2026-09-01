# Dockerfile — multi-stage build
# Стадия 1: сборка (используется полный SDK, т.к. нужен компилятор)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Сначала копируем только файл проекта — это отдельный слой Docker-кэша:
# зависимости не будут перекачиваться при каждом изменении кода.
COPY dnd_game.csproj ./
RUN dotnet restore dnd_game.csproj

# Теперь копируем весь исходный код и публикуем Release-сборку
COPY . .
RUN dotnet publish dnd_game.csproj -c Release -o /app/publish --no-restore

# Стадия 2: рантайм (лёгкий образ без SDK — только то, что нужно для запуска)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Порт задаётся в Program.cs (APP_URL, по умолчанию 0.0.0.0:5000) — держим его в EXPOSE в курсе.
EXPOSE 5000

ENTRYPOINT ["dotnet", "dnd_game.dll"]
