// Pages/Index.cshtml.cs
//
// 대시보드 PageModel:
//   - PR3 ConnectionStatusStore → 4개 타깃 상태 + overall
//   - PR4 LatestService → 27설비 latest + 슬롯 source 카운트
//
// 핸들러는 단순 GET. 페이지 새로고침으로 갱신.

using BeApi.Features.ConnectionStatus;
using BeApi.Features.Latest;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages;

public class IndexModel : PageModel
{
    private readonly ConnectionStatusStore _statusStore;
    private readonly LatestService _latestService;

    public IndexModel(ConnectionStatusStore statusStore, LatestService latestService)
    {
        _statusStore = statusStore;
        _latestService = latestService;
    }

    public IReadOnlyList<ConnectionStatusDto> StatusTargets { get; private set; } = Array.Empty<ConnectionStatusDto>();
    public ConnectionState Overall { get; private set; } = ConnectionState.Unknown;
    public EquipmentLatestEnvelopeDto? LatestEnvelope { get; private set; }
    public IReadOnlyDictionary<string, int> SourceCounts { get; private set; } = new Dictionary<string, int>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        StatusTargets = _statusStore.GetAllDto();
        Overall = _statusStore.GetOverall();

        LatestEnvelope = await _latestService.GetAllLatestAsync(ct).ConfigureAwait(false);

        SourceCounts = LatestEnvelope.Items
            .GroupBy(i => i.Source)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
