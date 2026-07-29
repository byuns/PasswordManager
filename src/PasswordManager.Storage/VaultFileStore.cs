using System.Text;
using PasswordManager.Core.Security;
using PasswordManager.Core.Vault;

namespace PasswordManager.Storage;

/// <summary>볼트 파일이 포맷에 맞지 않거나 지원하지 않는 버전일 때.</summary>
public sealed class InvalidVaultFileException(string message) : Exception(message);

/// <summary>
/// <see cref="EncryptedVault"/> 를 vault.dat 바이너리 포맷으로 직렬화/역직렬화하고,
/// 원자적 쓰기(tmp→bak→rename)로 파일에 저장한다 (design 5.3, TD-001).
/// </summary>
public static class VaultFileStore
{
    /// <summary>파일 식별용 매직 바이트.</summary>
    public const string Magic = "PWMV";

    /// <summary>현재 볼트 파일 포맷 버전.</summary>
    public const byte CurrentVersion = 0x01;

    /// <summary>볼트를 바이너리로 직렬화한다.</summary>
    public static byte[] Serialize(EncryptedVault vault)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        w.Write(Encoding.ASCII.GetBytes(Magic)); // 4B
        w.Write(CurrentVersion);                 // 1B

        w.Write(vault.Header.Kdf.MemoryKiB);
        w.Write(vault.Header.Kdf.Iterations);
        w.Write(vault.Header.Kdf.Parallelism);

        WriteBytes(w, vault.Header.Salt);
        WriteWrapped(w, vault.Header.DekByMaster);
        WriteWrapped(w, vault.Header.DekByRecovery);

        WriteBytes(w, vault.Nonce);
        WriteBytes(w, vault.Ciphertext);
        WriteBytes(w, vault.Tag);

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>바이너리를 볼트로 역직렬화한다. 매직/버전 불일치 시 예외를 던진다.</summary>
    public static EncryptedVault Deserialize(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var magic = r.ReadBytes(4);
        if (!magic.SequenceEqual(Encoding.ASCII.GetBytes(Magic)))
            throw new InvalidVaultFileException("볼트 파일 형식이 아닙니다.");

        var version = r.ReadByte();
        if (version != CurrentVersion)
            throw new InvalidVaultFileException($"지원하지 않는 볼트 버전입니다: {version}");

        var kdf = new KdfParams(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        var salt = ReadBytes(r);
        var dekByMaster = ReadWrapped(r);
        var dekByRecovery = ReadWrapped(r);
        var nonce = ReadBytes(r);
        var ciphertext = ReadBytes(r);
        var tag = ReadBytes(r);

        var header = new VaultHeader(salt, kdf, dekByMaster, dekByRecovery);
        return new EncryptedVault(header, nonce, ciphertext, tag);
    }

    /// <summary>원자적으로 파일에 저장한다: 임시 파일에 쓰고, 기존 파일은 .bak로 백업 후 교체.</summary>
    public static void Save(string path, EncryptedVault vault)
    {
        var bytes = Serialize(vault);
        var tmp = path + ".tmp";

        File.WriteAllBytes(tmp, bytes);          // ① 임시 파일에 완전히 쓰기
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true); // ② 이전 파일 백업
        File.Move(tmp, path, overwrite: true);   // ③ 원자적 교체
    }

    /// <summary>파일에서 볼트를 읽어 역직렬화한다.</summary>
    public static EncryptedVault Load(string path)
        => Deserialize(File.ReadAllBytes(path));

    private static void WriteBytes(BinaryWriter w, byte[] data)
    {
        w.Write(data.Length);
        w.Write(data);
    }

    private static void WriteWrapped(BinaryWriter w, WrappedKey k)
    {
        WriteBytes(w, k.Nonce);
        WriteBytes(w, k.Ciphertext);
        WriteBytes(w, k.Tag);
    }

    private static byte[] ReadBytes(BinaryReader r)
    {
        var len = r.ReadInt32();
        return r.ReadBytes(len);
    }

    private static WrappedKey ReadWrapped(BinaryReader r)
        => new(ReadBytes(r), ReadBytes(r), ReadBytes(r));
}
