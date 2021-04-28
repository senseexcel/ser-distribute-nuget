namespace DistributeService.Controllers
{   
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using NLog;
    #endregion

    [ApiController]
    [Route("[controller]")]
    public class DistributeController : ControllerBase
    {
        #region Logger
        private readonly static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties

        #endregion

        #region Constructor
        public DistributeController()
        {
          
        }
        #endregion

        [HttpPost]
        [Route("/distibute")]
        [Consumes("multipart/form-data")]
        [Produces("application/json", Type = typeof(Guid))]
        [RequestFormLimits(MultipartBodyLengthLimit = 262144000)]
        [RequestSizeLimit(262144000)]
        public IActionResult UploadWithId([FromBody][Required]  object distributeConfig, [FromRoute][Required] Guid fileId)
        {
            try
            {
                logger.Debug($"Start upload file with Id: '{fileId}'...");
                var result = Service.Upload(fileId, file);
                return Ok(result);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"The request method '{nameof(UploadWithId)}' failed.");
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Checks the service is Online
        /// </summary>
        /// <returns>Status</returns>
        [HttpGet]
        [Route("/online")]
        public IActionResult Online()
        {
            try
            {
                logger.Debug($"Request in method '{nameof(Online)}' comes in...");
                return Ok();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"The request '{nameof(Online)}' failed.");
                return BadRequest(ex.Message);
            }
        }
    }
}
