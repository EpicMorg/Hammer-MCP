# hammer-mcp-cli in a container: for MCP catalogs (e.g. Glama) that start the server and list its tools.
# Hammer itself runs on Windows, so inside a container every tool just reports that no Hammer is attached.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY src/ ./src/
RUN dotnet publish src/src/HammerMcp.Cli/HammerMcp.Cli.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "hammer-mcp-cli.dll", "--stdio"]
