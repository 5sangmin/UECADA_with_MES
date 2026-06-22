// Pages/Command/Request.cshtml.cs
//
// 운영자가 직접 명령 전송:
//   - 설비 드롭다운 (EquipmentCatalog 9개)
//   - command_type 드롭다운 — 선택한 설비 prefix 에 맞춰 동적 옵션 (서버에서 모두 내려준 뒤
//     클라이언트가 JS 로 필터; JS 가 비활성이면 그냥 전체 목록에서 선택 가능, 서버에서 다시 검증).
//   - value: 자유 입력 (문자열). 비어 있으면 null 로 전송.
//   - 쿼리스트링 prefill 지원 (line, equip, command_type, value, idempotency_key, source_type, created_by).
//   - autoSubmit=1 이면 OnGet 에서 바로 제출 처리.
//
// 핸들러는 서버에서 CommandService.CreateAsync 직접 호출 → 결과/에러를 페이지에 다시 표시.

using System.Text.Json;
using BeApi.Features.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Command;

public class RequestModel : PageModel
{
    private readonly CommandService _commandService;

    public RequestModel(CommandService commandService)
    {
        _commandService = commandService;
    }

    // === 화면 모델 ===
    public IReadOnlyList<EquipmentCatalogItem> Equipments => EquipmentCatalog.Items;

    /// <summary>JS 로 동적 필터링하기 위해 prefix → command_type 목록 전부 직렬화해 전달.</summary>
    public string CommandTypesByPrefixJson { get; private set; } = "{}";

    // === Form 바인딩 ===
    [BindProperty] public int LineId { get; set; } = 1;
    [BindProperty] public string EquipmentCode { get; set; } = "CAST-01";
    [BindProperty] public string CommandType { get; set; } = "power";
    [BindProperty] public string? ValueText { get; set; }
    [BindProperty] public int? Priority { get; set; }
    [BindProperty] public int? MaxRetry { get; set; }
    [BindProperty] public string? SourceType { get; set; }
    [BindProperty] public string? CreatedBy { get; set; }
    [BindProperty] public string? IdempotencyKey { get; set; }

    // === 결과 표시 ===
    public CommandResponseDto? Result { get; private set; }
    public string? Outcome { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "line")] int? line,
        [FromQuery(Name = "equip")] string? equip,
        [FromQuery(Name = "command_type")] string? commandType,
        [FromQuery(Name = "value")] string? value,
        [FromQuery(Name = "priority")] int? priority,
        [FromQuery(Name = "max_retry")] int? maxRetry,
        [FromQuery(Name = "source_type")] string? sourceType,
        [FromQuery(Name = "created_by")] string? createdBy,
        [FromQuery(Name = "idempotency_key")] string? idempotencyKey,
        [FromQuery(Name = "autoSubmit")] int? autoSubmit,
        CancellationToken ct)
    {
        BuildCommandTypeJson();

        if (line.HasValue) LineId = line.Value;
        if (!string.IsNullOrWhiteSpace(equip)) EquipmentCode = equip!;
        if (!string.IsNullOrWhiteSpace(commandType)) CommandType = commandType!;
        if (value != null) ValueText = value;
        Priority = priority;
        MaxRetry = maxRetry;
        if (!string.IsNullOrWhiteSpace(sourceType)) SourceType = sourceType;
        if (!string.IsNullOrWhiteSpace(createdBy)) CreatedBy = createdBy;
        if (!string.IsNullOrWhiteSpace(idempotencyKey)) IdempotencyKey = idempotencyKey;

        if (autoSubmit == 1)
        {
            await ExecuteAsync(ct).ConfigureAwait(false);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        BuildCommandTypeJson();
        await ExecuteAsync(ct).ConfigureAwait(false);
        return Page();
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
        // value 자유 입력 → JsonElement?
        JsonElement? valueElement = null;
        if (!string.IsNullOrWhiteSpace(ValueText))
        {
            valueElement = TryParseValue(ValueText!);
        }

        var req = new CreateCommandRequest
        {
            LineId = LineId,
            EquipmentId = EquipmentCode,
            CommandType = CommandType,
            Value = valueElement,
            Priority = Priority,
            MaxRetry = MaxRetry,
            SourceType = string.IsNullOrWhiteSpace(SourceType) ? null : SourceType,
            CreatedBy = string.IsNullOrWhiteSpace(CreatedBy) ? null : CreatedBy,
            IdempotencyKey = string.IsNullOrWhiteSpace(IdempotencyKey) ? null : IdempotencyKey,
        };

        var result = await _commandService.CreateAsync(req, headerIdempotencyKey: null, ct).ConfigureAwait(false);
        Outcome = result.Outcome.ToString();
        Result = result.Dto;
        ErrorCode = result.ErrorCode;
        ErrorMessage = result.ErrorMessage;
    }

    /// <summary>
    /// value 자유 입력 파싱: 우선 JSON 으로 시도, 실패하면 문자열로 감싸서 전달.
    /// 빈 따옴표 "" 같은 것도 JSON 으로 통과되므로 그대로 둠.
    /// </summary>
    private static JsonElement? TryParseValue(string text)
    {
        text = text.Trim();
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // primitive string 으로 직렬화
            var json = JsonSerializer.Serialize(text);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }

    private void BuildCommandTypeJson()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var prefix in new[] { "CAST", "CNC", "WASH", "ASSY", "TEST" })
        {
            map[prefix] = CommandTypeCatalog.AllowedFor(prefix).ToArray();
        }
        CommandTypesByPrefixJson = JsonSerializer.Serialize(map);
    }
}
