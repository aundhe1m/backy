# backy

## Nuke DB and run

```shell
docker compose -f ~/backy/docker-compose.yml down && \
docker compose -f ~/backy/docker-compose.yml up -d && \
echo "BUILD" && \
dotnet build && \
echo "NUKE MIGRATION" && \
rm -rf Migrations/* && \
echo "CREATE MIGRATION" && \
dotnet ef migrations add InitialCreate && \
echo "RUN MIGRATION" && \
dotnet ef database update && \
echo "APP GOES BRRRR" && \
sudo dotnet run --no-build
```

## Run

```shell
dotnet build && \
sudo dotnet run --no-build
```

```shell
sudo umount /mnt/backy/md1 && sudo mdadm --stop /dev/md1
```
