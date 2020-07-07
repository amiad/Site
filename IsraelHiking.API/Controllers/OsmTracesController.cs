﻿using IsraelHiking.API.Services;
using IsraelHiking.Common;
using IsraelHiking.Common.Configuration;
using IsraelHiking.Common.DataContainer;
using IsraelHiking.Common.Extensions;
using IsraelHiking.DataAccessInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using OsmSharp.API;
using OsmSharp.IO.API;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IsraelHiking.API.Controllers
{
    /// <summary>
    /// This controller is responsible for managing OSM traces
    /// </summary>
    [Route("api/osm/trace")]
    public class OsmTracesController : ControllerBase
    {
        private readonly IClientsFactory _clientsFactory;
        private readonly IElevationDataStorage _elevationDataStorage;
        private readonly IDataContainerConverterService _dataContainerConverterService;
        private readonly IImageCreationService _imageCreationService;
        private readonly UsersIdAndTokensCache _cache;
        private readonly ConfigurationData _options;

        /// <summary>
        /// Controller's constructor
        /// </summary>
        /// <param name="clientsFactory"></param>
        /// <param name="elevationDataStorage"></param>
        /// <param name="dataContainerConverterService"></param>
        /// <param name="options"></param>
        /// <param name="imageCreationService"></param>
        /// <param name="cache"></param>
        public OsmTracesController(IClientsFactory clientsFactory,
            IElevationDataStorage elevationDataStorage,
            IDataContainerConverterService dataContainerConverterService,
            IOptions<ConfigurationData> options,
            IImageCreationService imageCreationService,
            UsersIdAndTokensCache cache)
        {
            _clientsFactory = clientsFactory;
            _elevationDataStorage = elevationDataStorage;
            _dataContainerConverterService = dataContainerConverterService;
            _imageCreationService = imageCreationService;
            _options = options.Value;
            _cache = cache;
        }

        /// <summary>
        /// Get OSM user traces
        /// </summary>
        /// <returns>A list of traces</returns>
        [Authorize]
        [HttpGet]
        public async Task<Trace[]> GetTraces()
        {
            var gateway = CreateClient();
            var gpxFiles = await gateway.GetTraces();
            return gpxFiles.Select(GpxFileToTrace).ToArray();
        }

        /// <summary>
        /// Get OSM user traces
        /// </summary>
        /// <returns>A list of traces</returns>
        [Authorize]
        [HttpGet("{id}")]
        public async Task<DataContainerPoco> GetTraceById(int id)
        {
            var gateway = CreateClient();
            var file = await gateway.GetTraceData(id);
            using MemoryStream memoryStream = new MemoryStream();
            file.Stream.CopyTo(memoryStream);
            var dataContainer = await _dataContainerConverterService.ToDataContainer(memoryStream.ToArray(), file.FileName);
            foreach (var latLng in dataContainer.Routes.SelectMany(routeData => routeData.Segments.SelectMany(routeSegmentData => routeSegmentData.Latlngs)))
            {
                latLng.Alt = await _elevationDataStorage.GetElevation(latLng.ToCoordinate());
            }
            return dataContainer;
        }

        /// <summary>
        /// Creates an image for a trace
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [Authorize]
        [HttpGet("{id}/picture")]
        public async Task<IActionResult> GetTraceByIdImage(int id)
        {
            var container = await GetTraceById(id);
            container.BaseLayer = new LayerData();
            var image = await _imageCreationService.Create(container, 128, 128);
            return new FileContentResult(image, new MediaTypeHeaderValue("image/png"));
        }


        /// <summary>
        /// Allows upload of traces to OSM
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PostUploadGpsTrace(IFormFile file)
        {
            if (file == null)
            {
                return new BadRequestResult();
            }
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var gateway = CreateClient();
            memoryStream.Seek(0, SeekOrigin.Begin);
            await gateway.CreateTrace(new GpxFile
            {
                Name = file.FileName,
                Description = Path.GetFileNameWithoutExtension(file.FileName),
                Visibility = Visibility.Private
            }, memoryStream);
            return Ok();
        }

        /// <summary>
        /// Allows update OSM trace meta data
        /// </summary>
        /// <param name="id">The Id of the trace</param>
        /// <param name="trace">The trace data</param>
        /// <returns></returns>
        [Authorize]
        [Route("{id}")]
        [HttpPut]
        public async Task<IActionResult> PutGpsTrace(string id, [FromBody]Trace trace)
        {
            if (id != trace.Id)
            {
                return BadRequest("trace id and url id do not match");
            }
            var gateway = CreateClient();
            await gateway.UpdateTrace(TraceToGpxFile(trace));
            return Ok(trace);
        }

        /// <summary>
        /// Allows the deletion of OSM trace
        /// </summary>
        /// <param name="id">The Id of the trace</param>
        /// <returns></returns>
        [Authorize]
        [Route("{id}")]
        [HttpDelete]
        public async Task<IActionResult> DeleteGpsTrace(long id)
        {
            var gateway = CreateClient();
            await gateway.DeleteTrace(id);
            return Ok();
        }

        private IAuthClient CreateClient()
        {
            var user = _cache.Get(User.Identity.Name);
            return _clientsFactory.CreateOAuthClient(_options.OsmConfiguration.ConsumerKey, _options.OsmConfiguration.ConsumerSecret, user.Token, user.TokenSecret);
        }

        private Trace GpxFileToTrace(GpxFile gpxFile)
        {
            return new Trace
            {
                Id = gpxFile.Id.ToString(),
                Name = gpxFile.Name,
                Description = gpxFile.Description,
                ImageUrl = Request.Scheme + "://" + Request.Host + Url.Content("~/api/osm/trace/") + gpxFile.Id + "/picture",
                Url = $"https://www.openstreetmap.org/user/{gpxFile.User}/traces/{gpxFile.Id}",
                TagsString = string.Join(",", gpxFile.Tags),
                TimeStamp = gpxFile.TimeStamp,
                Visibility = gpxFile.Visibility.ToString().ToLower()
            };
        }

        private GpxFile TraceToGpxFile(Trace trace)
        {
            return new GpxFile
            {
                Id = int.Parse(trace.Id),
                Name = trace.Name,
                Description = trace.Description,
                Tags = trace.TagsString?.Split(",", StringSplitOptions.RemoveEmptyEntries).ToArray() ?? new string[0],
                TimeStamp = trace.TimeStamp,
                Visibility = Enum.Parse<Visibility>(trace.Visibility, true)
            };
        }
    }
}
