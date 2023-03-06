# Storage для S3

Привет! Это простейший клиент для работы с S3 хранилищами. Протестировано **только на Minio, без https**. Мотивация создания была простейшей - я не понимал, почему клиенты от AWS и Minio едят так много памяти.

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
bool bucketCreateResult = await storageClient.CreateBucket(CancellationToken.None);
if (bucketCreateResult) Console.WriteLine("Bucket создан")
```

### Проверка существования bucket'a 

Как и в прошлый раз, мы знаем название bucket'a, так как мы передаём его в настройках клиента.

```csharp
bool bucketCheckResult = await storageClient.BucketExists(CancellationToken.None);
if (bucketCheckResult) Console.WriteLine("Bucket существует")
```

## Операции с S3 object

Напомню, что объект в смысле S3 это и есть файл.

### Создание object'a

Создание, то есть загрузка файла в S3 хранилище, возможна двумя путями: с разбиением исходных данных на кусочки (multipart) и без этого. Самый простой способ загрузки файла, это воспользоваться сделующим методом (если файл будет больше 5 МБ, то применяется multipart): 

```csharp
bool fileUploadResult = await storageClient.UploadFile(fileName, fileStream, fileContentType, CancellationToken.None);
if (fileUploadResult) Console.WriteLine("Файл загружен")
```

#### Создание S3 объекта без Multipart

Можно принудительно загружать файл без multipart. Есть сигнатура и для ``byte[]``. 

```csharp
bool fileUploadResult = await storageClient.PutFile(fileName, byteArray, fileContentType, CancellationToken.None);
if (fileUploadResult) Console.WriteLine("Файл загружен")
```

#### Создание S3 объекта с использованием Multipart

Можно принудительно загружать файл с использованием multipart. В этом случае нужно будет явно указать размер одного кусочка (не менее 5 МБ).

```csharp
bool fileUploadResult = await storageClient.PutFileMultipart(fileName, fileStream, fileContentType, partSize, CancellationToken.None);
if (fileUploadResult) Console.WriteLine("Файл загружен")
```

### Проверка существования object'a

```csharp
bool fileExistsResult = await storageClient.FileExists(fileName, CancellationToken.None);
if (fileExistsResult) Console.WriteLine("Файл существует")
```

### Удаление object'a

Удаление объекта из S3 происходит почти мгновенно. Такое ощущение, что просто ставится задача на удаление и клиенту возвращается результат.

```csharp
bool fileDeleteResult = await storageClient.DeleteFile(fileName, CancellationToken.None);
if (fileDeleteResult) Console.WriteLine("Файл удалён")
```