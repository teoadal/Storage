[![.NET Core](https://github.com/teoadal/Storage/actions/workflows/dotnet.yml/badge.svg)](https://github.com/teoadal/Storage/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/v/Storages3.svg)](https://www.nuget.org/packages/Storages3)
[![NuGet](https://img.shields.io/nuget/dt/Storages3.svg)](https://www.nuget.org/packages/Storages3)
[![codecov](https://codecov.io/gh/teoadal/Storage/branch/master/graph/badge.svg?token=8L4HN9FAIV)](https://codecov.io/gh/teoadal/Storage)
[![CodeFactor](https://www.codefactor.io/repository/github/teoadal/storage/badge)](https://www.codefactor.io/repository/github/teoadal/storage)

# Клиент для S3

Привет! Это обертка над HttpClient для работы с S3 хранилищами. Мотивация создания была простейшей - я не понимал,
почему клиенты [AWS](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/welcome.html)
и [Minio](https://github.com/minio/minio-dotnet) едят так много памяти . Результат моих экспериментов: скорость
почти как у Minio, а памяти потребляю почти в 200 раз меньше, чем клиент для AWS. На Windows. На Alpine и Debian (если запустить бенчмарк в контейнере) результаты сильно скромнее - в 7 раз меньше памяти, чем клиент на AWS. Интересно, кстати, почему.

```ini
BenchmarkDotNet v0.13.12, Windows 11 (10.0.23606.1000)
13th Gen Intel Core i9-13905H, 1 CPU, 20 logical and 14 physical cores
.NET SDK 8.0.100
[Host]   : .NET 8.0.0 (8.0.23.53103), X64 RyuJIT AVX2
.NET 8.0 : .NET 8.0.0 (8.0.23.53103), X64 RyuJIT AVX2

Job=.NET 8.0  Runtime=.NET 8.0
```

| Method  |    Mean |    Error |   StdDev | Ratio | RatioSD |       Gen0 |      Gen1 |    Allocated | Alloc Ratio |
|---------|--------:|---------:|---------:|------:|--------:|-----------:|----------:|-------------:|------------:|
| Aws     | 2.458 s | 0.0491 s | 0.1002 s |  1.60 |    0.17 | 16000.0000 | 5000.0000 |  202247.9 KB |      308.06 |
| Minio   | 1.729 s | 0.0329 s | 0.0737 s |  1.12 |    0.11 |          - |         - | 279651.64 KB |      425.96 |
| Storage | 1.569 s | 0.0465 s | 0.1370 s |  1.00 |    0.00 |          - |         - |    656.52 KB |        1.00 |
```ini

## Создание клиента

Для работы с хранилищем необходимо создать клиент.

```csharp
var storageClient = new S3Client(new S3Settings
{
    AccessKey = "ROOTUSER",
    Bucket = "mybucket",
    EndPoint = "localhost",     // для Yandex.Objects это "storage.yandexcloud.net"
    Port = 9000,                // стандартный порт Minio - 9000, для Yandex.Objects указывать не нужно
    SecretKey = "ChangeMe123",
    UseHttps = false,           // для Yandex.Objects укажите true
    UseHttp2 = false            // Yandex.Objects позволяет работать по HTTP2, можете указать true
})
```

Minio предоставляет [playground](https://play.min.io:9443) для тестирования (порт для запросов всё тот же - 9000). Ключи
можно найти [в документации](https://min.io/docs/minio/linux/developers/python/minio-py.html#file-uploader-py). Доступ к
Amazon S3 не тестировался.

## Операции с S3 bucket

### Создание bucket'a

Мы передаём название bucket'a в настройках, поэтому дополнительно его вводить не надо.

```csharp
bool bucketCreateResult = await storageClient.CreateBucket(cancellationToken);
Console.WriteLine(bucketCreateResult
    ? "Bucket создан"
    : $"Bucket не был создан");
```

### Проверка существования bucket'a

Как и в прошлый раз, мы знаем название bucket'a, так как мы передаём его в настройках клиента.

```csharp
bool bucketCheckResult = await storageClient.IsBucketExists(cancellationToken);
if (bucketCheckResult) Console.WriteLine("Bucket существует");
```

### Удаление bucket'a

```csharp
bool bucketDeleteResult = await storageClient.DeleteBucket(cancellationToken);
if (bucketDeleteResult) Console.WriteLine("Bucket удалён");
```

## Операции с S3 object

Напомню, что объект в смысле S3 это и есть файл.

### Создание файла

Создание, то есть загрузка файла в S3 хранилище, возможна двумя путями: можно разбить исходные данных на кусочки (
multipart), а можно не разбивать. Самый простой способ загрузки файла - воспользоваться следующим методом (если файл
будет больше 5 МБ, то применяется multipart):

```csharp
bool fileUploadResult = await storageClient.UploadFile(fileName, fileContentType, fileStream, cancellationToken);
if (fileUploadResult) Console.WriteLine("Файл загружен");
```


#### Управление Multipart-загрузкой

Для самостоятельного управления multipart-загрузкой, можно воспользоваться методом `UploadFile` без указания данных. Получится примеоно такой код:

```csharp

using S3Upload upload = await storageClient.UploadFile(fileName, fileType, cancellationToken);

await upload.Upload(stream, cancellationToken); // загружаем часть документа
if (!await upload.Upload(byteArray, cancellationToken)) { // загружаем другую часть документа
    await upload.Abort(cancellationToken); // отменяем загрузку
}
else {
    await upload.Complete(cancellationToken); // завершаем загрузку
}

```

В коде клиента именно эту логику использует метод PutFileMultipart. Конкретную реализацию можно подсмотреть в нём.

### Получение файла

```csharp
StorageFile fileGetResult = await storageClient.GetFile(fileName, cancellationToken);
if (fileGetResult) {
    Console.WriteLine($"Размер файла {fileGetResult.Length}, контент {fileGetResult.ContetType}");
    return await fileGetResult.GetStream(cancellationToken);
}
else {
    Console.WriteLine($"Файл не может быть загружен, так как {fileGetResult}");
}
```

### Проверка существования файла

```csharp
bool fileExistsResult = await storageClient.IsFileExists(fileName, cancellationToken);
if (fileExistsResult) Console.WriteLine("Файл существует");
```

### Создание подписанной ссылки на файл

Метод проверяет наличие файла в хранилище S3 и формирует GET запрос файла. Параметр `expiration` должен содержать время
валидности ссылки начиная с даты формирования ссылки.

```csharp
string? preSignedFileUrl = storageClient.GetFileUrl(fileName, expiration);
if (preSignedFileUrl != null) Console.WriteLine($"URL получен: {preSignedFileUrl}");
```

Существует не безопасный способ создать ссылку, без проверки наличия файла в S3.

```csharp
string preSignedFileUrl = await storageClient.BuildFileUrl(fileName, expiration, cancellationToken);
```

### Удаление

Удаление объекта из S3 происходит почти мгновенно. На самом деле в S3 хранилище просто ставится задача на удаление и
клиенту возвращается результат. Кстати, если удалить файл, который не существует, то ответ будет такой же, как если бы
файл существовал. Поэтому этот метод ничего не возвращает.

```csharp
await storageClient.DeleteFile(fileName, cancellationToken);
Console.WriteLine("Файл удалён, если он, конечно, существовал");
```

## Измерение производительности и тестирование

Локальное измерение производительности и тестирование осуществляется с помощью Minio в Docker'e по http. Понимаю, что
это не самый хороший способ, но зато он самый доступный и простой.

1. Файл `docker-compose` для локального тестирования можно найти в репозитории.
2. Запускаем `docker-compose up -d`. Если всё хорошо, то бенчмарк заработает в Docker'e.
3. Если нужно запустить бенчмарк локально, то обращаем внимание на файл `appsettings.json`. В нём содержатся основные
   настройки для подключения к Minio.
4. Свойство `BigFilePath` файла `appsettings.json` сейчас не заполнено. Его можно использвоать для загрузки реального
   файла (больше 100МБ). Если свойство не заполнено, то тест сгенерирует случайную последовательность байт размером
   123МБ в памяти.
