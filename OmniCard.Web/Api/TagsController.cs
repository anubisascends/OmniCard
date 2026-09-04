using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>The tag library (for autocomplete when editing a card's tags).</summary>
public sealed class TagsController(ITagService tags) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<TagDto>> Get() =>
        tags.GetAllTags().Select(t => new TagDto(t.Id, t.Name, t.UsageCount)).ToList();
}
