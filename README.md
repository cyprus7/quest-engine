# Quest Engine (C# / .NET 8)

Готовый скелет квестового движка с REST API, EF Core (InMemory/PG), сундуками с весами и тестами.

## Быстрый старт
```bash
# Требуется .NET 8 SDK
cd src/QuestEngine.Api
dotnet run
# Открой Swagger: http://localhost:5000/swagger
# Пример открытия сундука:
curl -X POST http://localhost:5000/v1/quests/mostbet_odyssey_v1/chests/open \
  -H "X-User-Id: demo-user" \
  -H "X-Auth-Token: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{"ChestId":"chest.day2.tower"}'
```
