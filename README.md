# backy

```shell
docker compose -f ~/backy/docker-compose.yml down && \
docker compose -f ~/backy/docker-compose.yml up -d && \
rm -rf Migrations/* && \
dotnet ef migrations add InitialCreate && \
dotnet ef database update && \
sudo dotnet run
```
