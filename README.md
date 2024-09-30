# backy

```shell
docker compose -f ~/backy-project/backy/docker-compose.yml down && \
docker compose -f ~/backy-project/backy/docker-compose.yml up -d && \
rm -rf Migrations/* && \
dotnet ef migrations add InitialCreate && \
dotnet ef database update && \
dotnet run
```
So I have this .NET 8 program called backy, and I would like to improve a the Drive page.
Currently I will 

force create mdadm --create /dev/md1 --level=1 --raid-devices=1 /dev/sdd --run --force


scsi-3600224801a52e7f00809be4a8acfcb25

scsi-3600224801a52e7f00809be4a8acfcb25

scsi-360022480c333527121c6607f9d583249