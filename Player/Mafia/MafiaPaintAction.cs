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
    [SerializeField] private CarryGripPoints paintGunGripPoints; // 그립 자세 지정용

    [Header("Settings")]
    [SerializeField] private float fireRange = 5;
    [SerializeField] private float fireRate = 0.01f;
    [Tooltip("브러시 반경 (미터, 월드 기준). 어떤 표면이든 같은 실제 크기로 칠해진다")]
    [SerializeField] private float brushRadius = 0.2f;
    [SerializeField] private LayerMask paintableLayers;

    [Header("Paint Gauge Settings")]
    [Tooltip("1초 발사 시 소모되는 페인트 양 (1.0 = 100%)")]
    [SerializeField] private float consumeRatePerSecond = 0.2f;
    [Tooltip("1초 비발사 시 회복되는 페인트 양")]
    [SerializeField] private float rechargeRatePerSecond = 0.1f;
    [Tooltip("발사 중단 후 회복 시작까지의 대기 시간(초)")]
    [SerializeField] private float rechargeDelay = 1.0f; // 재사용 딜레이
    // 서버 측 패킷 연사 방지용 타임스탬프
    private float _serverNextFireTime;
    // ===== 네트워크 상태 변수 (0.0 ~ 1.0 범위) =====
    private readonly NetworkVariable<float> _currentPaint = new(
        1.0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary> UI 표현을 위한 외부 참조 프로퍼티 (0.0 ~ 1.0) </summary>
    public float CurrentPaint => _currentPaint.Value;
    public System.Action<float> OnPaintGaugeChanged; // UI 갱신용 이벤트
    // ===== 외부 의존성 =====
    private PlayerInventory _inventory;
    private RoleManager _roleManager;
    private Camera _playerCamera;
    private RagdollNetworkSync _ragdollSync;
    private PlayerActionGate _gate;

    private float _nextFireTime;
    private bool _isFiring = false;     // 현재 마우스를 누르고 있는지 여부
    private float _lastFireTime;        // 회복 딜레이 계산용 타임스탬프

    private void Awake()
    {
        _roleManager = GetComponent<RoleManager>();
        _playerCamera = GetComponentInChildren<Camera>(true);
        _inventory = GetComponent<PlayerInventory>();
        _ragdollSync = GetComponent<RagdollNetworkSync>();
        _gate = PlayerActionGate.GetOrAdd(gameObject);

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

        _currentPaint.OnValueChanged += OnPaintValueChanged;

        if (IsServer)
        {
            _currentPaint.Value = 1.0f; // 초기화 시 100% 충전
        }

        // 역할은 모든 플레이어가 등록된 뒤에 배정되므로 스폰 시점엔 아직 None일 수 있다.
        // 변경 이벤트로 받아서 그때마다 게이지 구독/총 표시를 다시 판단한다
        if (_roleManager != null)
        {
            _roleManager.RoleChanged += OnRoleChanged;
            OnRoleChanged(_roleManager.CurrentRole);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_inventory != null)
        {
            _inventory.CurrentSlotNetworkVariable.OnValueChanged -= OnInventorySlotChanged;
        }

        _currentPaint.OnValueChanged -= OnPaintValueChanged;

        if (_roleManager != null)
        {
            _roleManager.RoleChanged -= OnRoleChanged;
        }

        if (IsOwner)
        {
            if (PaintGaugeUI.Instance != null)
            {
                PaintGaugeUI.Instance.Unsubscribe();
            }
        }
    }

    private void OnRoleChanged(PlayerRole role)
    {
        // 다른 플레이어 화면에서도 총이 보여야 하므로 표시는 모든 인스턴스에서 갱신
        if (_inventory != null)
        {
            UpdatePaintGunVisual(_inventory.CurrentSlot);
        }

        // 게이지 UI는 내 로컬 플레이어만, 마피아일 때만 바인딩
        if (!IsOwner || PaintGaugeUI.Instance == null) return;

        if (role == PlayerRole.Mafia)
        {
            PaintGaugeUI.Instance.Subscribe(this);
        }
        else
        {
            PaintGaugeUI.Instance.Unsubscribe();
        }
    }


    private void OnInventorySlotChanged(int previousValue, int newValue)
    {
        UpdatePaintGunVisual(newValue);
    }

    #region [총 꺼내기 / 집어넣기]
    private void UpdatePaintGunVisual(int currentSlot)
    {
        // 마피아이고 슬롯이 4번일 때만 총이 보여야 함
        bool shouldShow = (currentSlot == PlayerActionGate.PAINT_GUN_SLOT && _roleManager != null && _roleManager.CurrentRole == PlayerRole.Mafia);

        if (paintGunObject != null)
        {
            if (shouldShow)
            {
                AttachToHand(); // 켜기 전에 CarryAnchor로 재부모화
            }
            paintGunObject.SetActive(shouldShow);
        }

        // 서버 권위로 손 자세도 요청 (다른 아이템들과 동일한 패턴)
        RequestCarryPose(shouldShow);

        // 총이 해제되었다면 쏘고 있던 입력 상태도 강제 초기화
        if (!shouldShow)
        {
            _isFiring = false;
        }
    }
    // 총을 손에 붙이기
    private void AttachToHand()
    {
        if (paintGunObject == null) return;

        if (_ragdollSync == null || _ragdollSync.Poser == null || _ragdollSync.Poser.CarryAnchor == null)
        {
            return; // 래그돌이 아직 준비 안 됐으면 이번엔 스킵
        }

        Transform carryAnchor = _ragdollSync.Poser.CarryAnchor;
        paintGunObject.transform.SetParent(carryAnchor, false);
        // paintGunObject.transform.localPosition = Vector3.zero;
        // paintGunObject.transform.localRotation = Quaternion.identity;
    }

    private void RequestCarryPose(bool isCarrying)
    {
        // SetCarryRequested는 IsServerAuthoritative 인스턴스에서만 실제로 팔을 구동하므로
        // 서버에서 직접 호출해도 안전함
        if (_ragdollSync == null || _ragdollSync.Poser == null) return;

        if (isCarrying)
        {
            _ragdollSync.Poser.SetCarryTarget(paintGunGripPoints);
            _ragdollSync.Poser.SetCarryRequested(true);
        }
        else
        {
            _ragdollSync.Poser.SetCarryTarget(null);
            _ragdollSync.Poser.SetCarryRequested(false);
        }
    }
    

    public void OnSpawnPaintgun(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;
        if (_roleManager != null && _roleManager.CurrentRole != PlayerRole.Mafia) return;


        if (_inventory != null)
        {
            // 현재 실제 인벤토리 슬롯이 4번(페인트총)인지 체크
            bool isHoldingPaintGunNow = (_inventory.CurrentSlot == PlayerActionGate.PAINT_GUN_SLOT);
            int targetSlot = isHoldingPaintGunNow ? 0 : PlayerActionGate.PAINT_GUN_SLOT;

            // 인벤토리에 슬롯 변경을 요청 (꺼내기 제약은 ExecuteSlotChange의 게이트가 판정)
            _inventory.ExecuteSlotChange(targetSlot);
        }
    }
    #endregion

    #region [페인트 발사 및 게이지 동기화 로직]

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // 총을 꺼낸 상태일 때만 마우스 클릭 입력을 받음
        if (_inventory == null || _inventory.CurrentSlot != PlayerActionGate.PAINT_GUN_SLOT)
        {
            SetFiringServerRpc(false);
            _isFiring = false;
            return;
        }

        if (context.performed)
        {
            _isFiring = true;
            SetFiringServerRpc(true);
        }
        else if (context.canceled)
        {
            _isFiring = false;
            SetFiringServerRpc(false);
        }
    }
    [ServerRpc]
    private void SetFiringServerRpc(bool firing)
    {
        _isFiring = firing;
    }

    private void Update()
    {
        // 1. [서버 권위] 페인트 게이지 충전 및 소모 계산
        if (IsServer)
        {
            UpdatePaintServerLogic();
        }

        // 내 오브젝트이고, 총을 꺼냈고, 마우스를 누르고 있는 3가지 조건이 다 맞을 때만 작동
        if (!IsOwner || _inventory == null || _inventory.CurrentSlot != PlayerActionGate.PAINT_GUN_SLOT || !_isFiring) return;
        if (!_gate.CanDo(PlayerAction.FirePaint)) return;
        if (Time.time < _nextFireTime) return;
        _nextFireTime = Time.time + fireRate;

        ShootPaint();
    }

    /// <summary>
    /// 서버에서 매 프레임 페인트 충전 및 소모 상태를 직접 계산
    /// </summary>
    private void UpdatePaintServerLogic()
    {
        if (_isFiring && _currentPaint.Value > 0f)
        {
            // 발사 중: 페인트 감소
            _currentPaint.Value = Mathf.Max(0f, _currentPaint.Value - (consumeRatePerSecond * Time.deltaTime));
            _lastFireTime = Time.time;
        }
        else if (!_isFiring && _currentPaint.Value < 1.0f)
        {
            // 발사 중지 후 대기시간(rechargeDelay)이 지나면 자동 회복
            if (Time.time >= _lastFireTime + rechargeDelay)
            {
                _currentPaint.Value = Mathf.Min(1.0f, _currentPaint.Value + (rechargeRatePerSecond * Time.deltaTime));
            }
        }
    }

    private void ShootPaint()
    {
        if (_playerCamera == null) return;

        Ray ray = _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (!Physics.Raycast(ray, out RaycastHit hit, fireRange, paintableLayers)) return;

        var paintable = hit.collider.GetComponent<PaintableSurface>();
        if (paintable == null) return;

        // 브러시는 월드 좌표 기준으로 그리므로 맞은 지점(월드)을 보낸다
        RequestPaintServerRpc(paintable.SurfaceId, hit.point, brushRadius);
    }

    [ServerRpc]
    private void RequestPaintServerRpc(int surfaceId, Vector3 point, float radius)
    {
        // 총을 꺼낸 마피아만 칠할 수 있음(치트 방어)
        if (_roleManager == null || _roleManager.CurrentRole != PlayerRole.Mafia) return;
        if (!_gate.CanDo(PlayerAction.FirePaint)) return;
        if (_currentPaint.Value <= 0f) return;

        // 3. [보안 검증] 서버 측 발사 빈도(Cooltime) 검증 (클라이언트 연사 연동 방어)
        if (Time.time < _serverNextFireTime) return;
        _serverNextFireTime = Time.time + fireRate;

        // 4. [보안 검증] 현재 슬롯 및 발사 상태(SetFiringServerRpc) 재확인
        if (_inventory == null || _inventory.CurrentSlot != PlayerActionGate.PAINT_GUN_SLOT || !_isFiring) return;

        // 모든 검증 통과 시 클라이언트에 그리기 전파
        ApplyPaintClientRpc(surfaceId, point, radius);
    }

    [ClientRpc]
    private void ApplyPaintClientRpc(int surfaceId, Vector3 point, float radius)
    {
        PaintSurfaceManager.Instance.DrawAt(surfaceId, point, radius, isPaint: true);
    }

    private void OnPaintValueChanged(float previousValue, float newValue)
    {
        // 내 로컬 UI 갱신용 이벤트 호출
        OnPaintGaugeChanged?.Invoke(newValue);
    }

    #endregion

    private void OnDisable()
    {
        _isFiring = false;
        if (IsOwner)
        {
            SetFiringServerRpc(false);
        }
    }
}