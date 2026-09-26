/// <summary>
/// UI 소리 종류 (버튼 클릭, 팝업 열기/닫기 등). 종류별 실제 소리는 UISoundProfile에서 정한다.
/// 인스펙터에는 숫자로 저장되므로 새 종류는 반드시 맨 뒤에 새 번호로 추가할 것 (중간에 끼우거나 번호를 바꾸면 기존 설정이 밀림)
/// </summary>
public enum UISoundType
{
    // 버튼
    Default = 0,    // 일반 클릭
    Confirm = 1,    // 확인, 게임 시작 등
    Cancel = 2,     // 닫기, 뒤로 가기, 취소
    Tab = 3,        // 탭/페이지 전환
    Toggle = 4,     // 켜기/끄기

    // 팝업
    PopupOpen = 5,
    PopupClose = 6,

    // 슬라이더
    SliderTick = 7, // 슬라이더를 움직일 때 눈금마다 나는 짧은 소리

    None = 100,     // 소리 없음
}
