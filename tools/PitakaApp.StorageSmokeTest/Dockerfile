FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY . .
RUN dotnet restore tools/PitakaApp.StorageSmokeTest/PitakaApp.StorageSmokeTest.csproj
ENTRYPOINT ["dotnet", "run", "--project", "tools/PitakaApp.StorageSmokeTest/PitakaApp.StorageSmokeTest.csproj", "--no-restore", "--"]
