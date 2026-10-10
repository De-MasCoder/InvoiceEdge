using Amazon.S3;
using Amazon.S3.Transfer;

namespace InvoiceEdgeWorker.Services
{
    public class AmazonS3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly ILogger<AmazonS3StorageService> _logger;

        public AmazonS3StorageService(IAmazonS3 s3Client, IConfiguration configuration, ILogger<AmazonS3StorageService> logger)
        {
            _s3Client = s3Client;
            _logger = logger;

            // Grab the bucket name from appsettings.json
            _bucketName = configuration["AWS:BucketName"]
                ?? throw new ArgumentNullException("AWS BucketName configuration is missing.");
        }

        public async Task<string> UploadInvoiceAsync(string localFilePath, string fileName, CancellationToken cancellationToken)
        {
            try
            {
                //Partition storage keys by Year/Month to optimize S3 indexing partitions
                string s3Key = $"invoices/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid()}_{fileName}";

                _logger.LogInformation("☁️ Uploading {fileName} to AWS S3 bucket '{bucket}' with key '{key}'...", fileName, _bucketName, s3Key);

                using var fileTransferUtility = new TransferUtility(_s3Client);

                var uploadRequest = new TransferUtilityUploadRequest
                {
                    FilePath = localFilePath,
                    BucketName = _bucketName,
                    Key = s3Key,
                    CannedACL = S3CannedACL.Private // Invoices contain private PII/Financial data; always lock them down
                };

                await fileTransferUtility.UploadAsync(uploadRequest, cancellationToken);

                _logger.LogInformation("Cloud archiving complete for {fileName}.", fileName);

                // Return the unique S3 URI mapping to store in our database for future retrieval
                return $"s3://{_bucketName}/{s3Key}";
            }
            catch (AmazonS3Exception amazonS3Exception)
            {
                _logger.LogError(amazonS3Exception, "AWS S3 Error encountered during upload processing.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected structural exception occurred while archiving to S3.");
                throw;
            }
        }
    }
}
