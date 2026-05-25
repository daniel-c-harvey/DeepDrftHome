using DeepDrftData;
using DeepDrftModels.DTOs;
using Microsoft.AspNetCore.Mvc;
using Models.Common;
using NetBlocks.Models;

namespace DeepDrftPublic.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrackController : ControllerBase
{
    private readonly ITrackService _trackService;

    public TrackController(ITrackService trackService)
    {
        _trackService = trackService;
    }

    [HttpGet("page")]
    public async Task<ActionResult<ApiResultDto<PagedResult<TrackDto>>>> GetPage(
        [FromQuery] int pageNumber, 
        [FromQuery] int pageSize, 
        [FromQuery] string? sortColumn = null, 
        [FromQuery] bool sortDescending = false)
    {
        var result = await _trackService.GetPaged(pageNumber, pageSize, sortColumn, sortDescending);
        var apiResult = ApiResult<PagedResult<TrackDto>>.From(result);
        var dto = new ApiResultDto<PagedResult<TrackDto>>(apiResult);

        return result.Success ? Ok(dto) : StatusCode(500, dto);
    }
}