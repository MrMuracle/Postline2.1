using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Configuration;
using System.IO;

namespace Postline2
{
    class Program
    {
        private static readonly string _instrumentationKey = ConfigurationManager.AppSettings["InstrumentationKey"];
        private static bool SendToAzure = Convert.ToBoolean(ConfigurationManager.AppSettings["SendToAzure"]);
        private static bool SendToZasilkovna = Convert.ToBoolean(ConfigurationManager.AppSettings["SendToZasilkovna"]);

        static void Main(string[] args)
        {
            //-----------TESTING---------------

            //---------------------------------

            //Telemetry
            #region Telemetry Client Init
            IServiceCollection services = new ServiceCollection();
            services.AddApplicationInsightsTelemetryWorkerService(_instrumentationKey);

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();
            #endregion

            using (telemetryClient.StartOperation<RequestTelemetry>("Postline2-Program"))
            {
                //FileManager Init
                FileManager fileManager = new FileManager(telemetryClient);
                while (true)
                {
                    if (!fileManager.AnyFilesLeft())
                    {
                        telemetryClient.TrackEvent("No files to process. Terminating");
                        return;
                    }
                    FileInfo file = fileManager.GetFile();

                    //Counter Check
                    if(!fileManager.IsFileCounterValid(file))
                    {
                        fileManager.MoveError(file);
                        continue;
                    }
                    fileManager.SetModifyTime(file);

                    if (fileManager.ReadForStatusCodeInnerXML(file) == (int)FileManager.StatusCode.Error)
                    {
                        //fileManager.ManageError(file);
                        //fileManager.MoveError(file);
                    }
                    else if (fileManager.ReadForStatusCodeInnerXML(file) == (int)FileManager.StatusCode.OK)
                    {
                        if (SendToAzure)
                        {
                            BlobStorageManager blobStorageManager = new BlobStorageManager(telemetryClient);
                            blobStorageManager.Run(file);
                        }

                        if (SendToZasilkovna)
                        {
                            throw new NotImplementedException();
                        }
                        fileManager.MoveOK(file);
                    }
                    else if (fileManager.ReadForStatusCodeInnerXML(file) == (int)FileManager.StatusCode.New)
                    {
                        fileManager.MoveNew(file);
                    }

                }
            }

        }
    }
}
