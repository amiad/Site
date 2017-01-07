﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using GeoAPI.Geometries;
using IsraelHiking.API.Gpx;
using IsraelHiking.API.Gpx.GpxTypes;
using IsraelHiking.API.Services;
using IsraelHiking.API.Services.Osm;
using IsraelHiking.API.Swagger;
using IsraelHiking.Common;
using IsraelHiking.DataAccessInterfaces;
using IsraelTransverseMercator;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using Swashbuckle.Swagger.Annotations;

namespace IsraelHiking.API.Controllers
{
    /// <summary>
    /// This controller is responsible for all OSM related requests
    /// </summary>
    public class OsmController : ApiController
    {
        private readonly IHttpGatewayFactory _httpGatewayFactory;
        private readonly IDataContainerConverterService _dataContainerConverterService;
        private readonly ICoordinatesConverter _coordinatesConverter;
        private readonly IElasticSearchGateway _elasticSearchGateway;
        private readonly IAddibleGpxLinesFinderService _addibleGpxLinesFinderService;
        private readonly IOsmLineAdderService _osmLineAdderService;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly LruCache<string, TokenAndSecret> _cache;

        /// <summary>
        /// Controller's constructor
        /// </summary>
        /// <param name="httpGatewayFactory"></param>
        /// <param name="dataContainerConverterService"></param>
        /// <param name="coordinatesConverter"></param>
        /// <param name="elasticSearchGateway"></param>
        /// <param name="addibleGpxLinesFinderService"></param>
        /// <param name="osmLineAdderService"></param>
        /// <param name="configurationProvider"></param>
        /// <param name="cache"></param>
        public OsmController(IHttpGatewayFactory httpGatewayFactory,
            IDataContainerConverterService dataContainerConverterService,
            ICoordinatesConverter coordinatesConverter,
            IElasticSearchGateway elasticSearchGateway,
            IAddibleGpxLinesFinderService addibleGpxLinesFinderService,
            IOsmLineAdderService osmLineAdderService,
            IConfigurationProvider configurationProvider,
            LruCache<string, TokenAndSecret> cache)
        {
            _httpGatewayFactory = httpGatewayFactory;
            _dataContainerConverterService = dataContainerConverterService;
            _coordinatesConverter = coordinatesConverter;
            _elasticSearchGateway = elasticSearchGateway;
            _addibleGpxLinesFinderService = addibleGpxLinesFinderService;
            _osmLineAdderService = osmLineAdderService;
            _configurationProvider = configurationProvider;
            _cache = cache;
        }

        /// <summary>
        /// Get a list of highways in the given bounding box
        /// </summary>
        /// <param name="northEast">Bounding box's north-east coordinates</param>
        /// <param name="southWest">Bounding box's south-west coordinates</param>
        /// <returns>A list of features in GeoJSON format</returns>
        public async Task<List<Feature>> GetHighways(string northEast, string southWest)
        {
            return await _elasticSearchGateway.GetHighways(new LatLng(northEast), new LatLng(southWest));
        }

        /// <summary>
        /// Get the OSM server configuration
        /// </summary>
        /// <returns>The OSM server configurations</returns>
        [HttpGet]
        [Route("api/osm/configuration")]
        public OsmConfiguraionData GetConfigurations()
        {
            return _configurationProvider.OsmConfiguraion;
        }

        /// <summary>
        /// Adds a route to OSM - this requires to be logged in to OSM
        /// </summary>
        /// <param name="feature"></param>
        /// <returns></returns>
        [Authorize]
        public async Task PutGpsTraceIntoOsm(Feature feature)
        {
            var tags = feature.Attributes.GetNames().ToDictionary(n => n, n => feature.Attributes[n].ToString());
            await _osmLineAdderService.Add(feature.Geometry as LineString, tags, _cache.Get(User.Identity.Name));
        }

        /// <summary>
        /// Finds missing parts of a given route
        /// </summary>
        /// <param name="url">The url to fetch the file from - optional, use file upload if not provided</param>
        /// <returns></returns>
        [SwaggerOperationFilter(typeof(OptionalFileUploadParams))]
        [ResponseType(typeof(FeatureCollection))]
        public async Task<IHttpActionResult> PostGpsTrace(string url = "")
        {
            var fileFetcherGatewayResponse = await GetFile(url);
            if (fileFetcherGatewayResponse == null)
            {
                return BadRequest("Url is not provided or file is empty...");
            }
            var gpxBytes = await _dataContainerConverterService.Convert(fileFetcherGatewayResponse.Content, fileFetcherGatewayResponse.FileName, DataContainerConverterService.GPX);
            var gpx = gpxBytes.ToGpx().UpdateBounds();
            var highwayType = GetHighwayType(gpx);
            var gpxLines = GpxToLineStrings(gpx);
            var manipulatedLines = await _addibleGpxLinesFinderService.GetLines(gpxLines);
            var attributesTable = new AttributesTable();
            attributesTable.AddAttribute("highway", highwayType);
            var features = manipulatedLines.Select(l => new Feature(ToWgs84LineString(l.Coordinates), attributesTable) as IFeature).ToList();
            return Ok(new FeatureCollection(new Collection<IFeature>(features)));
        }

