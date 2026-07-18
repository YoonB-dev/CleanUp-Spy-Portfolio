using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public class InGameUIController : NetworkBehaviour
{
    public static InGameUIController Instance { get; private set; }

    [SerializeField] private TMP_Text gameTimerTxt;
    [SerializeField] private GameObject jobDisplayCanvas;
    [SerializeField] private GameObject jobDisplayImage;
    [SerializeField] private TMP_Text jobNotificationText;
    public LocalizedString jobNotificationString; // 직업 알림 텍스트를 위한 LocalizedString
    private object[] _argsBuffer = new object[1]; // 한번만 생성하고 재활용 하기 위한 버파

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (GameTimerManager.Instance != null)
        {
            // 넷변수의 값이 변경될 때마다 텍스트 변환 함수를 호출하도록 리스너 등록
            GameTimerManager.Instance.RemainingTime.OnValueChanged += ConvertAndShowTime;
            GameTimerManager.Instance.CurrentState.OnValueChanged += OnTimerStateChanged;

            
            OnTimerStateChanged(TimerState.None, GameTimerManager.Instance.CurrentState.Value);

            // 첫 진입 시 UI에 현재 초기 시간 강제 표현 (예: 05:00)
            ConvertAndShowTime(0, GameTimerManager.Instance.RemainingTime.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (GameTimerManager.Instance != null)
        {
            GameTimerManager.Instance.CurrentState.OnValueChanged -= OnTimerStateChanged;
            GameTimerManager.Instance.RemainingTime.OnValueChanged -= ConvertAndShowTime;
        }
    }

    /// <summary>
    /// 타이머의 상태(Ready -> Playing)가 바뀔 때 호출되는 콜백
    /// </summary>
    private void OnTimerStateChanged(TimerState previousState, TimerState newState)
    {
        switch (newState)
        {
            case TimerState.Ready:
                // 5초 대기 시작 시점에 직업 패널을 킨다.
                if (jobDisplayCanvas != null) jobDisplayCanvas.SetActive(true);

                // 2. 로컬 플레이어 캐릭터나 직업 매니저를 통해 배정된 직업 정보를 띄운다.
                string job = RoleAssignmentManager.Instance.IsMafia(NetworkManager.Singleton.LocalClientId) ? "job_mafia" : "job_citizen";
                SetPlayerRoleNotification(job);
                break;

            case TimerState.Playing:
                // 3. 5초가 지나 본 게임 상태가 되면 직업 패널을 닫는다.
                if (jobDisplayCanvas != null) jobDisplayCanvas.SetActive(false);
                break;
        }
    }

    /// <summary>
    /// 데이터를 받아 분:초 포맷으로 바꿈
    /// </summary>
    private void ConvertAndShowTime(int previousValue, int newValue)
    {
        if (gameTimerTxt == null) return;

        int minutes = newValue / 60;
        int seconds = newValue % 60;

        gameTimerTxt.text = $"{minutes:D2}:{seconds:D2}";
    }


    /// <summary>
    /// 플레이어의 직업 번역본을 알림 텍스트에 적용합니다.
    /// </summary>
    public void SetPlayerRoleNotification(string jobTableKey)
    {
        // 1. 해당 언어에 맞춰 번역된 직업 이름("마피아" 혹은 "Mafia")을 먼저 뽑아옵니다.
        
        string translatedJobName = LocalizationSettings.StringDatabase.GetLocalizedString("InGame", jobTableKey);

        // 2. 버퍼 배열에 번역된 결과값을 넣어줍니다.
        _argsBuffer[0] = translatedJobName;

        // 3. Arguments에 넣어주면 전체 문장("당신의 직업은 마피아 입니다.")이 완성됩니다.
        jobNotificationString.Arguments = _argsBuffer;

        // 4. UI 텍스트에 반영
        jobNotificationText.text = jobNotificationString.GetLocalizedString();
    }
}