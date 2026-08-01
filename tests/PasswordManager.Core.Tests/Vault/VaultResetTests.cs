using PasswordManager.Core.Vault;

namespace PasswordManager.Core.Tests.Vault;

/// <summary>전체 초기화가 지울 대상 규칙 (TD-044).</summary>
public class VaultResetTests
{
    private const string VaultPath = @"C:\data\PasswordManager\vault.dat";

    [Fact]
    public void Covers_the_vault_and_every_sidecar_the_app_creates()
    {
        var paths = VaultReset.PathsFor(VaultPath);

        Assert.Equal(new[]
        {
            @"C:\data\PasswordManager\vault.dat",
            @"C:\data\PasswordManager\vault.dat.tmp",      // 원자적 쓰기 중 남은 임시 파일
            @"C:\data\PasswordManager\vault.dat.bak",      // 자동 백업
            @"C:\data\PasswordManager\vault.dat.lockout",  // 로그인 재시도 잠금 상태
            @"C:\data\PasswordManager\slack-failures.log", // 슬랙 전송 실패 로그
        }, paths);
    }

    [Fact]
    public void Sidecars_live_next_to_the_vault_whatever_its_folder_is()
    {
        var paths = VaultReset.PathsFor(@"D:\other\my.vault");

        Assert.Contains(@"D:\other\my.vault.bak", paths);
        Assert.Contains(@"D:\other\slack-failures.log", paths);
    }

    [Fact]
    public void Backup_file_is_included_because_it_would_be_unopenable_anyway()
    {
        // 초기화의 목적은 첫 설치 상태로 되돌리는 것 — 백업만 남기면 열 수 없는 파일이 남을 뿐이다.
        Assert.Contains(VaultPath + ".bak", VaultReset.PathsFor(VaultPath));
    }

    [Fact]
    public void Rejects_an_empty_path()
    {
        Assert.Throws<ArgumentException>(() => VaultReset.PathsFor("  "));
    }
}
