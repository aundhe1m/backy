# Backy - Cold Storage Backup Solution

Backy is a specialized backup application designed for managing cold storage drive pools. It provides a robust solution for organizations looking to maintain offline or semi-offline backups of their data, with built-in support for remote storage indexing and file tracking.

## Purpose

The main purpose of Backy is to provide a dedicated host solution for cold storage backups, separate from your primary storage infrastructure. It accomplishes this through:

- Creating and managing RAID-based drive pools for cold storage
- Remote storage connectivity via SSH/SFTP for file indexing
- File history tracking in PostgreSQL for disaster recovery mapping
- Easy-to-use web interface for managing drives and remote connections

## Key Features

- **Drive Pool Management**
  - Create and manage RAID1 drive pools
  - Drive protection to prevent accidental use
  - Detailed drive status monitoring
  - Pool mounting/unmounting capabilities

- **Remote Storage Integration**
  - SSH/SFTP connectivity
  - Automated file indexing
  - Configurable scan schedules
  - Filter rules for selective indexing

- **File Tracking**
  - PostgreSQL-based file history
  - Disaster recovery mapping
  - File change detection
  - Historical file location tracking

## Project Status

Current development status:
- ✅ Drive pool management (Complete)
- 🟨 Remote storage connection (In Progress)
- 🟥 Cold storage transfer system (Not Started)

## Prerequisites

- .NET 8.0
- PostgreSQL
- Docker and Docker Compose
- Linux-based OS with mdadm support
- Root access for drive operations

## Quick Start

1. Clone the repository:
   ```shell
   git clone [repository-url]
   cd backy
   ```

2. Start the PostgreSQL database:
   ```shell
   docker compose up -d
   ```

3. Build and run the application:
   ```shell
   dotnet build
   sudo dotnet run --no-build
   ```

## Development Commands

### Create migration

```shell
dotnet ef migrations add "$(date +%Y%m%d_%H%M%S)" --project ./Backy
```

### Full Reset (Database and Migrations)
```shell
docker compose -f ../docker-compose.yml down && \
docker compose -f ../docker-compose.yml up -d && \
dotnet build && \
rm -rf Migrations/* && \
dotnet ef migrations add InitialCreate && \
dotnet ef database update && \
dotnet run --no-build
```

### Quick Run
```shell
dotnet build && sudo dotnet run --no-build
```

### Unmount Pool Example
```shell
sudo umount /mnt/backy/ble && sudo mdadm --stop /dev/md127
```

## Configuration

### Time Zone
Time zone can be configured by setting the Timezone value to an IANA time zone ID (e.g., "Europe/Oslo") in appsettings.json.

## Architecture

The application is built using:
- ASP.NET Core 8.0
- Blazor Server for the web interface
- Entity Framework Core for database operations
- PostgreSQL for data storage
- Linux system tools (mdadm, mount, etc.) for drive operations

## Project Structure

- `/Components` - Blazor components and pages
- `/Data` - Database context and configurations
- `/Models` - Data models and DTOs
- `/Services` - Business logic and system operations
- `/wwwroot` - Static files and CSS

## Contributing

Please read through the architecture documentation in the `/docs` folder before contributing. All pull requests should follow the established coding patterns and include appropriate tests.

## License

[License Information]

## Security

This application requires root access for drive operations. Always run in a controlled environment with appropriate security measures.
