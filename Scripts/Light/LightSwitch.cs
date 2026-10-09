using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class LightSwitch : NetworkBehaviour
{
    // 스위치 상태를 동기화할 네트워크 변수 (기본값 true = 불 켜짐)
    public NetworkVariable<bool> IsLightOn = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 환경 제어를 위한 스크립트나 라이트 오브젝트 연결
    [SerializeField] private Light mainDirectionalLight;

    public override void OnNetworkSpawn()
    {
        // 상태가 변할 때마다 실행될 콜백 함수 등록
        IsLightOn.OnValueChanged += OnLightStateChanged;

        // 처음 접속했을 때 현재 상태 반영
        ApplyLightEffect(IsLightOn.Value);
    }

    // 플레이어가 스위치 앞에서 상호작용 키를 눌렀을 때 호출되는 함수
    public void Interact()
    {
        if (IsClient)
        {
            // 클라이언트는 서버에게 "나 스위치 눌렀어"라고 요청만 함
            RequestToggleSwitchServerRpc();
        }
    }

    [ServerRpc]
    private void RequestToggleSwitchServerRpc()
    {
        if (!IsServer) return; // 서버가 아닌 경우 무시
        // 호스트(서버)가 상태를 뒤집음 -> 자동으로 모든 클라이언트에게 동기화됨
        IsLightOn.Value = !IsLightOn.Value;
    }

    private void OnLightStateChanged(bool previousValue, bool newValue)
    {
        // 네트워크 변수가 변하면 모든 클라이언트(호스트 포함)에서 이 함수가 실행됨
        ApplyLightEffect(newValue);
    }

    private void ApplyLightEffect(bool lightOn)
    {
        if (lightOn)
        {
            // 불이 켜졌을 때: 원래대로 복구 -> 공통
            if (mainDirectionalLight != null) mainDirectionalLight.intensity = 1.0f;
            RenderSettings.fog = false; // 안개 끄기
        }
        else
        {
            // 기본적으로 디렉셔널 라이트는 다 같이 어둡게 만든다. 
            if (mainDirectionalLight != null) mainDirectionalLight.intensity = 0.05f;

            // 로컬 플레이어 컴포넌트를 찾아서 마피아인지 확인합니다.
            bool isLocalPlayerMafia = false;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            {
                var localPlayerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (localPlayerObj != null && localPlayerObj.TryGetComponent<RoleManager>(out var roleManager))
                {
                    if (roleManager.CurrentRole.Equals(PlayerRole.Mafia))
                    {
                        isLocalPlayerMafia = true;
                    }
                }
            }

            // 렌더링 차별화 코드
            if (isLocalPlayerMafia)
            {
                // 1. 마피아 클라이언트 화면: 불은 꺼졌지만 시야가 좁아지면 안 됨!
                RenderSettings.fog = true;
                RenderSettings.fogColor = Color.black;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.01f; // 안개 밀도를 극단적으로 낮춰서 먼 곳까지 다 보이게 합니다.
            }
            else
            {
                // 2. 시민 클라이언트 화면: 눈앞이 캄캄하고 시야가 좁아짐
                RenderSettings.fog = true;
                RenderSettings.fogColor = Color.black;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.2f; // 안개 밀도가 높아서 주변이 안 보임
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        IsLightOn.OnValueChanged -= OnLightStateChanged;
    }

    public void ToggleSwitchByServer()
    {
        if (!IsServer) return;
        IsLightOn.Value = !IsLightOn.Value;
    }
}