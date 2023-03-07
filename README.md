
[![Pipeline](https://github.com/teoadal/local/workflows/.NET%20Core/badge.svg?branch=master)](https://github.com/teoadal/storage/actions)
[![NuGet](https://img.shields.io/nuget/v/Storages3.svg)](https://www.nuget.org/packages/Storages3) 
[![NuGet](https://img.shields.io/nuget/dt/Storages3.svg)](https://www.nuget.org/packages/Storages3)
[![CodeFactor](https://www.codefactor.io/repository/github/teoadal/storage/badge)](https://www.codefactor.io/repository/github/teoadal/storage)

# Клиент для S3

Привет! Это обертка над HttpClient для работы с S3 хранилищами. **Протестировано только на Minio, без https**. Мотивация создания была простейшей - я не понимал, почему клиенты [AWS](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/welcome.html) и [Minio](https://github.com/minio/minio-dotnet) едят так много памяти. Для красоты эксперимента я добавил ещё один клиент, который нашёл на github - клиент для [Yandex Objects](https://github.com/DubZero/AspNetCore.Yandex.ObjectStorage), который использовать строго не рекомендую. Результат моих экспериментов: скорость почти как у Minio, а памяти потребляю почти в 200 раз меньше, чем клиент для AWS.

```ini
BenchmarkDotNet=v0.13.5, OS=Windows 11 (10.0.22621.1265/22H2/2022Update/SunValley2)
AMD Ryzen 7 5800H with Radeon Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK=7.0.102
  [Host]   : .NET 7.0.2 (7.0.222.60605), X64 RyuJIT AVX2 DEBUG
  .NET 7.0 : .NET 7.0.2 (7.0.222.60605), X64 RyuJIT AVX2

Job=.NET 7.0  Runtime=.NET 7.0 
```

| Method  |    Mean | Ratio |       Gen0 |      Gen1 |      Gen2 |     Allocated | Alloc Ratio |
|---------|--------:|------:|-----------:|----------:|----------:|--------------:|------------:|
| Aws     | 2.160 s |  1.65 | 25000.0000 | 8000.0000 |         - |  207325.71 KB |      254.56 |
| Minio   | 1.280 s |  0.97 |          - |         - |         - |  279978.45 KB |      343.76 |
| Yandex  | 1.539 s |  1.17 |  1000.0000 | 1000.0000 | 1000.0000 | 1033076.55 KB |    1,268.43 |
| **Storage** | 1.318 s |  **1.00** |          - |         - |         - |     **814.45 KB** |        **1.00** |


## Создание клиента

Для работы с хранилищем необходимо создать клиент.

```csharp
var storageClient = new StorageClient(new StorageSettings
{
    AccessKey = "ROOTUSER",
    Bucket = "mybucket",
    EndPoint = "localhost",
    Port = 9000,
    SecretKey = "ChangeMe123",
})
```

## Операции с S3 bucket

### Создание bucket'a

Мы передаём название bucket'a в настройках, поэтому дополнительно его вводить не надо.

```csharp
bool bucketCreateResult = await storageClient.CreateBucket(cancellationToken);
if (bucketCreateResult) Console.WriteLine("Bucket создан")
```

### Проверка существования bucket'a 

Как и в прошлый раз, мы знаем название bucket'a, так как мы передаём его в настройках клиента.

```csharp
bool bucketCheckResult = await storageClient.BucketExists(cancellationToken);
if (bucketCheckResult) Console.WriteLine("Bucket существует")
```

### Удаление bucket'a

```csharp
bool bucketDeleteResult = await storageClient.DeleteBucket(cancellationToken);
if (bucketDeleteResult) Console.WriteLine("Bucket удалён")
```

## Операции с S3 object

Напомню, что объект в смысле S3 это и есть файл.

### Создание

Создание, то есть загрузка файла в S3 хранилище, возможна двумя путями: можно разбить исходные данных на кусочки (multipart), а можно не разбивать. Самый простой способ загрузки файла - воспользоваться следующим методом (если файл будет больше 5 МБ, то применяется multipart): 

```csharp
bool fileUploadResult = await storageClient.UploadFile(fileName, fileStream, fileContentType, cancellationToken);
if (fileUploadResult) Console.WriteLine("Файл загружен")
```

#### Создание без Multipart

Можно принудительно загружать файл без multipart. Есть сигнатура и для ``byte[]``. 

```csharp
bool fileUploadResult = await storageClient.PutFile(fileName, byteArray, fileContentType, cancellationToken);
if (fileUploadResult) Console.WriteLine("Файл загружен")
```

#### Создание с использованием Multipart

Можно принудительно загружать файл с использованием multipart. В этом случае нужно будет явно указать размер одного кусочка (не менее 5 МБ).

```csharp
bool fileUploadResult = await storageClient.PutFileMultipart(fileName, fileStream, fileContentType, partSize, cancellationToken);
if (fileUploadResult) Console.WriteLine("Файл загружен")
```

### Получение

```csharp
StorageFile fileGetResult = await storageClient.GetFile(fileName, cancellationToken);
if (fileGetResult.Exists) {
    Console.WriteLine($"Размер файла {fileGetResult.Length}, контент {fileGetResult.ContetType}");
    return fileGetResult.GetStream();
}
```

### Проверка существования

```csharp
bool fileExistsResult = await storageClient.FileExists(fileName, cancellationToken);
if (fileExistsResult) Console.WriteLine("Файл существует")
```

### Создание подписанной ссылки

Метод проверяет наличие файла в хранилище S3 и формирует GET запрос файла. Параметр `expiration` должен содержать время валидности ссылки начиная с даты формирования ссылки.

```csharp
string preSignedFileUrl = storageClient.BuildFileUrl(fileName, expiration);
```

Если необходимо создать ссылку без проверки наличия файла в S3:

```csharp
string? preSignedFileUrl = await storageClient.CreateFileUrl(fileName, expiration, cancellationToken);
if (preSignedFileUrl != null) Console.WriteLine("URL получен")
```

### Удаление

Удаление объекта из S3 происходит почти мгновенно. Такое ощущение, что просто ставится задача на удаление и клиенту возвращается результат.

```csharp
bool fileDeleteResult = await storageClient.DeleteFile(fileName, cancellationToken);
if (fileDeleteResult) Console.WriteLine("Файл удалён")
```

## Тесты

Как запускать тесты через ``github actions`` пока не придумал. Нужна же minio, а как вставить её я не знаю. Варианты нашёл, читаю.
