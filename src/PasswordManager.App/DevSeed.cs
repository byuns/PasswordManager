#if DEBUG
using System.Linq;
using PasswordManager.Core.Models;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.App;

/// <summary>
/// 개발/테스트 전용 더미 시드. 앱을 <c>PWM_VAULT_PATH</c>(임시 경로) + <c>PWM_SEED=1</c>로 띄울 때만
/// 실행돼, 태그가 많은 볼트를 만들어 UI(예: 태그 칩 바 가로 슬라이딩)를 확인한다. 실제 볼트엔 영향 없음.
/// <para>
/// <b>Debug 빌드에만 포함된다</b>(TD-045). 게시(Release)본에는 아예 컴파일되지 않아, 배포된 exe에
/// 개발용 경로와 하드코딩된 시드 비밀번호가 남지 않는다.
/// </para>
/// </summary>
internal static class DevSeed
{
    /// <summary>시드 볼트의 마스터 비밀번호(테스트 전용).</summary>
    public const string Password = "seedtestvault-2026";

    public static void Populate(VaultManager manager)
    {
        manager.CreateNew(Password, new KdfParams(MemoryKiB: 8192, Iterations: 2, Parallelism: 1));

        // 한 사이트에 계정 여러 개가 섞이도록 구성(그룹핑 확인용) + 태그를 넉넉히(가로 슬라이딩 확인용).
        var data = new (string Title, string Login, string[] Tags)[]
        {
            ("GitHub",  "personal@me.com", new[] { "개발", "개인" }),
            ("GitHub",  "work@corp.com",   new[] { "개발", "업무" }),
            ("Google",  "me@gmail.com",    new[] { "메일", "개인" }),
            ("Google",  "backup@gmail.com", new[] { "메일", "백업" }),
            ("Steam",   "gamer",           new[] { "게임" }),
            ("Steam",   "gamer_alt",       new[] { "게임", "부계" }),
            ("AWS",     "root",            new[] { "인프라", "클라우드" }),
            ("AWS",     "deploy",          new[] { "인프라", "개발" }),
            ("Notion",  "team",            new[] { "업무", "문서" }),
            ("Notion",  "personal",        new[] { "문서", "개인" }),
            ("Naver",   "me@naver.com",    new[] { "포털", "메일" }),
            ("Figma",   "design",          new[] { "업무", "디자인" }),
            ("Epic",    "gamer2",          new[] { "게임", "런처" }),
            ("Toss",    "pay",             new[] { "금융" }),
            ("KB",      "bank",            new[] { "금융", "은행" }),
            ("Netflix", "watch",           new[] { "미디어", "구독" }),
            ("Youtube", "tube",            new[] { "미디어", "구독" }),
        };

        foreach (var (title, login, tags) in data)
            manager.Add(new VaultEntry
            {
                Title = title,
                Login = login,
                Password = "pw-" + login,
                Tags = tags.ToList(),
                Notes = $"{title} {login} 계정 메모 — 복구 이메일/보안질문 등 (hover 미리보기 테스트)",
            });
    }
}
#endif
