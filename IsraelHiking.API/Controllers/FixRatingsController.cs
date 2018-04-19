﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IsraelHiking.Common;
using IsraelHiking.DataAccessInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IsraelHiking.API.Controllers
{
    [Route("api/[controller]")]
    public class FixRatingsController : Controller
    {
        private readonly IElasticSearchGateway _elasticSearchGateway;
        private readonly IHttpGatewayFactory _httpGatewayFactory;
        private readonly ILogger _logger;

        public FixRatingsController(IElasticSearchGateway elasticSearchGateway,
            IHttpGatewayFactory httpGatewayFactory,
            ILogger logger)
        {
            _elasticSearchGateway = elasticSearchGateway;
            _httpGatewayFactory = httpGatewayFactory;
            _logger = logger;
        }

        public async Task<IActionResult> GetFixRatings()
        {
            _logger.LogInformation("Starting rating fixing");
            var osmGateway = _httpGatewayFactory.CreateOsmGateway(null);
            var ratings = await _elasticSearchGateway.GetRatings();
            _logger.LogInformation("Rating to fix: " + ratings.Count);
            foreach (var rating in ratings)
            {
                if (rating.Source != Sources.OSM)
                {
                    continue;
                }

                foreach (var type in new [] { "relation", "node", "way" })
                {
                    var node = await osmGateway.GetElement(rating.Id, type);
                    if (node != null)
                    {
                        _logger.LogInformation("https://www.openstreetmap.org/" + type + "/" + rating.Id);
                        rating.Id = type + "_" + rating.Id;
                        await _elasticSearchGateway.UpdateRating(rating);
                        break;
                    }
                }
                
            }
            _logger.LogInformation("Finished rating fixing");
            return Ok();
        }
    }
}
