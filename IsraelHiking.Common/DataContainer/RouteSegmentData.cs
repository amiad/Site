﻿using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IsraelHiking.Common.DataContainer
{
    public static class RoutingType
    {
        public const string HIKE = "Hike";
        public const string BIKE = "Bike";
        public const string CAR = "Car";
        public const string FOUR_WHEEL_DRIVE = "4WD";
        public const string NONE = "None";
    }

    public class RouteSegmentData
    {
        [JsonPropertyName("routingType")]
        public string RoutingType { get; set; }
        [JsonPropertyName("routePoint")]
        public LatLng RoutePoint { get; set; }
        [JsonPropertyName("latlngs")]
        public List<LatLngTime> Latlngs { get; set; }

        public RouteSegmentData()
        {
            Latlngs = new List<LatLngTime>();
        }
    }
}