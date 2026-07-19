using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 마피아용 페인트 총의 장착(꺼내기/넣기) 및 발사(RenderTexture 드로잉 rpc)를 모두 관리하는 통합 스크립트.
/// </summary>
public class MafiaPaintAction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject paintGunObject; // 자식으로 넣어둔 페인트 총 오브젝트
    [SerializeField] private Transform muzzleTransform;  // 이펙트/사운드용 (선택)

    [Header("Settings")]
    [SerializeField] private float fireRange = 5;
    [SerializeField] private float fireRate = 0.01f;
    [Tooltip("브러시 반경 (표면 UV 기준, 0~1 사이 값)")]
    [SerializeField] private float brushRadius = 0.02f;
    [SerializeField] private LayerMask paintableLayers;
    private PlayerInventory _inventory;
    private RoleManager _roleManager;
    private Camera _playerCamera;

    private float _nextFireTime;
    private bool _isFiring = false;     // 현재 마우스를 누르고 있는지 여부

    private void Awake()
    {
        _roleManager = GetComponent<RoleManager>();
        _playerCamera = GetComponentInChildren<Camera>(true);
        _inventory = GetComponent<PlayerInventory>();

        if (paintGunObject != null) paintGunObject.SetActive(false); // 처음엔 꺼둠
    }

    public override void OnNetworkSpawn()
    {
        if (_inventory != null)
        {
            _inventory.CurrentSlotNetworkVariable.OnValueChanged += OnInventorySlotChanged;

            // 방 중간 입장 유저나 초기 세팅을 위해 강제 한 번 실행
            UpdatePaintGunVisual(_inventory.CurrentSlot);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null)
        {
            _inventory.CurrentSlotNetworkVariable.OnValueChanged -= OnInventorySlotChanged;
        }
    }

    private void OnInventorySlotChanged(int previousValue, int newValue)
    {
        UpdatePaintGunVisual(newValue);
    }


    private void UpdatePaintGunVisual(int currentSlot)
    {
        // 마피아이고 슬롯이 4번일 때만 총이 보여야 함
        bool shouldShow = (currentSlot == 4 && _roleManager != null && _roleManager.CurrentRole == PlayerRole.Mafia);

        if (paintGunObject != null)
        {
            paintGunObject.SetActive(shouldShow);
        }

        // 총이 해제되었다면 쏘고 있던 입력 상태도 강제 초기화
        if (!shouldShow)
        {
            _isFiring = false;
        }
    }

    #region [총 꺼내기 / 집어넣기]

    public void OnSpawnPaintgun(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;
        if (_roleManager != null && _roleManager.CurrentRole != PlayerRole.Mafia) return;


        if (_inventory != null)
        {
            // 현재 실제 인벤토리 슬롯이 4번(페인트총)인지 체크
            bool isHoldingPaintGunNow = (_inventory.CurrentSlot == 4);
            int targetSlot = isHoldingPaintGunNow ? 0 : 4;

            // 인벤토리에 슬롯 변경을 요청 (이 요청이 서버를 거쳐 NetworkVariable을 바꿈)
            _inventory.ExecuteSlotChange(targetSlot);
        }
    }
    #endregion

    #region [페인트 발사 로직]

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // 총을 꺼낸 상태일 때만 마우스 클릭 입력을 받음
        if (_inventory == null || _inventory.CurrentSlot != 4)
        {
            _isFiring = false;
            return;
        }

        if (context.performed) _isFiring = true;
        else if (context.canceled) _isFiring = false;
    }

    private void Update()
    {
        // 내 오브젝트이고, 총을 꺼냈고, 마우스를 누르고 있는 3가지 조건이 다 맞을 때만 작동
        if (!IsOwner || _inventory == null || _inventory.CurrentSlot != 4 || !_isFiring) return;
        if (Time.time < _nextFireTime) return;
        _nextFireTime = Time.time + fireRate;

        ShootPaint();
    }

    private void ShootPaint()
    {
        if (_playerCamera == null) return;

        Ray ray = _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (!Physics.Raycast(ray, out RaycastHit hit, fireRange, paintableLayers)) return;

        var paintable = hit.collider.GetComponent<PaintableSurface>();
        if (paintable == null) return;

        Vector2 uv = hit.textureCoord;
        RequestPaintServerRpc(paintable.SurfaceId, uv, brushRadius);
    }

    [ServerRpc]
    private void RequestPaintServerRpc(int surfaceId, Vector2 uv, float radius)
    {
        // TODO: 마피아 유저가 맞는지 여기서 최종 검증하면 보안상 아주 좋음
        ApplyPaintClientRpc(surfaceId, uv, radius);
    }

    [ClientRpc]
    private void ApplyPaintClientRpc(int surfaceId, Vector2 uv, float radius)
    {
        PaintSurfaceManager.Instance.DrawAt(surfaceId, uv, radius, isPaint: true);
    }

    #endregion

    private void OnDisable()
    {
        // 스크립트가 꺼지거나 플레이어가 사망/종료 시 상태 리셋
        _isFiring = false;
    }
}