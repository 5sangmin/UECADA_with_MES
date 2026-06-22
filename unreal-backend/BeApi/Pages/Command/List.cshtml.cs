// Pages/Command/List.cshtml.cs
//
// 명령 이력 목록 — CommandService.GetPagedAsync 직접 호출.
// 쿼리: ?page=1&page_size=50

using BeApi.Features.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Command;

public class ListModel : PageModel
{
    private readonly CommandService _commandService;

    public ListModel(CommandService commandService)
    {
        _commandService = commandService;
    }

    public CommandPageDto? PageData { get; private set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true, Name = "page_size")]
    public int PageSize { get; set; } = 50;

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (PageNumber < 1) PageNumber = 1;
        if (PageSize < 1) PageSize = 50;
        if (PageSize > 500) PageSize = 500;

        PageData = await _commandService.GetPagedAsync(PageNumber, PageSize, ct).ConfigureAwait(false);
    }
}
