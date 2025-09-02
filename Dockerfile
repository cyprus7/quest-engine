# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# csproj — сначала, чтобы кэшировались restore
COPY src/QuestEngine.Domain/QuestEngine.Domain.csproj src/QuestEngine.Domain/
COPY src/QuestEngine.Application/QuestEngine.Application.csproj src/QuestEngine.Application/
COPY src/QuestEngine.Infrastructure/QuestEngine.Infrastructure.csproj src/QuestEngine.Infrastructure/
COPY src/QuestEngine.Api/QuestEngine.Api.csproj src/QuestEngine.Api/

RUN dotnet restore src/QuestEngine.Api/QuestEngine.Api.csproj

# остальной код
COPY . .

# publish
RUN dotnet publish src/QuestEngine.Api/QuestEngine.Api.csproj -c Release -o /app/publish

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# публикуем
COPY --from=build /app/publish ./
# обязательно кладём контент квеста внутрь образа
COPY src/QuestEngine.Api/content ./content

# сетап env
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "QuestEngine.Api.dll"]
