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
    [Tooltip("지우개 반경 (미터, 월드 기준). 어떤 표면이든 같은 실제 크기로 지워진다")]
    [SerializeField] private float brushRadius = 0.3f;
    [SerializeField] private LayerMask paintableLayers;

    private PickupItem _pickupItem;
    private PlayerInteraction _localPlayer; // 현재 나를 들고 있는 주인
    private Camera _playerCamera;

    private float _nextCleanTime;
    private bool _isCleaning;

    private void Awake()
    {
        _pickupItem = GetComponent<PickupItem>();
    }

    private void Update()
    {
        // 네트워크 스폰이 완벽히 완료되지 않았다면 업데이트를 수행하지 않습니다
        if (!IsSpawned) return;

        // 1. 아무도 들고 있지 않으면 청소 로직 정지 (IsHeld는 PickupItem이 서버 권위로 관리)
        if (!_pickupItem.IsHeld)
        {
            _isCleaning = false;
            return;
        }

        // 2. 실소유주(오너)의 화면에서만 입력/레이캐스트 처리
        if (!IsOwner) return;

        // 3. 오너 = 곧 holder이므로, 로컬 플레이어를 그냥 나 자신으로 참조
        if (_localPlayer == null && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayerObj != null)
            {
                _localPlayer = localPlayerObj.GetComponent<PlayerInteraction>();
            }
        }
        if (_localPlayer == null) return;

        // 4. 청소
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
        if (_playerCamera == null && _localPlayer != null)
        {
            _playerCamera = _localPlayer.GetComponentInChildren<Camera>(true);
        }
        if (_playerCamera == null) return;
        Ray ray = _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (!Physics.Raycast(ray, out RaycastHit hit, cleanRange, paintableLayers)) return;
        
        var paintable = hit.collider.GetComponent<PaintableSurface>();
        if (paintable == null) return;

        // 브러시는 월드 좌표 기준으로 그리므로 맞은 지점(월드)을 보낸다
        RequestCleanServerRpc(paintable.SurfaceId, hit.point, brushRadius);
    }

    [ServerRpc]
    private void RequestCleanServerRpc(int surfaceId, Vector3 point, float radius, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        // RPC를 보낸 클라이언트가 진짜 이 아이템을 들고 있는 주인인지 체크
        var holder = _pickupItem.Holder;
        if (holder == null || holder.OwnerClientId != rpcParams.Receive.SenderClientId)
        {
            Debug.LogWarning("[검증 거부] 아이템을 들고 있지 않은 클라이언트가 청소를 요청함.");
            return;
        }
        if (holder.Inventory != null && holder.Inventory.CurrentSlot == 4) return;

        ApplyCleanClientRpc(surfaceId, point, radius);
    }

    [ClientRpc]
    private void ApplyCleanClientRpc(int surfaceId, Vector3 point, float radius)
    {
        PaintSurfaceManager.Instance.DrawAt(surfaceId, point, radius, isPaint: false);
    }
}