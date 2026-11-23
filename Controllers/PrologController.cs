using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using prolog_mobile_api.Clients;
using prolog_mobile_api.Services;

namespace prolog_mobile_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrologController(PrologService prologService) : ControllerBase
{
    // GET api/prolog/health
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "online" });

    // GET api/prolog?page=1&pageSize=25
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, PrologApiClient.MaxPageSize)] int pageSize = PrologApiClient.MaxPageSize,
        CancellationToken cancellationToken = default)
    {
        var products = await prologService.GetProductsAsync(page, pageSize, cancellationToken);
        return Ok(products);
    }
}
