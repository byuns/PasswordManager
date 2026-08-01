# PasswordManager

> 내 PC에서만 동작하는 **로컬 전용** 비밀번호 관리자 (Windows · .NET 8 · WPF)

여러 사이트·게임의 아이디와 비밀번호를 안전하게 보관하고, 비밀번호를 바꿀 때마다
**이전 비밀번호와 마지막 변경일**까지 기록해 두는 데스크톱 앱입니다.
모든 데이터는 이 PC의 **암호화된 파일 하나**에만 저장되며, 클라우드로 나가지 않습니다.

---

## ✨ 주요 기능

- **계정 관리** — 사이트별 아이디·비밀번호·메모·태그 저장, 한 사이트에 여러 계정 등록 가능
- **강한 암호화** — 마스터 비밀번호로 볼트 전체를 잠금 (Argon2id + AES-256-GCM)
- **2단계 열람 보호** — 개별 비밀번호를 볼 때 폰의 OTP(2FA)를 한 번 더 확인
- **비밀번호 이력** — 변경할 때마다 이전 값과 변경 날짜를 자동 보관 (분실 방지)
- **비밀번호 생성기** — 길이·문자 종류를 골라 안전한 비밀번호를 즉석에서 생성
- **검색·태그 필터** — 제목·아이디·태그로 원하는 계정을 빠르게 찾기
- **즐겨찾기·정렬** — 자주 쓰는 계정을 맨 위에 고정, 이름·최근 변경·최근 사용순 정렬
- **휴지통** — 삭제한 계정을 30일간 보관해 실수로 지워도 되살리기
- **키보드 단축키** — `Ctrl+F` 검색 · `Ctrl+N` 추가 · `Ctrl+L` 잠금 · `↑↓` 이동 등
- **복구 키** — 마스터 비밀번호를 잊어도 최초 발급한 복구 키로 되살리기
- **위생 관리** — 클립보드 자동 삭제, 자동 잠금, 화면 마스킹

---

## 🔒 어떻게 안전한가요?

1. **마스터 비밀번호 하나**로 볼트 파일 전체를 암호화합니다. 이 비밀번호가 없으면 목록조차 볼 수 없습니다.
2. 개별 비밀번호는 평소엔 잠가 두고, **"보기"를 누른 순간에만** 풀어서 보여줍니다 (지연 복호화).
3. 그 순간에도 폰 OTP를 한 번 더 확인해, 잠깐 자리를 비운 사이 훔쳐보는 것을 막습니다.

> ⚠️ 로컬 전용이라 **마스터 비밀번호와 복구 키를 둘 다 잃으면 복구할 수 없습니다.**
> 복구 키는 종이 등 앱 밖 안전한 곳에 꼭 보관하세요.

---

## 🛠 기술 스택

| 구분 | 사용 |
|---|---|
| 플랫폼 | Windows 10/11 데스크톱 |
| 언어·런타임 | C# / .NET 8 (LTS) |
| UI | WPF + MVVM (CommunityToolkit.Mvvm) |
| 암호화 | Argon2id (Konscious) · AES-256-GCM (.NET 내장) |
| OTP | TOTP (RFC 6238) |

---

## 📁 프로젝트 구조

```
PasswordManager/
├─ src/
│  ├─ PasswordManager.App/         # WPF 화면 (Views, App.xaml)
│  ├─ PasswordManager.ViewModels/  # 화면 로직 (MVVM)
│  ├─ PasswordManager.Core/        # 암호화·OTP 등 순수 로직
│  └─ PasswordManager.Storage/     # 볼트 파일 읽기/쓰기
├─ tests/                          # 단위 테스트
└─ docs/                           # 설계·의사결정 문서
```

---

## 🚀 설치하고 쓰기

### 1. 실행 파일 만들기

.NET 런타임까지 담은 **단일 exe**로 게시합니다. 받는 PC에 .NET을 따로 깔 필요가 없습니다.

```powershell
dotnet publish src/PasswordManager.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```

`publish/PasswordManager.exe` (약 71MB) 하나가 나옵니다. 원하는 폴더로 옮겨 두고,
바로가기를 만들어 시작 메뉴나 작업표시줄에 고정하면 됩니다.

### 2. 첫 실행

1. **마스터 비밀번호 설정** — 12자 이상. 이 하나로 볼트 전체가 잠깁니다.
2. **복구 키 발급** — 화면에 뜬 복구 키를 종이 등 **앱 밖에** 보관하고 확인 체크.
3. **OTP 등록** — 표시된 QR을 폰의 Google Authenticator 등으로 스캔.
   등록해야 비밀번호 보기·편집·삭제가 열립니다.

볼트 파일은 `%APPDATA%\PasswordManager\vault.dat`에 만들어집니다.
저장할 때마다 이전 파일이 `vault.dat.bak`으로 보존됩니다.

> ⚠️ **백업**: 볼트 파일을 잃으면 계정 정보도 사라집니다.
> 설정 > 백업·데이터에서 주기적으로 백업해 두세요.

---

## 🧑‍💻 개발

```powershell
dotnet build                                  # 빌드
dotnet test                                   # 전체 테스트
dotnet run --project src/PasswordManager.App  # 실제 볼트로 실행
```

더미 데이터가 든 **임시 볼트**로 화면을 확인하려면:

```powershell
$env:PWM_VAULT_PATH = "$env:TEMP\pwm-seed\vault.dat"   # 실제 볼트와 분리
$env:PWM_SEED = "1"                                     # 볼트가 없을 때만 시드
dotnet run --project src/PasswordManager.App
```

시드 볼트의 마스터 비밀번호는 `DevSeed.cs`에 있습니다.

---

## 📚 문서

자세한 설계와 결정 배경은 `docs/` 폴더에 있습니다.

- [`docs/design.md`](docs/design.md) — 전체 설계·아키텍처
- [`docs/tradeoffs.md`](docs/tradeoffs.md) — 선택·트레이드오프 기록 (TD-001 …)
- [`docs/flows.md`](docs/flows.md) — 주요 화면 흐름
- [`docs/ui.md`](docs/ui.md) · [`docs/design-ux.md`](docs/design-ux.md) — UI/UX 설계

---

## 개발 방식

테스트 우선(TDD)으로 개발합니다. 암호화·TOTP·이력 계산 등 핵심 로직은 단위 테스트로 검증합니다.
