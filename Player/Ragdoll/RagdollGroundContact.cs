using UnityEngine;

/// <summary>
/// 래그돌 뼈에 붙어 외부 물체와의 충돌을 드라이버에 알린다. 기상 타이밍(착지) 판정용. <br/>
/// 서버에서만 물리가 돌므로 서버에서만 부착된다.
/// </summary>
[DisallowMultipleComponent]
public class RagdollGroundContact : MonoBehaviour
{
    // 접촉면이 위를 향한 정도. 1이면 완전 수평, 0이면 수직(벽). 약 60도 이내 경사만 바닥으로 인정
    private const float MIN_GROUND_NORMAL_Y = 0.5f;

    private RagdollDriver _driver;
    private Transform _ragdollRoot;
    private Transform _playerRoot;

    /// <summary>부착 시 판정에 필요한 기준 계층을 넘겨받는다.</summary>
    /// <param name="driver">접촉을 보고할 드라이버</param>
    /// <param name="playerRoot">Player 루트(자기 캡슐 제외용)</param>
    public void Bind(RagdollDriver driver, Transform playerRoot)
    {
        _driver = driver;
        _ragdollRoot = driver.transform;
        _playerRoot = playerRoot;
    }

    private void OnCollisionEnter(Collision collision)
    {
        Report(collision);
    }

    // 이미 닿은 채로 쓰러지는 경우가 있어 Enter만으로는 놓친다
    private void OnCollisionStay(Collision collision)
    {
        Report(collision);
    }

    private void Report(Collision collision)
    {
        // 평상시에도 뼈는 월드와 계속 부딪히므로 쓰러진 동안만 본다
        if (_driver == null || !_driver.IsLimp)
        {
            return;
        }

        // 자기 뼈끼리 부딪힌 것과 자기 캡슐은 착지가 아니다
        Transform other = collision.transform;
        if (other.IsChildOf(_ragdollRoot)
            || (_playerRoot != null && other.IsChildOf(_playerRoot)))
        {
            return;
        }

        // 벽에 부딪힌 건 착지가 아니다. 법선이 위를 향한 면(바닥·상자 윗면)에 닿아야 인정.
        // contacts 프로퍼티는 매번 배열을 할당하므로 GetContact로 훑는다
        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            if (collision.GetContact(i).normal.y >= MIN_GROUND_NORMAL_Y)
            {
                _driver.ReportGroundContact();
                return;
            }
        }
    }
}
