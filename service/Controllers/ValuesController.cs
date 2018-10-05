using System;
using Microsoft.AspNetCore.Mvc;

namespace istio_abtest.Controllers
{
	[Route("api/[controller]")]
    [ApiController]
    public class VersionController : ControllerBase
    {
        // GET api/values
        [HttpGet]
        public ActionResult<string> Get()
        {
            return Environment.GetEnvironmentVariable("IMAGE_TAG") ?? "Unknown";
        }
    }
}
