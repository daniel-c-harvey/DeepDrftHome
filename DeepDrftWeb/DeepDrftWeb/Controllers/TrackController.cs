using DeepDrftModels.Entities;
using DeepDrftModels.Models;
using DeepDrftWeb.Services;
using Microsoft.AspNetCore.Mvc;
using NetBlocks.Models;

namespace DeepDrftWeb.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly TrackService _trackService;

    public TrackController(TrackService trackService)
    {
        _trackService = trackService;
    }

    [HttpGet("page")]
    public async Task<ActionResult<ApiResultDto<PagedResult<TrackEntity>>>> GetPage(
        [FromQuery] int pageNumber, 
        [FromQuery] int pageSize, 
        [FromQuery] string? sortColumn = null, 
        [FromQuery] bool sortDescending = false)
    {
        var result = await _trackService.GetPaged(pageNumber, pageSize, sortColumn, sortDescending);
        var apiResult = ApiResult<PagedResult<TrackEntity>>.From(result);
        var dto = new ApiResultDto<PagedResult<TrackEntity>>(apiResult);

        return result.Success ? Ok(dto) : StatusCode(500, dto);
    }
}