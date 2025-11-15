FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY --link DesktopRepositoryServer/*.csproj DesktopRepositoryServer/
RUN dotnet restore DesktopRepositoryServer/DesktopRepositoryServer.csproj

COPY --link DesktopRepositoryServer/. DesktopRepositoryServer/

RUN dotnet build --no-restore -c Release DesktopRepositoryServer/DesktopRepositoryServer.csproj

# test-build builds the test project
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --link --from=publish /app .
COPY appsettings.Container.json /app/appsettings.Container.json
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

RUN apk update && apk add --no-cache openssl


ENTRYPOINT ["/bin/ash", "/entrypoint.sh"]