using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LightInteraction : NetworkBehaviour
{
    private RoleManager _roleManager;
    private PlayerActionGate _gate;
    [SerializeField] private float interactDistance = 3f;
    [SerializeField] private Transform playerCameraTransform; // 서버에서 거리/시야 검증용 (플레이어 카메라 위치)
    [Header("UI Settings")]
    [SerializeField] private Image interactionGaugeImage;
    // 꾹 누르기 상호작용을 위한 변수
    private bool _serverIsHolding = false;
    private float _serverHoldTimer = 0f;
    private const float LIGHT_SWITCH_HOLD_TIME = 3.0f;
    private LightSwitch _serverTargetSwitch;

    // 클라이언트에서 꾹 누르기 상호작용을 위한 변수
    private bool _clientIsHolding = false;
    private float _clientHoldTimer = 0f;

    private void Awake()
    {
        _roleManager = GetComponent<RoleManager>();
        _gate = PlayerActionGate.GetOrAdd(gameObject);
        if (playerCameraTransform == null)
        {
            playerCameraTransform = GetComponentInChildren<Camera>(true)?.transform;
        }
    }

    private void Update()
    {
        // 1. [서버 측 검증] 서버에서 실시간으로 타이머를 돌리고 거리를 체크합니다.
        if (IsServer && _serverIsHolding && _serverTargetSwitch != null)
        {
            // [서버 검증 1] 실시간 거리 및 시야(조준) 확인
            if (!ValidateInteraction(_serverTargetSwitch))
            {
                ServerCancelInteraction();
                return;
            }

            // [서버 검증 2] 타이머 누적
            _serverHoldTimer += Time.deltaTime;

            if (_serverHoldTimer >= LIGHT_SWITCH_HOLD_TIME)
            {
                // 3초 도달 시 서버 권한으로 즉시 실행
                _serverTargetSwitch.ToggleSwitchByServer();
                ServerCancelInteraction();
            }
        }

        // 2. [클라이언트 측 로컬 연출] Local Owner 플레이어의 UI 게이지용 타이머
        if (IsClient && IsOwner && _clientIsHolding)
        {
            _clientHoldTimer += Time.deltaTime;
            // UI 게이지
            if (interactionGaugeImage != null)
            {
                // 3초 비율에 맞춰 fillAmount(0.0 ~ 1.0)를 채움.
                interactionGaugeImage.fillAmount = Mathf.Clamp01(_clientHoldTimer / LIGHT_SWITCH_HOLD_TIME);
            }
        }
    }

    #region 클라이언트 상호작용 시작/취소 요청 (PlayerInteraction에서 호출)

    public void StartLightInteraction(LightSwitch lightSwitch)
    {
        if (!IsOwner) return;

        // 클라이언트 로컬 예측 시동
        if (!lightSwitch.IsLightOn.Value)
        {
            _clientIsHolding = true;
            _clientHoldTimer = 0f;

            if (interactionGaugeImage != null)
            {
                interactionGaugeImage.fillAmount = 0f;
                interactionGaugeImage.gameObject.SetActive(true); // 게이지 UI 활성화
            }
        }

        // 서버에 상호작용 시작 의도를 알림
        RequestStartInteractionServerRpc(new NetworkObjectReference(lightSwitch.NetworkObject));
    }

    public void CancelLightInteraction()
    {
        if (!IsOwner) return;

        _clientIsHolding = false;
        _clientHoldTimer = 0f;

        // 서버에 상호작용 취소 의도를 알림
        RequestCancelInteractionServerRpc();
    }

    private void ResetClientUI()
    {
        _clientIsHolding = false;
        _clientHoldTimer = 0f;

        if (interactionGaugeImage != null)
        {
            interactionGaugeImage.fillAmount = 0f;
            interactionGaugeImage.gameObject.SetActive(false); // 게이지 UI 숨김
        }
    }

    #endregion

    #region 서버 RPC 및 검증 로직

    [ServerRpc]
    private void RequestStartInteractionServerRpc(NetworkObjectReference switchReference)
    {
        // 1. 오브젝트 유효성 검증
        if (!switchReference.TryGet(out NetworkObject switchNetObj) ||
            !switchNetObj.TryGetComponent<LightSwitch>(out LightSwitch lightSwitch))
        {
            return;
        }

        // 2. 물리적 거리 및 시야 1차 검증
        if (!ValidateInteraction(lightSwitch)) return;

        // 상호 배타 규칙 서버 재검증(치트 방어)
        if (!_gate.CanDo(PlayerAction.ToggleLight)) return;

        // 3. 상황별 역할(Role) 재검증
        if (lightSwitch.IsLightOn.Value)
        {
            // [불이 켜진 상태] 오직 마피아만 즉시 끌 수 있음
            if (_roleManager != null && _roleManager.CurrentRole.Equals(PlayerRole.Mafia))
            {
                lightSwitch.ToggleSwitchByServer();
                Debug.Log($"[서버] 마피아(Client:{OwnerClientId})가 불을 즉시 껐습니다.");
            }
        }
        else
        {
            // [불이 꺼진 상태] 서버에서 홀드 카운트 다운 시작
            _serverIsHolding = true;
            _serverHoldTimer = 0f;
            _serverTargetSwitch = lightSwitch;
            Debug.Log($"[서버] 플레이어(Client:{OwnerClientId})가 스위치 홀드를 시작합니다.");
        }
    }

    [ServerRpc]
    private void RequestCancelInteractionServerRpc()
    {
        ServerCancelInteraction();
    }

    // 서버 측 상태 초기화 함수
    private void ServerCancelInteraction()
    {
        if (_serverIsHolding)
        {
            Debug.Log($"[서버] 플레이어(Client:{OwnerClientId})의 스위치 상호작용이 종료/취소되었습니다.");
        }
        _serverIsHolding = false;
        _serverHoldTimer = 0f;
        _serverTargetSwitch = null;

        // --- [수정] ClientRpcParams를 생성하여 이 오너 플레이어에게만 전송하도록 설정 ---
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                // 이 플레이어 오브젝트의 주인의 ClientId를 타겟으로 지정합니다.
                TargetClientIds = new ulong[] { OwnerClientId }
            }
        };

        // 매개변수로 params를 넘겨주며 호출합니다.
        CancelInteractionClientRpc(clientRpcParams);
    }

    [ClientRpc]
    private void CancelInteractionClientRpc(ClientRpcParams clientRpcParams = default)
    {
        // 서버에 의해 강제로 취소당했을 때 클라이언트 로컬 타이머도 초기화
        if (IsOwner)
        {
            ResetClientUI();
        }
    }

    /// <summary>
    /// 서버 측에서 플레이어와 스위치 간의 거리 및 각도(시야)를 판정하는 핵심 검증 메서드
    /// </summary>
    private bool ValidateInteraction(LightSwitch lightSwitch)
    {
        if (playerCameraTransform == null || lightSwitch == null) return false;

        // 1. 거리 검증 (서버 시점)
        float distance = Vector3.Distance(playerCameraTransform.position, lightSwitch.transform.position);
        if (distance > interactDistance + 0.5f) // 네트워크 레이턴시를 감안해 약간의 마진(+0.5f)을 둡니다.
        {
            Debug.LogWarning($"[서버 검증 실패] 플레이어(Client:{OwnerClientId})와의 거리가 너무 멉니다. 거리: {distance}");
            return false;
        }

        // 2. 시야 방향(조준) 검증
        Vector3 dirToSwitch = (lightSwitch.transform.position - playerCameraTransform.position).normalized;
        float dot = Vector3.Dot(playerCameraTransform.forward, dirToSwitch);

        // 바라보는 방향과 스위치 방향의 사잇각이 약 60도 이내여야 함 (Dot 기준 0.5 이상)
        if (dot < 0.5f)
        {
            Debug.LogWarning($"[서버 검증 실패] 플레이어(Client:{OwnerClientId})가 스위치를 바라보고 있지 않습니다.");
            return false;
        }

        return true;
    }

    #endregion
}