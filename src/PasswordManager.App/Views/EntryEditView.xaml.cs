using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PasswordManager.ViewModels;

namespace PasswordManager.App.Views;

/// <summary>
/// 항목 추가/편집 폼. 비밀번호는 보안상 바인딩하지 않고 code-behind에서 다룬다.
/// 기존 비번은 실제 길이를 감추려고 <see cref="MaskLength"/>자리 고정 마스킹으로 표시하고,
/// 사용자가 칸에 들어와 새로 입력할 때만 실제 값을 교체한다(안 건드리면 원본 유지).
/// 생성기로 ViewModel.Password가 바뀌면(코드 경로) PasswordBox에도 반영한다.
/// </summary>
public partial class EntryEditView : UserControl
{
    /// <summary>실제 길이를 노출하지 않기 위한 고정 마스킹 자릿수.</summary>
    private const int MaskLength = 10;
    private static readonly string Placeholder = new('•', MaskLength);

    private EntryEditViewModel? _vm;
    private bool _suppress;            // 프로그램이 PasswordBox를 채우는 동안 변경 이벤트 무시
    private bool _showingPlaceholder;  // 지금 칸이 고정 마스킹(자리표시)만 보이는 상태인지
    private bool _userEdited;          // 사용자가 새 비밀번호를 실제로 입력했는지
    private bool _siteSelectionReady;  // 초기 바인딩이 일으키는 사이트명 선택 이벤트는 무시

    public EntryEditView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EntryEditViewModel vm)
            return;

        _vm = vm;
        // 기존 비번이 있으면 실제 값 대신 고정 10점을 보여준다. 실제 값은 vm.Password에 그대로 남는다.
        if (!string.IsNullOrEmpty(vm.Password))
            ShowPlaceholder();
        vm.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) =>
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        };
        // 여기까지 온 뒤의 선택만 "사용자가 고른 것"으로 본다. 편집 폼을 열 때 기존 사이트명이
        // 목록과 일치해 자동 선택되는 경우까지 자동 채움이 돌면 안 된다.
        _siteSelectionReady = true;
    }

    /// <summary>사이트명 드롭다운에서 기존 사이트를 고르면 URL·태그를 함께 채운다(TD-046).</summary>
    private void TitleInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_siteSelectionReady || e.AddedItems.Count == 0)
            return;
        (_vm ?? DataContext as EntryEditViewModel)?.ApplySiteCommand.Execute(e.AddedItems[0] as string);
    }

    private void ShowPlaceholder()
    {
        _suppress = true;
        PasswordInput.Password = Placeholder;
        _suppress = false;
        _showingPlaceholder = true;
        // 자리표시를 "보기"로 열어봐야 점만 나오므로 버튼을 감춘다. 기존 비번 확인은
        // 목록의 OTP 게이트 보기 화면이 담당한다(TD-047).
        PasswordInput.RevealButtonEnabled = false;
    }

    private void HidePlaceholder()
    {
        _showingPlaceholder = false;
        PasswordInput.RevealButtonEnabled = true;
    }

    private void PasswordInput_GotKeyboardFocus(object sender, RoutedEventArgs e)
    {
        // 편집을 시작하면 자리표시를 지워 빈 칸에서 새로 입력하게 한다.
        if (!_showingPlaceholder)
            return;
        _suppress = true;
        PasswordInput.Password = string.Empty;
        _suppress = false;
        HidePlaceholder();
    }

    private void PasswordInput_LostKeyboardFocus(object sender, RoutedEventArgs e)
    {
        // 새로 입력하지 않고 떠났으면(원본 유지) 다시 자리표시를 보여준다.
        if (_userEdited || PasswordInput.Password.Length != 0)
            return;
        if (!string.IsNullOrEmpty(_vm?.Password))
            ShowPlaceholder();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 생성기가 비번을 만들면 실제 값을 칸에 반영하고 편집된 것으로 표시한다.
        if (e.PropertyName != nameof(EntryEditViewModel.Password) || _vm is null)
            return;
        if (_showingPlaceholder || PasswordInput.Password != _vm.Password)
        {
            _suppress = true;
            PasswordInput.Password = _vm.Password;
            _suppress = false;
            HidePlaceholder();
            _userEdited = true;
        }
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || _vm is null)
            return;
        _userEdited = true;
        HidePlaceholder();
        _vm.Password = PasswordInput.Password;
    }

    /// <summary>태그 입력창에서 Space/Enter를 누르면 현재 입력을 태그 칩으로 확정한다(인스타식).</summary>
    private void TagInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter))
            return;
        (_vm ?? DataContext as EntryEditViewModel)?.AddTagCommand.Execute(null);
        e.Handled = true;
    }
}
