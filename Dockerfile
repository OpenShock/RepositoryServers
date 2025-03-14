FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY --link DesktopRepositoryServer/*.csproj DesktopRepositoryServer/
RUN dotnet restore  DesktopRepositoryServer/DesktopRepositoryServer.csproj

COPY --link DesktopRepositoryServer/. DesktopRepositoryServer/

# test-build builds the xUnit test project
FROM build AS test-build

COPY --link DesktopRepositoryServer.Tests/*.csproj tests/
WORKDIR /src/tests
RUN dotnet restore

COPY --link DesktopRepositoryServer.Tests/ .
RUN dotnet build --no-restore


# test-entrypoint exposes tests as the default executable for the stage
FROM test-build AS test
ENTRYPOINT ["dotnet", "test", "--no-build", "--logger:trx"]

# publish builds and publishes complexapp
FROM build AS publish
WORKDIR /src/DesktopRepositoryServer
RUN dotnet publish --no-restore -o /app


# final is the final runtime stage for running the app
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app
COPY --link --from=publish /app .
ENTRYPOINT ["dotnet", "OpenShock.Desktop.RepositoryServer.dll"]