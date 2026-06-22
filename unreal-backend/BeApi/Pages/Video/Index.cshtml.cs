// Pages/Video/Index.cshtml.cs
//
// /video — 3 (line) × 9 (equipment) 그리드.
//   - 각 셀: 썸네일 (api/video/{line}/{equip}/thumbnail.jpg) + 코드 라벨
//   - 클릭 시 /video/{line}/{equipmentId} 로 이동.
//
// 9 설비는 EquipmentCatalog 사용 (CAST-01, CNC-01~03, WASH-01, ASSY-01~02, TEST-01~02).
// 3 라인은 1/2/3 고정 (현재 시스템 가정).

using BeApi.Features.Commands;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Video;

public class IndexModel : PageModel
{
    public IReadOnlyList<int> Lines { get; } = new[] { 1, 2, 3 };
    public IReadOnlyList<EquipmentCatalogItem> Equipments => EquipmentCatalog.Items;

    public void OnGet()
    {
    }
}
