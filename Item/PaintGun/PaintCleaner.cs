using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 바닥에 놓여있는 청소 도구 프리펩에 부착하는 스크립트.
/// PickupItem에 의해 들려있는 상태에서만 발사(청소) 입력을 감지하고 작동한다.
/// </summary>
[RequireComponent(typeof(PickupItem))]
public class PaintCleaner : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float cleanRange = 3f;
    [SerializeField] private float cleanRate = 0.1f;
    [SerializeField] private float brushRadius = 0.08f;
    [SerializeField] private LayerMask paintableLayers;

    private PickupItem _pickupItem;
    private PlayerInteraction _currentHolder; // 현재 나를 들고 있는 주인
    private Camera _playerCamera;

    private float _nextCleanTime;
    private bool _isCleaning;

    private void Awake()
    {
        _pickupItem = GetComponent<PickupItem>();
    }

    private void Update()
    {
        if (IsOwner)
        {
            // 내가 이 아이템의 소유자라면, 내 로컬 플레이어 캐릭터 컴포넌트를 주인으로 설정!
            if (_currentHolder == null && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayerObj != null)
                {
                    _currentHolder = localPlayerObj.GetComponent<PlayerInteraction>();
                }
            }
        }
        else if (IsServer)
        {
            // 서버(호스트) 시점에서는 PickupItem이 가지고 있는 _holder를 그대로 신뢰해도 됩니다.
            _currentHolder = _pickupItem.Holder;
        }
        // 1. [공통] 현재 아무도 안 들고 있다면 청소 로직 완전 정지
        if (_currentHolder == null)
        {
            _isCleaning = false;
            return;
        }
        
        // 2. [로컬] 나를 들고 있는 실소유주(IsOwner)의 화면에서만 마우스 입력 및 레이캐스트 연산 수행
        if (!IsOwner) return;

        // 주인이 페인트 총을 들고 있다면(마피아라면) 청소기 작동 방지
        if (_currentHolder.IsHoldingPaintGun.Value)
        {
            _isCleaning = false;
            return;
        }

        // 3. 청소기 작동 중일 때 주기적으로 발사
        if (!_isCleaning) return;
        if (Time.time < _nextCleanTime) return;
        _nextCleanTime = Time.time + cleanRate;
        CleanPaint();
    }

    /// <summary>
    /// PlayerInteraction의 Input Action 이벤트(OnClean)에서 이 함수를 호출해 주어야 합니다.
    /// </summary>
    public void SetCleaningInput(bool isPressing)
    {
        _isCleaning = isPressing;
    }

    private void CleanPaint()
    {
        // 캐싱이 안 되어있다면 주인의 카메라를 찾아옴
        if (_playerCamera == null && _currentHolder != null)
        {
            _playerCamera = _currentHolder.GetComponentInChildren<Camera>(true);
        }
        if (_playerCamera == null) return;
        Ray ray = _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (!Physics.Raycast(ray, out RaycastHit hit, cleanRange, paintableLayers)) return;
        
        var paintable = hit.collider.GetComponent<PaintableSurface>();
        if (paintable == null) return;

        Vector2 uv = hit.textureCoord;

        // 내 주인의 컴포넌트 정보와 함께 서버에 청소 요청
        RequestCleanServerRpc(paintable.SurfaceId, uv, brushRadius);
    }

    [ServerRpc]
    private void RequestCleanServerRpc(int surfaceId, Vector2 uv, float radius, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        // RPC를 보낸 클라이언트가 진짜 이 아이템을 들고 있는 주인인지 체크
        if (_currentHolder == null || _currentHolder.OwnerClientId != rpcParams.Receive.SenderClientId)
        {
            Debug.LogWarning($"[검증 거부] 아이템을 들고 있지 않은 클라이언트가 청소를 요청함.");
            return;
        }

        // 마피아 총 검증
        if (_currentHolder.IsHoldingPaintGun.Value) return;

        ApplyCleanClientRpc(surfaceId, uv, radius);
    }

    [ClientRpc]
    private void ApplyCleanClientRpc(int surfaceId, Vector2 uv, float radius)
    {
        PaintSurfaceManager.Instance.DrawAt(surfaceId, uv, radius, isPaint: false);
    }
}