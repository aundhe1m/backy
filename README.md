# backy

## Nuke DB and run

```shell
docker compose -f /home/aundhe1m/backy-project/backy/docker-compose.yml down && \
docker compose -f /home/aundhe1m/backy-project/backy/docker-compose.yml up -d && \
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

## Time Zone

Time zone can be defined be setting the Timezone value to a IANA time zone ID (Europe/Oslo)
