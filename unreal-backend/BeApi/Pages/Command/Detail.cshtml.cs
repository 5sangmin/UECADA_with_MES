// Pages/Command/Detail.cshtml.cs
//
// 명령 상세 — CommandService.GetByCommandIdAsync(int) 직접 호출.
// 쿼리: ?command_id=123

using BeApi.Features.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Command;

public class DetailModel : PageModel
{
    private readonly CommandService _commandService;

    public DetailModel(CommandService commandService)
    {
        _commandService = commandService;
    }

    [BindProperty(SupportsGet = true, Name = "command_id")]
    public int CommandId { get; set; }

    public CommandDetailDto? Detail { get; private set; }
    public bool IsNotFound { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (CommandId <= 0)
        {
            IsNotFound = true;
            return;
        }
        Detail = await _commandService.GetByCommandIdAsync(CommandId, ct).ConfigureAwait(false);
        IsNotFound = Detail == null;
    }
}
