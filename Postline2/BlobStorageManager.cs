using Azure.Storage.Blobs;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Postline2
{
    public class BlobStorageManager
    {
        //API
        private static readonly HttpClient client = new HttpClient();
        private static readonly Uri uri = new Uri(ConfigurationManager.AppSettings["APIBaseAddress"]);
        //Blob
        private readonly string connectionString = ConfigurationManager.AppSettings["ConnectionStringBlobStorage"];
        private readonly string containerName = ConfigurationManager.AppSettings["ContainerName"];
        //Telemetry
        private TelemetryClient TelemetryClient;

        public BlobStorageManager(TelemetryClient telemetryClient)
        {
            TelemetryClient = telemetryClient;
        }
        public void Run(FileInfo file)
        {
            RunAsync(file).GetAwaiter().GetResult();
        }

        private async Task RunAsync(FileInfo file)
        {
            using (TelemetryClient.StartOperation<RequestTelemetry>("BlobStorageManager Upload"))
            {
                BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);

                BlobContainerClient containerClient = await CreateContainerIfDoesntExists(blobServiceClient);

                BlobClient blobClient = containerClient.GetBlobClient(file.Name.Split('$').Last());
                APIManager.GetInstance(TelemetryClient).Run(new Acknowledgment() { AzureLink = blobClient.Uri });
                Console.WriteLine(blobClient.Uri);//**
                Acknowledgment ack = new Acknowledgment() { AzureLink = blobClient.Uri };
                using (FileStream uploadStream = File.OpenRead(file.FullName))
                {
                    await blobClient.UploadAsync(uploadStream, true);
                    
                    //await SendACKAsync();
                }
                APIManager.GetInstance(TelemetryClient).Run(ack);
            }
        }
        private async Task<BlobContainerClient> CreateContainerIfDoesntExists(BlobServiceClient blobServiceClient)
        {
            BlobContainerClient containerClient;

            foreach (var item in blobServiceClient.GetBlobContainers())
            {
                if (item.Name == containerName)
                {
                    return new BlobContainerClient(connectionString, containerName);
                }
            }
            try
            {
                containerClient = await blobServiceClient.CreateBlobContainerAsync(containerName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                containerClient = new BlobContainerClient(connectionString, containerName);
            }
            return containerClient;

        }
        private async Task<Uri> SendACKAsync(Acknowledgment ACK)
        {
            client.BaseAddress = uri;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "dummiesACK", ACK);
            response.EnsureSuccessStatusCode();

            TelemetryClient.TrackEvent(response.Headers.Location.ToString());
            Console.WriteLine(response.Headers.Location.ToString());
            return uri;
        }
    }
}