        private async Task<RemoteFileFetcherGatewayResponse> GetFile(string url)
        {
            if (string.IsNullOrEmpty(url) == false)
            {
                var fetcher = _httpGatewayFactory.CreateRemoteFileFetcherGateway(_cache.Get(User.Identity.Name));
                return await fetcher.GetFileContent(url);
            }
            var streamProvider = new MultipartMemoryStreamProvider();
            var multipartFileStreamProvider = await Request.Content.ReadAsMultipartAsync(streamProvider);

            if (multipartFileStreamProvider.Contents.Count == 0)
            {
                return null;
            }
            return new RemoteFileFetcherGatewayResponse
            {
                Content = await streamProvider.Contents.First().ReadAsByteArrayAsync(),
                FileName = streamProvider.Contents.First().Headers.ContentDisposition.FileName.Trim('"')
            };
        }

        private string GetHighwayType(gpxType gpx)
        {
            var waypointsGroups = new List<wptType[]>();
            waypointsGroups.AddRange((gpx.rte ?? new rteType[0]).Select(route => route.rtept).Where(ps => ps.All(p => p.timeSpecified)).ToArray());
            waypointsGroups.AddRange((gpx.trk ?? new trkType[0]).Select(track => track.trkseg.SelectMany(s => s.trkpt).ToArray()).Where(ps => ps.All(p => p.timeSpecified)));
            return GetHighwayTypeFromWaypoints(waypointsGroups);
        }

        /// <summary>
        /// Determines routing type by calculating the average speed of each set of points.
        /// Assuming all the point sent has time specified.
        /// </summary>
        /// <param name="waypointsGoups">A list of group of points</param>
        /// <returns>The calculated routing type</returns>
        private string GetHighwayTypeFromWaypoints(IReadOnlyCollection<wptType[]> waypointsGoups)
        {
            var velocityList = new List<double>();
            if (waypointsGoups.Count == 0)
            {
                return "track";
            }
            foreach (var waypoints in waypointsGoups)
            {
                var lengthInKm = ToItmLineString(waypoints).Length/1000;
                var timeInHours = (waypoints.Last().time - waypoints.First().time).TotalHours;
                velocityList.Add(lengthInKm/timeInHours);
            }
            var averageVelocity = velocityList.Sum()/velocityList.Count;
            if (averageVelocity <= 6)
            {
                return "footway";
            }
            if (averageVelocity <= 12)
            {
                return "cycleway";
            }
            return "track";
        }

        private LineString ToItmLineString(IEnumerable<wptType> waypoints)
        {
            var coordinates = waypoints.Select(wptType =>
            {
                var northEast = _coordinatesConverter.Wgs84ToItm(new LatLon
                {
                    Longitude = (double) wptType.lon,
                    Latitude = (double) wptType.lat
                });
                return new Coordinate(northEast.East, northEast.North);
            }).ToArray();
            return new LineString(coordinates);
        }

        private LineString ToWgs84LineString(IEnumerable<Coordinate> coordinates)
        {
            var cwgs84Coordinates = coordinates.Select(coordinate =>
            {
                var latLng = _coordinatesConverter.ItmToWgs84(new NorthEast
                {
                    North = (int) coordinate.Y,
                    East = (int) coordinate.X
                });
                return new Coordinate(latLng.Longitude, latLng.Latitude);
            }).ToArray();
            return new LineString(cwgs84Coordinates);
        }

        private List<LineString> GpxToLineStrings(gpxType gpx)
        {
            var lineStings = (gpx.rte ?? new rteType[0])
                .Select(route => ToItmLineString(route.rtept)).ToList();
            var tracksPointsList = (gpx.trk ?? new trkType[0])
                .Select(track => track.trkseg.SelectMany(s => s.trkpt).ToArray())
                .Select(ToItmLineString);
            lineStings.AddRange(tracksPointsList);
            return lineStings;
        }
    }
}
