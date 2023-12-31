/*
 * DTRO - OpenAPI 3.0
 *
 * A prototype implementation of API endpoints for publishing and consuming Traffic Regulation orders.
 *
 * OpenAPI spec version: 0.0.1
 *
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DfT.DTRO.Attributes;
using DfT.DTRO.Caching;
using DfT.DTRO.FeatureManagement;
using DfT.DTRO.Models;
using DfT.DTRO.RequestCorrelation;
using DfT.DTRO.Services.Storage;
using DfT.DTRO.Services.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace DfT.DTRO.Controllers;

/// <summary>
/// Prototype controller for capturing DTROs.
/// </summary>
[ApiController]
[Consumes("application/json")]
[Produces("application/json")]
public class DTROsController : ControllerBase
{
    private readonly IJsonSchemaValidationService _jsonSchemaValidationService;
    private readonly IStorageService _storageService;
    private readonly IRequestCorrelationProvider _correlationProvider;
    private readonly ISemanticValidationService _semanticValidationService;
    private readonly ILogger<DTROsController> _logger;
    private readonly IJsonLogicValidationService _jsonLogicValidationService;
    private readonly IDtroCache _dtroCache;

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="jsonSchemaValidationService">An <see cref="IJsonSchemaValidationService"/> instance.</param>
    /// <param name="semanticValidationService">An <see cref="ISemanticValidationService"/> instance.</param>
    /// <param name="storageService">An <see cref="IStorageService"/> instance.</param>
    /// <param name="correlationProvider">An <see cref="IRequestCorrelationProvider"/> instance.</param>
    /// <param name="jsonLogicValidationService">An <see cref="IJsonLogicValidationService"/> instance.</param>
    /// <param name="dtroCache">An <see cref="IDtroCache"/> instance.</param>
    /// <param name="logger">An <see cref="ILogger{DTROsController}"/> instance.</param>
    public DTROsController(
        IJsonSchemaValidationService jsonSchemaValidationService,
        ISemanticValidationService semanticValidationService,
        IStorageService storageService,
        IRequestCorrelationProvider correlationProvider,
        IJsonLogicValidationService jsonLogicValidationService,
        IDtroCache dtroCache,
        ILogger<DTROsController> logger)
    {
        _jsonSchemaValidationService = jsonSchemaValidationService;
        _semanticValidationService = semanticValidationService;
        _storageService = storageService;
        _correlationProvider = correlationProvider;
        _logger = logger;
        _jsonLogicValidationService = jsonLogicValidationService;
        _dtroCache = dtroCache;
    }

    /// <summary>
    /// Creates a new DTRO.
    /// </summary>
    /// <param name="body">A DTRO submission that satisfies the schema for the model version being submitted.</param>
    /// <response code="201">Created.</response>
    /// <response code="400">Bad request.</response>
    /// <response code="404">Not found.</response>
    /// <returns>Id of the DTRO.</returns>
    [HttpPost]
    [Route("/v1/dtros")]
    [ValidateModelState]
    [FeatureGate(FeatureNames.DtroWrite)]
    [SwaggerResponse(201, type: typeof(DTROResponse), description: "Created")]
    public async Task<IActionResult> CreateDtro([FromBody] Models.DTRO body)
    {
        const string methodName = "dtro.create";

        var response = new DTROResponse();

        _logger.LogInformation("[{method}] Creating DTRO with ID {dtroId}", methodName, response.Id);

        var errors = new List<string>();

        if (await ValidateDtro(body) is IActionResult errorResult)
        {
            return errorResult;
        }

        body.LastUpdated = DateTime.UtcNow;
        body.Created = body.LastUpdated;

        body.LastUpdatedCorrelationId = _correlationProvider.CorrelationId;
        body.CreatedCorrelationId = body.LastUpdatedCorrelationId;

        await _storageService.SaveDtroAsJson(response.Id, body);

        _logger.LogInformation(new EventId(201, "Created"), "[{method}] Successfully created DTRO with ID {dtroId}", methodName, response.Id);

        return CreatedAtAction(nameof(GetDtroById), new { id = response.Id }, response);
    }

    /// <summary>
    /// Updates an existing DTRO.
    /// </summary>
    /// <remarks>
    /// The payload requires a full DTRO which will replace the TRO with the quoted ID.
    /// </remarks>
    /// <response code="200">Ok.</response>
    /// <response code="400">Bad request.</response>
    /// <response code="404">Not found.</response>
    /// <returns>Id of the updated DTRO.</returns>
    [HttpPut]
    [Route("/v1/dtros/{id}")]
    [ValidateModelState]
    [FeatureGate(FeatureNames.DtroWrite)]
    [SwaggerResponse(statusCode: 200, type: typeof(DTROResponseDto), description: "Okay")]
    public async Task<IActionResult> UpdateDtro([FromRoute] Guid id, [FromBody] Models.DTRO body)
    {
        const string methodName = "dtro.update";

        _logger.LogInformation("[{method}] Updating DTRO with ID {dtroId}", methodName, id);

        if (await ValidateDtro(body) is IActionResult errorResult)
        {
            return errorResult;
        }

        body.LastUpdated = DateTime.UtcNow;
        body.LastUpdatedCorrelationId = _correlationProvider.CorrelationId;

        if (!await _storageService.TryUpdateDtroAsJson(id, body))
        {
            return NotFound(
                new ApiErrorResponse("Not found", $"D-TRO with id {id} not found"));
        }

        var response = new DTROResponse
        {
            Id = id
        };

        _logger.LogInformation(new EventId(200, "Updated"), "[{method}] Successfully updated DTRO with ID {dtroId}", methodName, id);

        await _dtroCache.InvalidateDtro(id);

        return Ok(response);
    }

    /// <summary>
    /// Gets a DTRO by ID.
    /// </summary>
    /// <response code="200">Okay.</response>
    /// <response code="404">Not found.</response>
    [HttpGet]
    [Route("/v1/dtros/{id}")]
    [FeatureGate(RequirementType.Any, FeatureNames.DtroRead, FeatureNames.DtroWrite)]
    public async Task<IActionResult> GetDtroById(Guid id)
    {
        const string methodName = "dtro.get_by_id";
        _logger.LogInformation("[{method}] Getting DTRO with ID {dtroId}", methodName, id);

        var cachedDtro = await _dtroCache.GetDtro(id);

        if (cachedDtro is not null)
        {
            return Ok(DTROResponseDto.FromDTRO(cachedDtro));
        }

        Models.DTRO dtroDomainObject = await _storageService.GetDtroById(id);

        if (dtroDomainObject is null || dtroDomainObject.Deleted)
        {
            return NotFound(
                new ApiErrorResponse("Not found", $"D-TRO with id {id} not found"));
        }

        await _dtroCache.CacheDtro(dtroDomainObject);

        return Ok(DTROResponseDto.FromDTRO(dtroDomainObject));
    }

    /// <summary>
    /// Marks a DTRO as deleted.
    /// </summary>
    /// <param name="id">Id of the DTRO.</param>
    /// <response code="204">Okay.</response>
    [HttpDelete("/v1/dtros/{id}")]
    [FeatureGate(FeatureNames.DtroWrite)]
    [SwaggerResponse(statusCode: 204, description: "Successfully deleted the DTRO.")]
    [SwaggerResponse(statusCode: 404, description: "Could not find a DTRO with the specified id.")]
    public async Task<IActionResult> DeleteDtro(Guid id)
    {
        var deleted = await _storageService.DeleteDtro(id);

        if (!deleted)
        {
            return NotFound(new ApiErrorResponse("Not found", $"D-TRO with id {id} not found"));
        }

        await _dtroCache.InvalidateDtro(id);
        await _dtroCache.InvalidateDtroExists(id);

        return NoContent();
    }

    /// <summary>
    /// Validates the DTRO.
    /// </summary>
    /// <param name="dtro">The DTRO to validate.</param>
    /// <returns>
    /// <see langword="null"/> if the validation succeeds;
    /// otherwise an <see cref="IActionResult"/> explaining the errors.
    /// </returns>
    private async Task<IActionResult> ValidateDtro(Models.DTRO dtro)
    {
        string jsonSchemaAsString;

        try
        {
            jsonSchemaAsString = _jsonSchemaValidationService.GetJsonSchemaForRequestAsString(dtro);
        }
        catch (FileNotFoundException)
        {
            // If file of quoted name could not be found this indicates invalid schema version ID.
            return NotFound(
                new ApiErrorResponse("Not found", "Schema version not found"));
        }

        var validationErrors = _jsonSchemaValidationService.ValidateRequestAgainstJsonSchema(jsonSchemaAsString, dtro.DtroDataToJsonString());

        if (validationErrors.Count > 0)
        {
            return BadRequest(
                new ApiErrorResponse("Bad request", new List<object>(validationErrors)));
        }

        var logicValidationErrors = await _jsonLogicValidationService.ValidateCreationRequest(dtro);

        if (logicValidationErrors.Count > 0)
        {
            return BadRequest(
                new ApiErrorResponse("Bad request", new List<object>(logicValidationErrors)));
        }

        var semanticValidationErrors = await _semanticValidationService.ValidateCreationRequest(dtro);

        if (semanticValidationErrors.Count > 0)
        {
            return BadRequest(
                new ApiErrorResponse("Bad request", new List<object>(semanticValidationErrors)));
        }

        return null;
    }
}