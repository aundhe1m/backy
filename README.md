# backy

## Nuke DB and run

```shell
docker compose -f ~/backy/docker-compose.yml down && \
docker compose -f ~/backy/docker-compose.yml up -d && \
dotnet build && \
rm -rf Migrations/* && \
dotnet ef migrations add InitialCreate && \
dotnet ef database update && \
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
