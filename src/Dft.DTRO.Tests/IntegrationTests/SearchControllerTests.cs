using System.Text;
using DfT.DTRO;
using DfT.DTRO.Models;
using DfT.DTRO.Models.Filtering;
using DfT.DTRO.Models.Pagination;
using DfT.DTRO.Services.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Dft.DTRO.Tests.IntegrationTests;

public class SearchControllerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SampleDtroJsonPath = "./DtroJsonDataExamples/proper-data.json";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<IStorageService> _mockStorageService;

    public SearchControllerTests(WebApplicationFactory<Program> factory)
    {
        _mockStorageService = new Mock<IStorageService>(MockBehavior.Strict);
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(_mockStorageService.Object);
        }));
    }

    [Fact]
    public async Task Post_Search_NoDtroIsMatchingTheCriteria_ReturnsEmptyResult()
    {
        _mockStorageService.Setup(mock => mock.FindDtros(It.IsAny<DtroSearch>()))
            .Returns(Task.FromResult(
                new PaginatedResult<DfT.DTRO.Models.DTRO>(Array.Empty<DfT.DTRO.Models.DTRO>(), 0)));
        HttpClient client = _factory.CreateClient();

        DtroSearch search =
            new() { Queries = new[] { new SearchQuery { Ta = 1585 } }, Page = 1, PageSize = 10 };
        string payload = JsonConvert.SerializeObject(search);

        HttpResponseMessage response =
            await client.PostAsync("/v1/search", new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        PaginatedResponse<DtroSearchResult>? data = JsonConvert.DeserializeObject<PaginatedResponse<DtroSearchResult>>(
            await response.Content.ReadAsStringAsync()
        );
        Assert.NotNull(data);
        Assert.Equal(1, data!.Page);
        Assert.Equal(0, data.PageSize);
        Assert.Equal(0, data.TotalCount);
        Assert.Equal(0, data.Results.Count);
        Assert.Empty(data.Results);
    }

    [Fact]
    public async Task Post_Search_DtroMatchingTheCriteriaExists_ReturnsMatchingDtros()
    {
        DfT.DTRO.Models.DTRO sampleDtro = await CreateDtroObject(SampleDtroJsonPath);

        _mockStorageService.Setup(mock => mock.FindDtros(It.IsAny<DtroSearch>()))
            .Returns(Task.FromResult(new PaginatedResult<DfT.DTRO.Models.DTRO>(new[] { sampleDtro }.ToList(), 1)));
        HttpClient client = _factory.CreateClient();

        DtroSearch search =
            new() { Queries = new[] { new SearchQuery { Ta = 1585 } }, Page = 1, PageSize = 10 };
        string payload = JsonConvert.SerializeObject(search);

        HttpResponseMessage response =
            await client.PostAsync("/v1/search", new StringContent(payload, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        PaginatedResponse<DtroSearchResult>? data = JsonConvert.DeserializeObject<PaginatedResponse<DtroSearchResult>>(
            await response.Content.ReadAsStringAsync()
        );
        Assert.NotNull(data);
        Assert.Equal(1, data!.Page);
        Assert.Equal(1, data.PageSize);
        Assert.Equal(1, data.TotalCount);
        Assert.Equal(1, data.Results.Count);
        Assert.Equal(1585, data.Results.First().TrafficAuthorityId);
    }
}