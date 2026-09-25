# Build the deployable application with the SDK. This is deliberately separate
# from the development container's watch output.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "PitakaApp.Api/PitakaApp.Api.csproj"
RUN dotnet publish "PitakaApp.Api/PitakaApp.Api.csproj" -c Release -o /app/publish

# The local API runs from the SDK stage so source changes are watched live.
# Compose bind-mounts the checkout at /workspace and mounts separate build
# output and NuGet cache volumes for the watcher and requested commands.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS development
RUN apt-get update \
    && apt-get install --no-install-recommends -y util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && dotnet tool install --tool-path /usr/local/share/dotnet-tools dotnet-ef --version 9.0.19 \
    && ln -s /usr/local/share/dotnet-tools/dotnet-ef /usr/local/bin/dotnet-ef \
    && mkdir -p /watch/nuget/packages /pdotnet/nuget \
    && chmod -R a+rwX /watch /pdotnet
ENV NUGET_PACKAGES=/watch/nuget/packages
WORKDIR /workspace
CMD ["dotnet", "watch", "--non-interactive", "--project", "PitakaApp.Api/PitakaApp.Api.csproj", "run", "--no-launch-profile", "--property:BaseIntermediateOutputPath=/watch/api/obj/", "--property:BaseOutputPath=/watch/api/bin/", "--property:DefaultItemExcludesInProjectFolder=**/obj/**"]

# The final image contains only the ASP.NET runtime and published application.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PitakaApp.Api.dll"]
