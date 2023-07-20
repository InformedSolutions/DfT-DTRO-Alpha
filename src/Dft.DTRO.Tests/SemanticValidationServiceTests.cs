﻿using System.Dynamic;
using DfT.DTRO.Services;
using DfT.DTRO.Services.Storage;
using DfT.DTRO.Services.Validation;
using Microsoft.Extensions.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Dft.DTRO.Tests;
public class SemanticValidationServiceTests
{
    private readonly Mock<ISystemClock> _mockClock;
    private readonly Mock<IStorageService> _mockStorageService;
    private readonly Mock<IConditionValidationService> _mockConditionValidationService;

    public SemanticValidationServiceTests()
    {
        _mockClock = new Mock<ISystemClock>();
        _mockStorageService = new Mock<IStorageService>();
        _mockConditionValidationService = new Mock<IConditionValidationService>();

        _mockClock.Setup(it => it.UtcNow).Returns(new DateTime(year: 2023, month: 5, day: 1));
    }

    [Fact]
    public async Task AllowsLastUpdateDateInThePast()
    {
        var dtro = PrepareDtro(@"{""lastUpdateDate"": ""2012-04-23T18:25:43.511Z""}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DisallowsLastUpdateDateInTheFuture()
    {
        var dtro = PrepareDtro(@"{""lastUpdateDate"": ""2027-04-23T18:25:43.511Z""}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task AllowsCoordinatesWithinBoundingBoxOsgb()
    {
        var dtro = PrepareDtro(@"{""geometry"": { ""crs"": ""osgb36Epsg27700"", ""coordinates"": {
            ""type"": ""Polygon"", ""coordinates"": [[[-10000, -10000],[0,0]]]}}}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AllowsCoordinatesWithinBoundingBoxWgs()
    {
        var dtro = PrepareDtro(@"{""geometry"": { ""crs"": ""wgs84Epsg4326"", ""coordinates"": {
            ""type"": ""Polygon"", ""coordinates"": [[[1, 55],[-3,60.3]]]}}}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DisallowsCoordinatesOutsideOfBoundingBoxOsgb()
    {
        var dtro = PrepareDtro(@"{""geometry"": { ""crs"": ""wgs84Epsg4326"", ""coordinates"": {
            ""type"": ""Polygon"", ""coordinates"": [[[-103940, 55],[-3,2000000.44]]]}}}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task DisallowsCoordinatesOutsideOfBoundingBoxWgs()
    {
        var dtro = PrepareDtro(@"{""geometry"": { ""crs"": ""wgs84Epsg4326"", ""coordinates"": {
            ""type"": ""Polygon"", ""coordinates"": [[[-8, 48],[3,60.9]]]}}}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task AllowsDtroWithEmptyReferenceToAnotherDtro()
    {
        DfT.DTRO.Models.DTRO dtro = PrepareDtro(@"{""crossRefTro"": []}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }
    
    [Fact]
    public async Task AllowsDtroWithoutReferenceToAnotherDtro()
    {
        DfT.DTRO.Models.DTRO dtro = PrepareDtro(@"{}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DoesNotApplyValidationForDtroFromExcludedSchema()
    {
        _mockStorageService.Setup(it => it.DtroExists(It.IsAny<Guid>())).ReturnsAsync(false);

        Guid id = new("5ca30f2d-1270-4b37-99ca-21c5afd79ccb");
        DfT.DTRO.Models.DTRO dtro = PrepareDtro(
            $@"{{""source"": {{ ""crossRefTro"": [""{id.ToString()}""] }} }}",
            "3.1.1"
        );

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        _mockStorageService.VerifyNoOtherCalls();
        Assert.Empty(result);
    }
    
    [Fact]
    public async Task AppliesValidationForDtroWithMatchingSchema()
    {
        _mockStorageService.Setup(it => it.DtroExists(It.IsAny<Guid>())).ReturnsAsync(false);

        Guid id = new("5ca30f2d-1270-4b37-99ca-21c5afd79ccb");
        DfT.DTRO.Models.DTRO dtro = PrepareDtro(
            $@"{{""source"": {{ ""crossRefTro"": [""{id.ToString()}""] }} }}",
            "3.1.2"
        );

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        _mockStorageService.Verify(it => it.DtroExists(id));
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task AllowsReferencingExistingDtro()
    {
        _mockStorageService.Setup(it => it.DtroExists(It.IsAny<Guid>())).ReturnsAsync(true);

        Guid id = new("5ca30f2d-1270-4b37-99ca-21c5afd79ccb");
        DfT.DTRO.Models.DTRO dtro = PrepareDtro($@"{{""source"": {{ ""crossRefTro"": [""{id.ToString()}""] }} }}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        _mockStorageService.Verify(it => it.DtroExists(id));
        Assert.Empty(result);
    }

    [Fact]
    public async Task DisallowsReferencingNonExistingDtro()
    {
        _mockStorageService.Setup(it => it.DtroExists(It.IsAny<Guid>())).ReturnsAsync(false);

        Guid id = new("5ca30f2d-1270-4b37-99ca-21c5afd79ccb");
        DfT.DTRO.Models.DTRO dtro = PrepareDtro($@"{{""source"": {{ ""crossRefTro"": [""{id.ToString()}""] }} }}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.NotEmpty(result);
        _mockStorageService.Verify(it => it.DtroExists(id));
    }

    [Fact]
    public async Task AllowsPoints()
    {
        var dtro = PrepareDtro(@"{""geometry"": { ""crs"": ""osgb36Epsg27700"", ""coordinates"": {
                  ""type"": ""Point"", ""coordinates"": [0,0]}}}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AllowsLineStrings()
    {
        var dtro = PrepareDtro(@"{""geometry"": { ""crs"": ""osgb36Epsg27700"", ""coordinates"": {
                  ""type"": ""LineString"", ""coordinates"": [[0,0],[0,0],[0,0]]}}}");

        var sut = new SemanticValidationService(_mockClock.Object, _mockStorageService.Object, _mockConditionValidationService.Object);

        var result = await sut.ValidateCreationRequest(dtro);

        Assert.Empty(result);
    }

    private static DfT.DTRO.Models.DTRO PrepareDtro(string jsonData, string schemaVersion = "10.0.0")
    {
        return new()
        {
            Data = JsonConvert.DeserializeObject<ExpandoObject>(jsonData, new ExpandoObjectConverter()),
            SchemaVersion = schemaVersion
        };
    }
}
