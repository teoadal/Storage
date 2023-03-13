FROM mcr.microsoft.com/dotnet/sdk:7.0
WORKDIR /src
COPY . .
RUN dotnet restore "./src/Storage.Benchmark/Storage.Benchmark.csproj" && dotnet publish "./src/Storage.Benchmark/Storage.Benchmark.csproj" -c Release -o "./src/publish"
ENTRYPOINT ["dotnet", "./src/publish/Storage.Benchmark.dll"]