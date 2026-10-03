/// <summary>
/// 발소리용 바닥 표면 종류. 표면별 실제 소리는 FootstepProfile에서 정한다.
/// 인스펙터에는 숫자로 저장되므로 새 종류는 반드시 맨 뒤에 새 번호로 추가할 것 (중간에 끼우거나 번호를 바꾸면 기존 설정이 밀림)
/// </summary>
public enum SurfaceType
{
    Default = 0,    // 표시가 없는 바닥
    Dirt = 1,       // 흙
    Wood = 2,       // 나무
    Stone = 3,      // 돌, 콘크리트
}
