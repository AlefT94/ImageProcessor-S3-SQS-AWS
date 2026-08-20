using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string region = config["AWS:Region"];
string bucketName = config["AWS:BucketName"];
string queueUrl = config["AWS:QueueUrl"];

var s3Client = new Amazon.S3.AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));
var sqsClient = new Amazon.SQS.AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(region));

while (true)
{
    Console.WriteLine("Digite o caminho da imagem (ou 'sair' para encerrar):");
    string? filePath = Console.ReadLine();

    if (filePath?.ToLower() == "sair")
        break;

    var fileExists = File.Exists(filePath);

    if (fileExists)
    {
        Console.WriteLine("Arquivo encontrado. Iniciando upload...");
        string key = $"uploads/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid()}_{Path.GetFileName(filePath)}";

        await s3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            FilePath = filePath
        });

        var message = new
        {
            Key = key,
            OriginalFileName = Path.GetFileName(filePath),
            UploadedAt = DateTime.UtcNow
        };

        await sqsClient.SendMessageAsync(new Amazon.SQS.Model.SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = System.Text.Json.JsonSerializer.Serialize(message)
        });

        Console.WriteLine("Upload concluído!");
    }
    else
    {
        Console.WriteLine("Arquivo não encontrado.");
    }
}