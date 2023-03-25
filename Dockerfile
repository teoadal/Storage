FROM mcr.microsoft.com/dotnet/sdk:7.0
WORKDIR /src
COPY . .
RUN dotnet restore "./src/Storage.Benchmark/Storage.Benchmark.csproj" && dotnet publish "./src/Storage.Benchmark/Storage.Benchmark.csproj" -c Release -o "./src/publish"
#ENTRYPOINT ["dotnet", "./src/publish/Storage.Benchmark.dll"]

RUN apt-get update -y && apt-get install -y wget && \
wget -O dotMemoryclt.zip https://www.nuget.org/api/v2/package/JetBrains.dotMemory.Console.linux-x64/2022.3.3 && \
apt-get install -y unzip && \
unzip dotMemoryclt.zip -d ./dotMemoryclt && \
chmod +x -R ./dotMemoryclt/*

ENTRYPOINT ./dotMemoryclt/tools/dotmemory start-net-core --temp-dir=./src/dotMemoryclt/tmp --timeout=16m --save-to-dir=./src/dotMemoryclt/workspaces --log-file=./src/dotMemoryclt/tmp/log.txt --trigger-timer=1m ./src/publish/Storage.Benchmark.dll