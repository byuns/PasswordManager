using CommunityToolkit.Mvvm.ComponentModel;

namespace PasswordManager.ViewModels;

/// <summary>메뉴얼의 안내 한 줄. <paramref name="Icon"/>은 WPF-UI SymbolRegular 이름(예: "Key24").</summary>
public sealed record ManualItem(string Icon, string Title, string Description);

/// <summary>주제별 안내 묶음(카드 하나). 아이콘·제목이 카드 머리가 된다.</summary>
public sealed record ManualSection(string Icon, string Title, IReadOnlyList<ManualItem> Items);

/// <summary>단축키 한 줄. <paramref name="Keys"/>는 키칩으로 나란히 그려진다(예: ["Ctrl", "F"]).</summary>
public sealed record ManualShortcut(IReadOnlyList<string> Keys, string Description);

/// <summary>
/// 사용 안내(메뉴얼) 화면 ViewModel. 내용은 정적이지만 항목 수가 늘어 View에 두면 같은 XAML이
/// 반복되므로, 문구를 데이터로 여기에 모으고 View는 템플릿만 갖는다(design-ux 5절·TD-039).
/// </summary>
public sealed partial class ManualViewModel : ObservableObject
{
    /// <summary>주제별 안내 섹션. 처음 쓰는 순서(시작 → 매일 → 보안)로 배열한다.</summary>
    public IReadOnlyList<ManualSection> Sections { get; } = new[]
    {
        new ManualSection("Rocket24", "시작하기", new[]
        {
            new ManualItem("Add24", "계정 추가",
                "오른쪽 위 ＋ 버튼으로 사이트명·아이디·비밀번호를 등록합니다. 같은 사이트에 여러 계정을 둘 수 있습니다."),
            new ManualItem("Tag24", "태그로 분류",
                "‘메인’·‘부캐’처럼 자유롭게 태그를 답니다. 같은 사이트의 여러 계정을 구분하는 기준이 됩니다."),
            new ManualItem("Key24", "OTP 등록",
                "설정에서 앱 2FA를 등록합니다. 등록해야 비밀번호 보기·편집·삭제가 열립니다."),
        }),
        new ManualSection("Search24", "매일 쓰기", new[]
        {
            new ManualItem("Search24", "찾기",
                "검색창에 사이트·아이디·태그를 입력하고, 태그 칩으로 더 좁힙니다. 칩을 여러 개 켜면 하나라도 맞는 계정이 남습니다."),
            new ManualItem("Eye24", "비밀번호 보기",
                "행의 자물쇠 버튼으로 OTP 인증을 거치면 보기·편집·삭제가 나타납니다. 비밀번호는 그 순간에만 복호화됩니다."),
            new ManualItem("Copy24", "복사",
                "아이디는 바로 복사되고, 비밀번호는 인증 후 복사됩니다. 복사한 값은 설정한 시간이 지나면 클립보드에서 자동으로 지워집니다."),
            new ManualItem("Star24", "즐겨찾기·정렬",
                "아이디 왼쪽 별을 누르면 맨 위 ‘즐겨찾기’ 그룹에 바로가기가 생깁니다(원래 자리에도 그대로 남습니다). 검색창 옆에서 이름순·최근 변경순·최근 사용순으로 정렬을 바꿀 수 있습니다."),
        }),
        new ManualSection("ShieldKeyhole24", "보안·복구", new[]
        {
            new ManualItem("LockClosed24", "잠금",
                "왼쪽 위 잠금 버튼으로 즉시 잠그고, 일정 시간 쓰지 않으면 자동으로 잠깁니다."),
            new ManualItem("ShieldKeyhole24", "복구 키",
                "마스터 비밀번호를 잊으면 복구 키로만 되살릴 수 있습니다. 앱 밖 안전한 곳에 따로 보관하세요."),
            new ManualItem("ArrowReset24", "전체 초기화",
                "마스터 비밀번호와 복구 키를 모두 잃었다면, 잠금 화면의 비밀번호 칸에 /reset 을 입력해 처음부터 다시 시작할 수 있습니다. 저장된 모든 계정이 영구적으로 사라집니다."),
            new ManualItem("Delete24", "휴지통",
                "삭제한 계정은 30일간 휴지통에 보관됩니다. 설정 > 백업·데이터 > 휴지통에서 되살리거나 완전히 지울 수 있습니다."),
            new ManualItem("Settings24", "설정",
                "자동 잠금 시간, 클립보드 삭제 시간, 백업·복원, 슬랙 알림을 조정합니다."),
        }),
    };

    /// <summary>계정 화면 단축키. MainView.xaml의 InputBindings와 짝을 이룬다(TD-039).</summary>
    public IReadOnlyList<ManualShortcut> Shortcuts { get; } = new[]
    {
        new ManualShortcut(new[] { "Ctrl", "F" }, "검색창으로 이동"),
        new ManualShortcut(new[] { "Ctrl", "N" }, "새 계정 추가"),
        new ManualShortcut(new[] { "Ctrl", "L" }, "즉시 잠금"),
        new ManualShortcut(new[] { "Ctrl", "B" }, "선택한 계정의 아이디 복사"),
        new ManualShortcut(new[] { "↑", "↓" }, "계정 선택 이동"),
        new ManualShortcut(new[] { "Enter" }, "선택한 계정 인증(OTP)"),
        new ManualShortcut(new[] { "Esc" }, "검색어·태그 필터 해제"),
    };
}
