using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace PluralsightTextToAudio
{
    public static class Function1
    {
        private static HttpClient _client
            = new HttpClient();

        private static DateTime TokenExpire =
            DateTime.MinValue;

        private static string BearerToken = "";

        [FunctionName("Function1")]
        public static async Task Run([BlobTrigger("textdocuments/{name}.txt", Connection = "IncomingBlob")]string blobString, 
            [Blob("audiodocuments/{name}.mp3",FileAccess.Write,Connection = "AudioConnection")] Stream audioStream,
            string name, ILogger log)
        {
            var audioFile = await TextToAudio(blobString);

            using (var ms = new MemoryStream(audioFile))
            {
                await ms.CopyToAsync(audioStream);
            }
        }

        public static async Task<(string,DateTime)> GetAuthToken()
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage(
                HttpMethod.Post,
                "https://eastus.api.cognitive.microsoft.com/sts/v1.0/issuetoken");

            requestMessage.Headers.Add("Ocp-Apim-Subscription-Key",
                System.Environment.GetEnvironmentVariable("ApiKey",
                EnvironmentVariableTarget.Process));

            var token = await _client.SendAsync(requestMessage);

            return (await token.Content.ReadAsStringAsync(),
                DateTime.Now.AddMinutes(9));
        }

        public static async Task<byte[]> TextToAudio(string document)
        {
            if (DateTime.Now > TokenExpire)
            {
                var (token, expiration) = await GetAuthToken();

                BearerToken = "Bearer " + token;
                TokenExpire = expiration;
            }

            var audioRequestBody = GetAudioRequest(document);

            HttpRequestMessage audioRequest =
                new HttpRequestMessage(HttpMethod.Post,
                "https://eastus.tts.speech.microsoft.com/cognitiveservices/v1");

            audioRequest.Content = new StringContent(audioRequestBody);
            audioRequest.Content.Headers.ContentType =
                MediaTypeHeaderValue.Parse("application/ssml+xml");
            audioRequest.Headers.Authorization =
                AuthenticationHeaderValue.Parse(BearerToken);
            audioRequest.Headers.UserAgent.Add(
                new ProductInfoHeaderValue("PluralsightFunction", "V1.0"));
            audioRequest.Headers.Add("X-Microsoft-OutputFormat",
                "audio-24khz-48kbitrate-mono-mp3");

            var audioResult = await _client.SendAsync(audioRequest);

            return await audioResult.Content.ReadAsByteArrayAsync();
        }

        private static string GetAudioRequest(string document)
        {
            var ssml = "<speak version='1.0' xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang='en-US'>" +
                        "<voice  name='Microsoft Server Speech Text to Speech Voice (en-US, Jessa24kRUS)'>" +
                        "{{ReplaceText}}" +
                        "</voice> </speak>";

            return ssml.Replace("{{ReplaceText}}", document);
        }
    }
}
