// src/Shared/Extensions/EnvFileLoader.cs
//
// PR8(Step14): .env 파일 자동 로딩 헬퍼.
//
// 설계:
//   - DotNetEnv 패키지 (NuGet) 를 얇게 감싸 BeApi 프로젝트 루트의 .env 를 자동 로드.
//   - 파일이 없으면 조용히 무시 (silent skip) — Production 환경에서는 OS 시스템 환경변수만
//     설정되어 있을 수 있으므로 .env 부재가 정상.
//   - 이미 OS 환경변수에 같은 키가 있으면 덮어쓰지 않음 (overwrite: false).
//     → OS ENV > .env > appsettings 순의 우선순위 유지.
//   - Program.cs 가장 앞에서 호출되어, WebApplication.CreateBuilder 가 환경변수를
//     Configuration 으로 흡수하기 전에 .env 값을 환경변수에 주입.
//
// 검색 경로:
//   1) Directory.GetCurrentDirectory() (= 일반적으로 dotnet run 실행 디렉터리)
//   2) AppContext.BaseDirectory (= 빌드 출력 디렉터리 — bin/Debug/net9.0 등)
//   3) 두 경로의 상위 1단계 (예: BeApi/bin/Debug/net9.0 → BeApi/)
//
// 첫 번째로 찾은 .env 만 로드한다. 우선순위는 위 순서.

namespace BeApi.Shared.Extensions;

public static class EnvFileLoader
{
    /// <summary>
    /// .env 파일을 검색하여 로드한다. 파일이 없으면 아무 일도 하지 않는다.
    /// </summary>
    /// <returns>로드한 .env 의 절대 경로. 못 찾으면 null.</returns>
    public static string? LoadIfPresent()
    {
        foreach (var candidate in EnumerateCandidatePaths())
        {
            if (File.Exists(candidate))
            {
                // clobberExistingVars: false → OS 환경변수가 이미 설정되어 있으면 덮어쓰지 않음.
                //   → OS ENV > .env 우선순위 유지.
                // onlyExactPath: true → 부모 디렉터리 자동 탐색 비활성 (우리가 명시적으로 경로 원함).
                // LoadOptions 는 DotNetEnv 네임스페이스의 최상위 타입 (Env 의 nested 타입이 아님).
                // clobberExistingVars: false → 이미 OS 환경변수가 설정되어 있으면 덮어쓰지 않음 (OS ENV 우선).
                // onlyExactPath: true → 부모 디렉터리 자동 탐색 비활성 (우리가 명시적으로 경로 관리).
                DotNetEnv.Env.Load(candidate, options: new DotNetEnv.LoadOptions(
                    setEnvVars: true,
                    clobberExistingVars: false,
                    onlyExactPath: true));
                return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in EnumerateCandidateDirectories())
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var path = Path.GetFullPath(Path.Combine(dir, ".env"));
            if (seen.Add(path))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        // 1) 현재 작업 디렉터리 — dotnet run 실행 위치
        yield return Directory.GetCurrentDirectory();

        // 2) 빌드 출력 디렉터리
        yield return AppContext.BaseDirectory;

        // 3) 위 두 디렉터리의 상위 (BeApi/bin/Debug/net9.0 같은 경우 BeApi/ 로 한 단계 위)
        var cwdParent = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
        if (cwdParent != null) yield return cwdParent;

        var baseParent = Directory.GetParent(AppContext.BaseDirectory)?.FullName;
        if (baseParent != null) yield return baseParent;

        // 4) 빌드 출력 디렉터리 상위 3단계까지 (bin/Debug/net9.0 → 프로젝트 루트)
        var baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 3; i++)
        {
            var parent = Directory.GetParent(baseDir);
            if (parent == null) break;
            baseDir = parent.FullName;
            yield return baseDir;
        }
    }
}
