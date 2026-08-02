using System.Security.Cryptography;
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
    private readonly KdfParams _kdfFloor; // 로그인 시 자동 상향 기준(design 7.5)

    private EncryptedVault? _current; // 현재 암호화 볼트(헤더+본문)
    private byte[]? _dek;             // 세션 DEK
    private VaultData? _data;         // 복호화된 본문

    public VaultManager(IVaultFileStore store, string path, KdfParams? kdfFloor = null)
    {
        _store = store;
        _path = path;
        _kdfFloor = kdfFloor ?? KdfParams.Recommended;
    }

    /// <summary>세션이 열려 있는가(마스터 비번으로 언락된 상태).</summary>
    public bool IsUnlocked => _data is not null;

    /// <summary>파일이 이미 존재하는가(첫 실행 여부 판단).</summary>
    public bool Exists() => _store.Exists(_path);

    /// <summary>현재 볼트의 활성 항목들(휴지통에 든 것은 제외, TD-041).</summary>
    public IReadOnlyList<VaultEntry> Entries =>
        RequireUnlocked().Entries.Where(e => e.DeletedAt is null).ToList();

    /// <summary>휴지통에 든 항목들(최근 삭제 순, TD-041).</summary>
    public IReadOnlyList<VaultEntry> DeletedEntries =>
        RequireUnlocked().Entries
            .Where(e => e.DeletedAt is not null)
            .OrderByDescending(e => e.DeletedAt!.Value)
            .ToList();

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

        _dek = session.Dek;
        _data = Deserialize(session.Content);
        MemoryHygiene.Clear(session.Content); // 복호화된 평문 버퍼 소거(design 5.5)

        // 저장된 KDF가 현재 기준보다 약하면 자동으로 상향해 재저장한다(design 7.5).
        if (vault.Header.Kdf.NeedsUpgradeTo(_kdfFloor))
        {
            vault = VaultService.UpgradeKdf(
                vault, masterPassword, _dek, vault.Header.Kdf.RaisedTo(_kdfFloor));
            _store.Save(_path, vault);
        }

        _current = vault;

        // 보관 기간을 넘긴 휴지통 항목을 이 시점에 정리한다(TD-041).
        PurgeExpiredTrash(DateTimeOffset.UtcNow);
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
        MemoryHygiene.Clear(recoveryKey); // 복구 키 바이트 소거(design 5.5)
        _store.Save(_path, reset);

        var session = VaultService.Unlock(reset, newMasterPassword);
        _current = reset;
        _dek = session.Dek;
        _data = Deserialize(session.Content);
        MemoryHygiene.Clear(session.Content);
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

    /// <summary>
    /// 복구 키를 재발급한다(design 7.6). 현재 마스터 비번을 확인해 헤더의 복구 래핑만 새 복구 키로
    /// 교체·저장한다. 세션·마스터 래핑·본문은 유지되며 이전 복구 키는 무효화된다. 사용자에게 1회 보여줄
    /// 새 복구 키 문자열을 반환한다. 현재 비번이 틀리면 <see cref="InvalidMasterPasswordException"/>.
    /// </summary>
    public string ReissueRecoveryKey(string currentMasterPassword)
    {
        RequireUnlocked();
        var result = VaultService.ReissueRecoveryKey(_current!, currentMasterPassword);
        _current = result.Vault;
        _store.Save(_path, _current);

        var code = RecoveryCode.Encode(result.RecoveryKey);
        MemoryHygiene.Clear(result.RecoveryKey); // 원본 복구 키 바이트 소거(design 5.5)
        return code;
    }

    /// <summary>앱 잠금해제 OTP가 등록되어 있는가(열람 게이트 사용 여부). design 5.4·TD-004.</summary>
    public bool HasOtp => RequireUnlocked().AppTotpSecret is not null;

    /// <summary>
    /// 앱 잠금해제 OTP secret을 등록(또는 재설정)한다. 등록 화면이 메모리에서 secret을 만들어
    /// 디바이스 등록을 확인한 뒤 넘겨주면 본문에 저장한다(persist-on-confirm, TD-005 재설정 포함).
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

    /// <summary>
    /// 현재 마스터 비밀번호가 맞는지 상태 변경 없이 확인한다(민감 작업 재확인용, TD-005).
    /// 세션·DEK를 바꾸지 않으며, 틀리면 false를 돌려준다.
    /// </summary>
    public bool VerifyMasterPassword(string masterPassword)
    {
        var vault = _current ?? _store.Load(_path);
        try
        {
            VaultService.Unlock(vault, masterPassword);
            return true;
        }
        catch (InvalidMasterPasswordException)
        {
            return false;
        }
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

    /// <summary>세션을 닫고 메모리의 DEK·데이터를 버린다. DEK 바이트는 0으로 소거한다(design 5.5).</summary>
    public void Lock()
    {
        MemoryHygiene.Clear(_dek);
        _dek = null;
        _data = null;
        _current = null;
    }

    /// <summary>id로 활성 항목을 찾는다(없거나 휴지통에 있으면 null).</summary>
    public VaultEntry? Get(string id) =>
        RequireUnlocked().Entries.FirstOrDefault(e => e.Id == id && e.DeletedAt is null);

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

    /// <summary>항목당 보관하는 비밀번호 이력 최대 개수(TD-021). 초과 시 오래된 것부터 제거.</summary>
    public const int PasswordHistoryLimit = 5;

    /// <summary>항목을 수정한다(현재 시각 기준).</summary>
    public void Update(VaultEntry entry) => Update(entry, DateTimeOffset.UtcNow);

    /// <summary>
    /// 항목을 수정한다. 비밀번호가 바뀐 경우에만 이전 비번을 이력에 적재하고(상한 <see cref="PasswordHistoryLimit"/>,
    /// 오래된 것부터 제거) <c>LastChangedAt</c>을 갱신한다. <c>CreatedAt</c>·<c>PasswordHistory</c>는 기존 항목
    /// 값을 기준으로 관리하므로 입력값을 신뢰하지 않는다(TD-021). now는 테스트에서 시각 고정용.
    /// </summary>
    public void Update(VaultEntry entry, DateTimeOffset now)
    {
        var data = RequireUnlocked();
        var index = data.Entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0)
            throw new InvalidOperationException($"수정할 항목을 찾을 수 없습니다: {entry.Id}");

        var existing = data.Entries[index];

        // 생성시각과 이력은 앱이 권한을 갖는 필드 — 기존 항목 값을 이어받는다.
        entry.CreatedAt = existing.CreatedAt;
        entry.PasswordHistory = existing.PasswordHistory;

        // 즐겨찾기·최근 사용·휴지통 상태도 편집 폼이 다루지 않는 앱 소유 필드다.
        // 편집 화면은 폼 값으로 새 VaultEntry를 만들어 넘기므로, 이어받지 않으면 조용히 초기화된다.
        entry.IsPinned = existing.IsPinned;
        entry.LastUsedAt = existing.LastUsedAt;
        entry.DeletedAt = existing.DeletedAt;

        if (entry.Password != existing.Password)
        {
            // 이전 비번을 최신이 앞에 오도록 적재하고 상한을 넘으면 오래된 것부터 제거.
            entry.PasswordHistory.Insert(0, new PasswordHistoryItem
            {
                Password = existing.Password,
                ChangedAt = existing.LastChangedAt,
            });
            if (entry.PasswordHistory.Count > PasswordHistoryLimit)
                entry.PasswordHistory.RemoveRange(
                    PasswordHistoryLimit, entry.PasswordHistory.Count - PasswordHistoryLimit);
            entry.LastChangedAt = now;
        }
        else
        {
            entry.LastChangedAt = existing.LastChangedAt;
        }

        entry.UpdatedAt = now;
        data.Entries[index] = entry;
        Persist();
    }

    /// <summary>현재 볼트를 백업 경로로 복사한다(암호화 상태 그대로, 언락 불필요). M6.</summary>
    public void Backup(string backupPath) => VaultBackup.Backup(_store, _path, backupPath);

    /// <summary>백업 파일로 현재 볼트를 복원하고 세션을 닫는다. 이후 백업의 마스터 비번으로 다시 열어야 한다. M6.</summary>
    public void Restore(string backupPath)
    {
        VaultBackup.Restore(_store, backupPath, _path);
        Lock();
    }

    /// <summary>유휴 자동 잠금까지의 분(design 5.5).</summary>
    public int AutoLockMinutes => RequireUnlocked().AutoLockMinutes;

    /// <summary>비밀번호 복사 후 클립보드 자동 삭제까지의 초(design 5.5).</summary>
    public int ClipboardClearSeconds => RequireUnlocked().ClipboardClearSeconds;

    /// <summary>자동 잠금·클립보드 삭제 시간을 저장한다(양수만 허용). design 5.5·7.9.</summary>
    public void SetTimeSettings(int autoLockMinutes, int clipboardClearSeconds)
    {
        if (autoLockMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(autoLockMinutes), "자동 잠금 시간은 1분 이상이어야 합니다.");
        if (clipboardClearSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(clipboardClearSeconds), "클립보드 삭제 시간은 1초 이상이어야 합니다.");

        var data = RequireUnlocked();
        data.AutoLockMinutes = autoLockMinutes;
        data.ClipboardClearSeconds = clipboardClearSeconds;
        Persist();
    }

    /// <summary>전역 오프라인 스위치 상태(기본 false=차단, TD-013).</summary>
    public bool NetworkAllowed => RequireUnlocked().NetworkAllowed;

    /// <summary>슬랙 알림 설정 스냅샷(design 7.8·TD-012). 변경은 <see cref="SaveNetworkSettings"/>로 저장한다.</summary>
    public SlackSettings Slack => RequireUnlocked().Slack;

    /// <summary>전역 오프라인 스위치와 슬랙 설정을 저장한다(design 7.8·7.9). null slack이면 기존 설정 유지.</summary>
    public void SaveNetworkSettings(bool networkAllowed, SlackSettings? slack = null)
    {
        var data = RequireUnlocked();
        data.NetworkAllowed = networkAllowed;
        if (slack is not null) data.Slack = slack;
        Persist();
    }

    /// <summary>활성 항목들을 자체 CSV(평문)로 내보낸다(휴지통 제외). 호출부가 강한 경고를 거쳐야 한다(design 7.7).</summary>
    public string ExportCsv() => CsvVault.Export(Entries);

    /// <summary>
    /// 입력한 복구 키가 이 볼트의 것인지 상태 변경 없이 확인한다(TD-050). 형식이 깨졌거나
    /// 다른 볼트의 키면 false. 헤더의 복구 래핑을 풀어보는 것으로 판정한다.
    /// </summary>
    public bool VerifyRecoveryKey(string recoveryCode)
    {
        var vault = _current ?? _store.Load(_path);
        byte[] key;
        try
        {
            key = RecoveryCode.Decode(recoveryCode);
        }
        catch (FormatException)
        {
            return false; // 복구 키 알파벳에 없는 문자
        }

        try
        {
            KeyWrap.Unwrap(key, vault.Header.DekByRecovery);
            return true;
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            return false; // 키가 틀렸거나 길이가 안 맞음
        }
        finally
        {
            MemoryHygiene.Clear(key);
        }
    }

    /// <summary>
    /// 활성 항목을 복구 키로 잠근 파일로 내보낸다(TD-050). 봉인 전에 이 볼트의 복구 키가 맞는지
    /// 확인한다 — 틀린 키로 잠그면 그 파일은 영영 열 수 없다. 아니면 <see cref="InvalidRecoveryKeyException"/>.
    /// </summary>
    public byte[] ExportEncrypted(string recoveryCode)
    {
        RequireUnlocked();
        if (!VerifyRecoveryKey(recoveryCode))
            throw new InvalidRecoveryKeyException();

        var key = RecoveryCode.Decode(recoveryCode);
        try
        {
            return EncryptedExport.Protect(key, ExportCsv());
        }
        finally
        {
            MemoryHygiene.Clear(key);
        }
    }

    /// <summary>
    /// 잠긴 내보내기 파일을 복구 키로 풀어 항목을 가져오고(모두 새 항목으로 추가) 개수를 돌려준다.
    /// 판정 기준은 <b>그 파일을 봉인한 복구 키</b>다 — 현재 볼트의 복구 키와 대조하지 않으므로,
    /// 다른 볼트에서 내보낸 파일도 그때의 복구 키만 있으면 가져올 수 있다(TD-050).
    /// </summary>
    public int ImportEncrypted(byte[] file, string recoveryCode)
    {
        RequireUnlocked();

        byte[] key;
        try
        {
            key = RecoveryCode.Decode(recoveryCode);
        }
        catch (FormatException)
        {
            throw new InvalidRecoveryKeyException();
        }

        string csv;
        try
        {
            csv = EncryptedExport.Unprotect(key, file);
        }
        catch (ArgumentException)
        {
            throw new InvalidRecoveryKeyException(); // 키 길이가 안 맞음
        }
        finally
        {
            MemoryHygiene.Clear(key);
        }

        return ImportCsv(csv);
    }

    /// <summary>
    /// CSV에서 항목을 읽어 모두 새 항목으로 추가하고 저장한다(중복 검사 없이 append). 추가한 개수를 돌려준다.
    /// id·시각은 앱이 새로 부여한다(입력값을 신뢰하지 않음). design 7.7.
    /// </summary>
    public int ImportCsv(string csv)
    {
        var data = RequireUnlocked();
        var imported = CsvVault.Parse(csv);
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in imported)
        {
            entry.Id = Guid.NewGuid().ToString();
            entry.CreatedAt = now;
            entry.UpdatedAt = now;
            entry.LastChangedAt = now;
            data.Entries.Add(entry);
        }
        if (imported.Count > 0) Persist();
        return imported.Count;
    }

    /// <summary>휴지통 보관 기간(일). 이 기간을 넘긴 항목은 언락 시 자동으로 영구 삭제된다(TD-041).</summary>
    public const int TrashRetentionDays = 30;

    /// <summary>id로 항목을 휴지통에 넣고 저장한다(소프트 삭제, TD-041).</summary>
    public void Remove(string id) => Remove(id, DateTimeOffset.UtcNow);

    /// <summary>id로 항목을 휴지통에 넣는다. now는 테스트에서 시각 고정용.</summary>
    public void Remove(string id, DateTimeOffset now)
    {
        var data = RequireUnlocked();
        var entry = data.Entries.FirstOrDefault(e => e.Id == id && e.DeletedAt is null);
        if (entry is null) return;

        entry.DeletedAt = now;
        entry.IsPinned = false; // 휴지통 항목이 즐겨찾기에 남아 있으면 안 된다
        Persist();
    }

    /// <summary>휴지통의 항목을 되살린다(TD-041). 없으면 아무것도 하지 않는다.</summary>
    public void RestoreEntry(string id)
    {
        var data = RequireUnlocked();
        var entry = data.Entries.FirstOrDefault(e => e.Id == id && e.DeletedAt is not null);
        if (entry is null) return;

        entry.DeletedAt = null;
        Persist();
    }

    /// <summary>휴지통의 항목을 영구 삭제한다(TD-041). 활성 항목은 건드리지 않는다.</summary>
    public void PurgeEntry(string id)
    {
        var data = RequireUnlocked();
        if (data.Entries.RemoveAll(e => e.Id == id && e.DeletedAt is not null) > 0)
            Persist();
    }

    /// <summary>휴지통을 비운다(영구 삭제). 활성 항목은 그대로 둔다(TD-041).</summary>
    public void EmptyTrash()
    {
        var data = RequireUnlocked();
        if (data.Entries.RemoveAll(e => e.DeletedAt is not null) > 0)
            Persist();
    }

    /// <summary>
    /// 보관 기간(<see cref="TrashRetentionDays"/>)을 넘긴 휴지통 항목을 영구 삭제하고 개수를 돌려준다.
    /// 언락 시 자동 호출된다. 경계(정확히 30일째)는 아직 남긴다 — 초과분만 지운다.
    /// </summary>
    public int PurgeExpiredTrash(DateTimeOffset now)
    {
        var data = RequireUnlocked();
        var cutoff = now.AddDays(-TrashRetentionDays);
        var purged = data.Entries.RemoveAll(e => e.DeletedAt is { } deleted && deleted < cutoff);
        if (purged > 0) Persist();
        return purged;
    }

    /// <summary>항목의 즐겨찾기 고정을 켜거나 끈다(TD-040). 비밀 노출이 아니라 OTP 게이트 없이 호출된다.</summary>
    public void SetPinned(string id, bool pinned)
    {
        var data = RequireUnlocked();
        var entry = data.Entries.FirstOrDefault(e => e.Id == id && e.DeletedAt is null);
        if (entry is null || entry.IsPinned == pinned) return;

        entry.IsPinned = pinned;
        Persist();
    }

    /// <summary>항목을 "지금 썼다"고 기록한다(OTP 인증 통과 시점, TD-040). "최근 사용순" 정렬의 기준.</summary>
    public void MarkUsed(string id, DateTimeOffset now)
    {
        var data = RequireUnlocked();
        var entry = data.Entries.FirstOrDefault(e => e.Id == id && e.DeletedAt is null);
        if (entry is null) return;

        entry.LastUsedAt = now;
        Persist();
    }

    /// <summary>계정 목록 정렬 기준(기본 이름순, TD-040).</summary>
    public EntrySortOrder SortOrder => RequireUnlocked().SortOrder;

    /// <summary>정렬 기준을 저장한다 — 다음 실행에도 유지된다(TD-040).</summary>
    public void SetSortOrder(EntrySortOrder order)
    {
        var data = RequireUnlocked();
        if (data.SortOrder == order) return;

        data.SortOrder = order;
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
