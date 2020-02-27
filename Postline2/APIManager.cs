using Microsoft.ApplicationInsights;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Postline2
{
    public class APIManager
    {
        private static TelemetryClient _telemetryClient;
        private static readonly HttpClient _client = new HttpClient();
        private static readonly Uri _uri = new Uri(ConfigurationManager.AppSettings["APIBaseAddress"]);

        #region Singleton
        private static APIManager _instance = null;
        private APIManager(TelemetryClient telemetryClient)
        {
            _telemetryClient = telemetryClient;
            _client.BaseAddress = _uri;
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }
        public static APIManager GetInstance(TelemetryClient telemetryClient)
        {
            if (_instance == null)
                APIManager._instance = new APIManager(telemetryClient);
            return APIManager._instance;
        }
        #endregion
        public void Run(Acknowledgment ACK)
        {
            CreateACKAsync(ACK).GetAwaiter().GetResult();
        }
        static async Task<Uri> CreateACKAsync(Acknowledgment ACK)
        {
            HttpResponseMessage response = await _client.PostAsJsonAsync(
                "dummiesACK", ACK);
            response.EnsureSuccessStatusCode();

            //TelemetryClient.TrackEvent(response.Headers.Location.ToString());
            //Console.WriteLine(response.Headers.Location.ToString() + "!!!");
            return _uri;
        }

    }
}
