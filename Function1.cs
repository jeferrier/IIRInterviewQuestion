using System;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace APIConsumer
{
    // A class to represent data retrieved from the API endpoint
    public class EventData
    {
        public int? id {get; set;}
        public string? name {get; set;}
        public string? program {get; set;}
        public DateTime dateStart {get; set;}
        public DateTime dateEnd {get; set;}
        public Uri? url {get; set;}
        public string? owner {get; set;}

        public EventData() {
        }
    }

    // A class to filter the properties which will make it into response JSON based on EventData class
    public class EventDataJSONFilter
    {
        public Uri? websiteUrl {get; set;}
        public string? name {get; set;}
        public int? days {get; set;}

        public EventDataJSONFilter(int days, string name, Uri websiteUrl) {
            this.days = days;
            this.name = name;
            this.websiteUrl = websiteUrl;
        }
    }
    public class Function1
    {
        private static HttpClient eventDataClient;
        private readonly ILogger<Function1> _logger;

        public Function1(ILogger<Function1> logger)
        {
            _logger = logger;
            eventDataClient = new HttpClient();
            eventDataClient.BaseAddress = new Uri("https://iir-interview-homework-ddbrefhkdkcgdpbs.eastus2-01.azurewebsites.net/api/v1.0/");
        }

        [Function("Function1")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route ="events/{id:int}")] HttpRequest req, int id)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            HttpResponseMessage eventDataResponse;
            EventData[]? responseEventDatas;
            EventData searchedForEventData = null;

            // Attempt to call the API until we find the event we are looking for, OR we retry five times
            int apiCallAttemptCount = 0;
            while (
                searchedForEventData is null
                && apiCallAttemptCount < 5
            ) {
                apiCallAttemptCount++;
                // We wrap this whole block in try/catch so that we can intercept when we get a 500 from the API
                // NOTE: Unhandled here is the situation where the API sends malformed JSON. It would have been easy to implement, but was outside the scope of the request.
                try
                {
                    _logger.LogInformation("Attempting API call" + apiCallAttemptCount);
                    // Call the API.
                    // 500 responses will throw an exception on the next line.
                    eventDataResponse = await eventDataClient.GetAsync(
                        "event-data"
                    );
                    eventDataResponse.EnsureSuccessStatusCode();

                    // Gather the JSON from the response and store it as a string for deserialization.at
                    string httpResponseJSON = await eventDataResponse.Content.ReadAsStringAsync();

                    // NOTE: Potentially unhandled malformed JSON exception here
                    responseEventDatas = JsonSerializer.Deserialize<EventData[]>(httpResponseJSON);

                    // This is in case I did something wrong, debug purposes mostly. I kept it because it was a sane check to make.
                    if (responseEventDatas is null) {
                        _logger.LogInformation("Response data is null");
                        continue;
                    }

                }
                catch (HttpRequestException)
                {
                    _logger.LogInformation("500 returned by API");
                    continue;
                }

                // Scan the results to see if there are any entries matching our query ID
                for (int a = 0; a < responseEventDatas.Length; a++) {
                    EventData currentEventData = responseEventDatas[a];
                    // If we found the record we're looking for, store it in a variable declared outside of this loop
                    if (currentEventData.id == id) {
                        searchedForEventData = currentEventData;
                    }
                }
            }

            // After we found the item OR we made 5 API attempts
            // Check if the item was actually found
            if (
                searchedForEventData is not null
            ) {
                // If it is, pass it into the filtering class to be serialized and sent as JSON in the response
                return new OkObjectResult(JsonSerializer.Serialize(new EventDataJSONFilter(
                    (int)(searchedForEventData.dateEnd - searchedForEventData.dateStart).TotalDays,
                    searchedForEventData.name,
                    searchedForEventData.url
                )));
            }

            // Otherwise, if the item was not found, simply return a 500
            return new StatusCodeResult(500);
        }
    }
}