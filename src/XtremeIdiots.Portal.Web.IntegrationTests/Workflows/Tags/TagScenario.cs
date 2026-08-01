using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.Tags;

internal sealed class TagScenario
{
    public TagScenario(bool userDefined = true)
    {
        Tag = new TagDto
        {
            TagId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Name = "Existing tag",
            Description = "Existing description",
            TagHtml = "<span class=\"badge\">Existing</span>",
            UserDefined = userDefined,
        };
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };

        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.GetTags(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApiResult<CollectionModel<TagDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<TagDto>>(new CollectionModel<TagDto>([Tag]))));
        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.GetTag(Tag.TagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ApiResult<TagDto>(HttpStatusCode.OK, new ApiResponse<TagDto>(Tag)));
        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.CreateTag(It.IsAny<TagDto>(), It.IsAny<CancellationToken>()))
            .Callback<TagDto, CancellationToken>((tag, _) => CreatedTags.Enqueue(tag))
            .ReturnsAsync(new ApiResult<TagDto>(HttpStatusCode.Created));
        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.UpdateTag(It.IsAny<TagDto>(), It.IsAny<CancellationToken>()))
            .Callback<TagDto, CancellationToken>((tag, _) => UpdatedTags.Enqueue(tag))
            .ReturnsAsync(new ApiResult<TagDto>(HttpStatusCode.OK));
        Mock.Get(RepositoryClient.Object.Tags.V1)
            .Setup(api => api.DeleteTag(Tag.TagId, It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => DeletedTagIds.Enqueue(id))
            .ReturnsAsync(new ApiResult(HttpStatusCode.OK));
    }

    public ConcurrentQueue<TagDto> CreatedTags { get; } = new();

    public ConcurrentQueue<Guid> DeletedTagIds { get; } = new();

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public TagDto Tag { get; }

    public ConcurrentQueue<TagDto> UpdatedTags { get; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);
    }
}
