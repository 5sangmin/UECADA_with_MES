// Controllers/PingController.cs
using BeApi.Shared.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PingController : ControllerBase
{
    private readonly UdpSettings _udpSettings;

    public PingController(IOptions<UdpSettings> udpOptions)
    {
        _udpSettings = udpOptions.Value;  // .Value로 실제 설정값 꺼냄
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            timestamp = DateTimeOffset.UtcNow,
            service = "be-api",
            udpMulticastGroup = _udpSettings.MulticastGroup,
            udpListenPort = _udpSettings.ListenPort
        });
    }
}