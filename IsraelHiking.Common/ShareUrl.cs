using IsraelHiking.Common.DataContainer;
using System;
using System.Text.Json.Serialization;

namespace IsraelHiking.Common
{
    public class ShareUrl
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("description")]
        public string Description { get; set; }
        [JsonPropertyName("osmUserId")]
        public string OsmUserId { get; set; }
        [JsonPropertyName("viewsCount")]
        public int ViewsCount { get; set; }
        [JsonPropertyName("creationDate")]
        public DateTime CreationDate { get; set; }
        [JsonPropertyName("lastViewed")]
        public DateTime LastViewed { get; set; }

        [JsonPropertyName("dataContainer")]
        public DataContainerPoco DataContainer { get; set; }
    }
}
