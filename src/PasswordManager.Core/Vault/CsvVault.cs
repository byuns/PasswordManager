using System.Text;
using PasswordManager.Core.Models;

namespace PasswordManager.Core.Vault;

/// <summary>
/// 항목 목록을 자체 CSV 포맷으로 내보내고/가져온다(design 7.7). 헤더는
/// <c>title,url,login,password,notes,tags</c>이며, 태그는 한 칸에 <c>|</c>로 이어 붙인다.
/// RFC4180 규칙(따옴표·쉼표·개행 포함 필드는 큰따옴표로 감싸고 내부 따옴표는 두 번)으로 이스케이프한다.
/// 평문 비밀번호가 그대로 담기므로 호출부는 강한 경고를 거쳐야 한다.
/// </summary>
public static class CsvVault
{
    private const char TagSeparator = '|';
    private static readonly string[] Header = { "title", "url", "login", "password", "notes", "tags" };

    /// <summary>항목들을 CSV 문자열로 직렬화한다(헤더 포함). 줄 구분은 CRLF.</summary>
    public static string Export(IEnumerable<VaultEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', Header)).Append("\r\n");
        foreach (var e in entries)
        {
            var fields = new[]
            {
                e.Title, e.Url, e.Login, e.Password, e.Notes,
                string.Join(TagSeparator, e.Tags),
            };
            sb.Append(string.Join(',', fields.Select(EscapeField))).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>CSV 문자열을 항목 목록으로 파싱한다. 첫 줄은 헤더로 간주해 건너뛴다. 빈 줄은 무시.</summary>
    public static List<VaultEntry> Parse(string csv)
    {
        var records = ParseRecords(csv);
        var result = new List<VaultEntry>();
        // 첫 레코드는 헤더 → 건너뛴다.
        foreach (var fields in records.Skip(1))
        {
            // 완전히 빈 줄(필드 1개이고 값도 빈 문자열)은 무시.
            if (fields.Count == 1 && fields[0].Length == 0) continue;

            result.Add(new VaultEntry
            {
                Title = Field(fields, 0),
                Url = Field(fields, 1),
                Login = Field(fields, 2),
                Password = Field(fields, 3),
                Notes = Field(fields, 4),
                Tags = Field(fields, 5)
                    .Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
            });
        }
        return result;
    }

    private static string Field(IReadOnlyList<string> fields, int index) =>
        index < fields.Count ? fields[index] : string.Empty;

    private static string EscapeField(string value)
    {
        if (value.IndexOfAny(new[] { '"', ',', '\n', '\r' }) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>CSV 텍스트를 레코드(필드 배열) 목록으로 파싱한다. 따옴표 안의 쉼표·개행을 존중한다.</summary>
    private static List<List<string>> ParseRecords(string csv)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        void EndField() { fields.Add(field.ToString()); field.Clear(); }
        void EndRecord() { EndField(); records.Add(fields.ToList()); fields.Clear(); }

        while (i < csv.Length)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; i++; break;
                case ',': EndField(); i++; break;
                case '\r':
                    if (i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    EndRecord(); i++;
                    break;
                case '\n': EndRecord(); i++; break;
                default: field.Append(c); i++; break;
            }
        }

        // 마지막 레코드(개행으로 끝나지 않은 경우)를 마무리한다.
        if (field.Length > 0 || fields.Count > 0)
            EndRecord();

        return records;
    }
}
