# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the csproj and restore any dependencies
COPY ["BackyBack/BackyBack.csproj", "BackyBack/"]
RUN dotnet restore "BackyBack/BackyBack.csproj"

# Copy the rest of the BackyBacklication code and build it
COPY BackyBack/. ./BackyBack
WORKDIR "/src/BackyBack"
RUN dotnet build "BackyBack.csproj" -c Release -o /BackyBack/build

# Publish the BackyBacklication (produces a compiled output)
RUN dotnet publish "BackyBack.csproj" -c Release -o /BackyBack/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /BackyBack

# Install necessary tools for drive management
RUN apt-get update && \
    apt-get install -y \
    parted \
    e2fsprogs \
    util-linux \
    udev \
    sudo && \
    rm -rf /var/lib/apt/lists/*

# Copy the compiled BackyBack from the build stage
COPY --from=build /BackyBack/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "BackyBack.dll"]
