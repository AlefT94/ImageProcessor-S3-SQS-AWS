using ImageProcessor.Processor;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string region = config["AWS:Region"];
string bucketName = config["AWS:BucketName"];
string queueUrl = config["AWS:QueueUrl"];
const string LOCAL_DOWNLOAD_PATH = "C:\\temp";

var sqsClient = new Amazon.SQS.AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(region));
var s3Client = new Amazon.S3.AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));
var receiveRequest = new Amazon.SQS.Model.ReceiveMessageRequest
{
    QueueUrl = queueUrl,
    MaxNumberOfMessages = 1,
    WaitTimeSeconds = 20 // long polling - espera até 20s por mensagem antes de retornar vazio
};

Directory.CreateDirectory(LOCAL_DOWNLOAD_PATH);

while (true)
{
    var response = await sqsClient.ReceiveMessageAsync(receiveRequest);

    if (response.Messages is not null)
    {
        foreach (var message in response.Messages)
        {
            try
            {
                var imageMessage = System.Text.Json.JsonSerializer.Deserialize<ImageMessage>(message.Body);
                Console.WriteLine("Processando " + imageMessage.Key);

                var getObjectResponse = await s3Client.GetObjectAsync(bucketName, imageMessage.Key);

                string localFilePath = Path.Combine(LOCAL_DOWNLOAD_PATH, Path.GetFileName(imageMessage.Key));

                await getObjectResponse.WriteResponseStreamToFileAsync(localFilePath, false, CancellationToken.None);

                string processedFilePath;
                using (var image = await Image.LoadAsync(localFilePath))
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(200, 200),
                        Mode = ResizeMode.Max // mantém proporção, não distorce
                    }));

                    processedFilePath = Path.Combine(LOCAL_DOWNLOAD_PATH, "thumb_" + Path.GetFileName(localFilePath));
                    await image.SaveAsync(processedFilePath);
                }

                string processedKey = imageMessage.Key.Replace("uploads/", "processed/");

                await s3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = processedKey,
                    FilePath = processedFilePath
                });

                await sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle);

                File.Delete(localFilePath);
                File.Delete(processedFilePath);

                Console.WriteLine("Processamento concluído e mensagem removida da fila.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao processar a mensagem: " + ex.Message);
                continue;
            }
        }
    }
}