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

    private PlayerInteraction _playerInteraction;
    private Camera _playerCamera;

    private float _nextFireTime;
    private bool _isPaintEquip = false; // 현재 총을 꺼냈는지 여부
    private bool _isFiring = false;     // 현재 마우스를 누르고 있는지 여부

    private void Awake()
    {
        _playerInteraction = GetComponent<PlayerInteraction>();
        _playerCamera = GetComponentInChildren<Camera>(true);

        if (paintGunObject != null) paintGunObject.SetActive(false); // 처음엔 꺼둠
    }

    #region [총 꺼내기 / 집어넣기]

    public void OnSpawnPaintgun(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;

        // 이미 다른 일반 아이템을 들고 있다면 총을 꺼내지 못하게 막음
        if (!_isPaintEquip && _playerInteraction != null && _playerInteraction.IsHoldingItem())
        {
            Debug.Log("이미 물건을 들고 있어 페인트 총을 꺼낼 수 없습니다.");
            return;
        }

        // 상태 전환
        _isPaintEquip = !_isPaintEquip;

        // 총을 집어넣는다면 쏘고 있던 상태도 강제로 해제
        if (!_isPaintEquip)
        {
            _isFiring = false;
        }

        // 1. [로컬] 내 화면에서 즉시 총을 켜고/끄기
        TogglePaintgunLocal(_isPaintEquip);

        // 2. [서버/클라이언트] 다른 사람들에게도 내 총 상태 동기화 및 NetworkVariable 변경
        TogglePaintgunServerRpc(_isPaintEquip);
    }

    private void TogglePaintgunLocal(bool isOut)
    {
        if (paintGunObject != null) paintGunObject.SetActive(isOut);
    }

    [ServerRpc]
    private void TogglePaintgunServerRpc(bool isOut)
    {
        if (_playerInteraction != null)
        {
            _playerInteraction.IsHoldingPaintGun.Value = isOut;
        }
        TogglePaintgunClientRpc(isOut);
    }

    [ClientRpc]
    private void TogglePaintgunClientRpc(bool isOut)
    {
        if (IsOwner) return; // 주인은 위에서 이미 처리했으므로 패스
        if (paintGunObject != null) paintGunObject.SetActive(isOut);
    }

    #endregion

    #region [페인트 발사 로직]

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        // 총을 꺼낸 상태일 때만 마우스 클릭 입력을 받음
        if (!_isPaintEquip)
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
        if (!IsOwner || !_isPaintEquip || !_isFiring) return;
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
        _isPaintEquip = false;
    }
}