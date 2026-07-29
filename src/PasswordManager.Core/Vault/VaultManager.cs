using System.Text;
using PasswordManager.Core.Models;
using PasswordManager.Core.Security;

namespace PasswordManager.Core.Vault;

/// <summary>
/// 볼트 세션 오케스트레이션 (design 5, TD-017). 언락 시 DEK를 세션 동안 보유하고,
/// 복호화된 <see cref="VaultData"/>에 대한 CRUD를 수행하며 변경 시마다 본문만 재암호화해 저장한다.
/// 파일 I/O는 <see cref="IVaultFileStore"/> 구현에 위임한다(메모리 위생 강화는 M5).
/// </summary>
public sealed class VaultManager
{
    private readonly IVaultFileStore _store;
    private readonly string _path;

    private EncryptedVault? _current; // 현재 암호화 볼트(헤더+본문)
    private byte[]? _dek;             // 세션 DEK
    private VaultData? _data;         // 복호화된 본문

    public VaultManager(IVaultFileStore store, string path)
    {
        _store = store;
        _path = path;
    }

    /// <summary>세션이 열려 있는가(마스터 비번으로 언락된 상태).</summary>
    public bool IsUnlocked => _data is not null;

    /// <summary>파일이 이미 존재하는가(첫 실행 여부 판단).</summary>
    public bool Exists() => _store.Exists(_path);

    /// <summary>현재 볼트의 항목들(읽기 전용 스냅샷 뷰).</summary>
    public IReadOnlyList<VaultEntry> Entries => RequireUnlocked().Entries;

    /// <summary>새 볼트를 만들어 저장하고 세션을 연다. 최초 1회 보여줄 복구 키를 반환한다(TD-010).</summary>
    public byte[] CreateNew(string masterPassword, KdfParams kdf)
    {
        var data = new VaultData();
        var content = Serialize(data);
        var result = VaultService.Create(masterPassword, content, kdf);

        _current = result.Vault;
        _dek = VaultService.Unlock(result.Vault, masterPassword).Dek;
        _data = data;
        _store.Save(_path, _current);

        return result.RecoveryKey;
    }

    /// <summary>기존 볼트를 마스터 비번으로 열어 세션을 시작한다.</summary>
    public void Open(string masterPassword)
    {
        var vault = _store.Load(_path);
        var session = VaultService.Unlock(vault, masterPassword);

        _current = vault;
        _dek = session.Dek;
        _data = Deserialize(session.Content);
    }

    /// <summary>
    /// 복구 키(문자열)로 마스터 비밀번호를 재설정하고 새 비번으로 세션을 연다(design 5.7).
    /// 복구 키가 형식에 맞지 않거나 틀리면 예외를 던진다.
    /// </summary>
    public void Recover(string recoveryCode, string newMasterPassword, KdfParams kdf)
    {
        var vault = _store.Load(_path);
        var recoveryKey = RecoveryCode.Decode(recoveryCode);
        var reset = VaultService.ResetMasterPasswordWithRecovery(vault, recoveryKey, newMasterPassword, kdf);
        _store.Save(_path, reset);

        var session = VaultService.Unlock(reset, newMasterPassword);
        _current = reset;
        _dek = session.Dek;
        _data = Deserialize(session.Content);
    }

    /// <summary>
    /// 마스터 비밀번호를 변경한다(rekey, design 7.5/TD-006). 현재 비번으로 확인 후 헤더의
    /// 마스터 래핑만 새 비번으로 교체한다. DEK·본문·복구 래핑은 그대로라 세션은 유지된다.
    /// </summary>
    public void ChangeMasterPassword(string currentMasterPassword, string newMasterPassword, KdfParams kdf)
    {
        RequireUnlocked();
        _current = VaultService.ChangeMasterPassword(_current!, currentMasterPassword, newMasterPassword, kdf);
        _store.Save(_path, _current);
    }

    /// <summary>앱 잠금해제 OTP가 등록되어 있는가(열람 게이트 사용 여부). design 5.4·TD-004.</summary>
    public bool HasOtp => RequireUnlocked().AppTotpSecret is not null;

    /// <summary>
    /// 앱 잠금해제 OTP secret을 등록(또는 재설정)한다. 등록 화면이 메모리에서 secret을 만들어
    /// 폰 등록을 확인한 뒤 넘겨주면 본문에 저장한다(persist-on-confirm, TD-005 재설정 포함).
    /// secret 생성은 <see cref="TotpValidator.GenerateSecret"/>가 담당한다.
    /// </summary>
    public void SetOtpSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("OTP secret이 비어 있습니다.", nameof(secret));

        var data = RequireUnlocked();
        data.AppTotpSecret = secret;
        Persist();
    }

    /// <summary>입력한 OTP 코드를 검증한다(현재 시각 기준). 미등록이면 예외를 던진다.</summary>
    public bool VerifyOtp(string code) => VerifyOtp(code, DateTimeOffset.UtcNow);

    /// <summary>입력한 OTP 코드를 지정 시각 기준으로 검증한다(테스트에서 시각 고정용).</summary>
    public bool VerifyOtp(string code, DateTimeOffset now)
    {
        var data = RequireUnlocked();
        if (data.AppTotpSecret is null)
            throw new InvalidOperationException("OTP가 등록되어 있지 않습니다. 먼저 SetupOtp로 등록하세요.");
        return TotpValidator.Verify(data.AppTotpSecret, code, now);
    }

    /// <summary>세션을 닫고 메모리의 DEK·데이터를 버린다.</summary>
    public void Lock()
    {
        _dek = null;
        _data = null;
        _current = null;
    }

    /// <summary>id로 항목을 찾는다(없으면 null).</summary>
    public VaultEntry? Get(string id) => RequireUnlocked().Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>새 항목을 추가한다. 생성/수정/마지막변경 시각을 찍고 저장한다.</summary>
    public void Add(VaultEntry entry)
    {
        var data = RequireUnlocked();
        var now = DateTimeOffset.UtcNow;
        entry.CreatedAt = now;
        entry.UpdatedAt = now;
        entry.LastChangedAt = now;
        data.Entries.Add(entry);
        Persist();
    }

    /// <summary>항목을 수정한다. 같은 id의 기존 항목을 교체하고 수정 시각을 갱신한 뒤 저장한다.</summary>
    public void Update(VaultEntry entry)
    {
        var data = RequireUnlocked();
        var index = data.Entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0)
            throw new InvalidOperationException($"수정할 항목을 찾을 수 없습니다: {entry.Id}");

        entry.UpdatedAt = DateTimeOffset.UtcNow;
        data.Entries[index] = entry;
        Persist();
    }

    /// <summary>id로 항목을 삭제하고 저장한다.</summary>
    public void Remove(string id)
    {
        var data = RequireUnlocked();
        data.Entries.RemoveAll(e => e.Id == id);
        Persist();
    }

    /// <summary>현재 데이터를 본문만 재암호화해 저장한다(헤더·복구 래핑은 유지, TD-017).</summary>
    private void Persist()
    {
        _current = VaultService.SealBody(_current!, _dek!, Serialize(_data!));
        _store.Save(_path, _current);
    }

    private VaultData RequireUnlocked() =>
        _data ?? throw new InvalidOperationException("볼트가 잠겨 있습니다. 먼저 Open/CreateNew로 여세요.");

    private static byte[] Serialize(VaultData data) => Encoding.UTF8.GetBytes(VaultJson.Serialize(data));
    private static VaultData Deserialize(byte[] content) => VaultJson.Deserialize(Encoding.UTF8.GetString(content));
}
