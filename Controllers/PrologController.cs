using Microsoft.AspNetCore.Mvc;
using prolog_mobile_api.Services;

namespace prolog_mobile_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrologController(PrologService prologService) : ControllerBase
{
    // GET api/prolog/health
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "online" });

    // GET api/prolog
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var categories = await prologService.GetCategorySummariesAsync(cancellationToken);
        return Ok(categories);
    }
}
