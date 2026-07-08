using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PickupItem))]
[RequireComponent(typeof(NetworkObject))]
public class PlaceableBox : NetworkBehaviour
{
    private Rigidbody rb;
    private Collider boxCollider;
    private PickupItem pickupItem;
    private float boxSize = 1.0f;
    // 무너짐 연출용 변수
    private float randomPushForce = 3.0f;  // 양옆으로 튕기는 힘의 세기
    private float randomTorqueForce = 1.0f; // 회전하며 떨어지는 힘의 세기
    // 배치 상태여부.
    private readonly NetworkVariable<bool> _isPlacedNet = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        boxCollider = GetComponent<Collider>();
        pickupItem = GetComponent<PickupItem>();
    }
    public override void OnNetworkSpawn()
    {
        // 네트워크 변수 값이 바뀔 때(설치/무너짐) 실행될 클라이언트 콜백 등록
        _isPlacedNet.OnValueChanged += OnPlacementStateChanged;

        // JIP(진입 중인 클라이언트)나 초기 스폰 시 상태에 맞게 태그를 1번 정돈합니다.
        RefreshTagLocal(_isPlacedNet.Value);
    }

    /// <summary>
    /// 서버에서 박스를 특정 그리드 위치에 고정할 때 호출한다.
    /// </summary>
    public void PlaceAt(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (!IsServer) return;

        _isPlacedNet.Value = true;

        // 1. 위치 및 회전 정렬
        transform.position = targetPosition;
        transform.rotation = targetRotation;

        // 2. 물리 엔진 비활성화 (공중에 딱 굳어있게 만듦)
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 3. 콜라이더는 켜두되, 다른 박스가 이 위에 쌓일 수 있도록 레이어나 태그 변경
        if (boxCollider != null)
        {
            boxCollider.enabled = true;
        }

        // 다른 박스가 레이캐스트로 검사할 수 있도록 태그를 변경합니다.
        gameObject.tag = "PlacedBox";

        // 점수 추가
        ScoreManager.Instance?.AddBoxScore();
    }

    /// <summary>
    /// 플레이어가 이 박스를 다시 주웠을 때 상태를 초기화합니다.
    /// </summary>
    public void OnPickedUp()
    {
        if (!IsServer) return;
        Demolish();
    }

    /// <summary>
    /// 이 박스의 고정 상태를 해제하고 물리력을 완전히 되돌린다.
    /// 여기서 점수 해제 및 연쇄 무너짐 처리를 담당.
    /// </summary>
    public void Demolish()
    {
        if (!IsServer) return;
        if (_isPlacedNet.Value == false)
        {
            return;
        }

        _isPlacedNet.Value = false;
        RefreshTagLocal(false);
        gameObject.tag = "Untagged"; // 태그 원상복구

        if (rb != null)
        {
            rb.isKinematic = false; // 물리 엔진 재가동 (추락 시작)
            rb.useGravity = true;

            // 랜덤한 힘과 회전을 주어 자연스럽게 무너지는 연출
            Vector3 randomDirection = new Vector3(
                Random.Range(-1.0f, 1.0f),
                Random.Range(0.1f, 0.5f), // 아주 살짝 위로 통 튀튀하게 Y축 양수 부여
                Random.Range(-1.0f, 1.0f)
            ).normalized;

            // 1. 순간적인 충격 힘(Impulse)을 주어 옆으로 튕겨 나가게 만듭니다.
            rb.AddForce(randomDirection * randomPushForce, ForceMode.Impulse);

            // 2. 상자가 돌면서 떨어지도록 무작위 회전력(Torque)도 살짝 가해줍니다.
            Vector3 randomTorque = new Vector3(
                Random.Range(-1.0f, 1.0f),
                Random.Range(-1.0f, 1.0f),
                Random.Range(-1.0f, 1.0f)
            ).normalized;
            rb.AddTorque(randomTorque * randomTorqueForce, ForceMode.Impulse);
        }

        // 점수 차감
        ScoreManager.Instance?.SubtractBoxScore();

        // 2. 연쇄 무너짐 처리: 내 바로 위에 다른 박스가 고정되어 있는지 검사.
        // 내 중심점에서 위 방향(Vector3.up * boxSize) 공간을 체크합니다.
        Vector3 upperCheckPos = transform.position + (Vector3.up * boxSize);

        // OverlapBox로 내 바로 위에 얹혀있는 콜라이더를 수색합니다.
        Collider[] upperColliders = Physics.OverlapBox(upperCheckPos, Vector3.one * (boxSize * 0.45f));
        foreach (var col in upperColliders)
        {
            // 위에 얹힌 오브젝트가 '고정된 박스'라면, 그 박스도 연쇄적으로 고정을 풀어버립니다.
            if (col.CompareTag("PlacedBox") && col.TryGetComponent<PlaceableBox>(out var upperBox))
            {
                upperBox.Demolish(); // 3단, 4단 박스까지 타고 올라가며 연쇄 무너짐 발동!
            }
        }
    }

    /// <summary>
    /// [클라이언트 공통] 서버가 배치 상태 변수를 바꿨을 때 태그를 로컬에서 직접 갱신해주는 동기화 함수
    /// </summary>
    private void OnPlacementStateChanged(bool previousValue, bool newValue)
    {
        RefreshTagLocal(newValue);
        // 서버가 _isPlacedNet을 false로 바꾸면, 클라이언트도 물리 엔진을 켭니다.
        if (!newValue)
        {
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
        }
    }

    /// <summary>
    /// 상태값에 따라 오브젝트의 태그를 동기화.
    /// </summary>
    private void RefreshTagLocal(bool isPlaced)
    {
        gameObject.tag = isPlaced ? "PlacedBox" : "Untagged";
    }
    public void RequestDemolish()
    {
        if (IsServer)
        {
            Demolish();
        }
    }
}
