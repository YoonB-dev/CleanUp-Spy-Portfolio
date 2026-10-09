using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public class InGameUIController : NetworkBehaviour
{
    public static InGameUIController Instance { get; private set; }

    [SerializeField] private TMP_Text gameTimerTxt; 
    [SerializeField] private GameObject jobDisplayCanvas; // 전체 캔버스 -> 시작하자마자 띄워서 일관성 유지
    [SerializeField] private GameObject jobDisplayPanel; // 직업 결정되면 동작 -> 애니메이션으로 띄워짐
    [SerializeField] private Image roleAccent;
    [SerializeField] private Color citizenColor = new(0.180f, 0.420f, 0.320f);
    [SerializeField] private Color mafiaColor = new(0.871f, 0.482f, 0.157f);
    [SerializeField] private TMP_Text jobNotificationText;

    [Header("로컬라이제이션 string 연결")]
    public LocalizedString jobNotificationString; 
    [SerializeField] private LocalizedString citizenDesString;
    [SerializeField] private LocalizedString mafiaDesString;
    [SerializeField] private TMP_Text jobDescriptionText;

    [Header("결과")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private LocalizeStringEvent winnerText;
    [SerializeField] private LocalizeStringEvent contaminationText;
    [SerializeField] private LocalizeStringEvent breakdownText;
    [SerializeField] private GameObject returnButton;
    [SerializeField] private GameObject waitHostText;
    private object[] _argsBuffer = new object[1]; // 한번만 생성하고 재활용하기 위한 버퍼
    private RoleManager _localRole;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        jobDisplayCanvas.SetActive(true);
        jobDisplayPanel.SetActive(false);
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

        if (_localRole != null) _localRole.RoleChanged -= OnLocalRoleChanged;
    }

    /// <summary>
    /// 타이머의 상태(Ready -> Playing)가 바뀔 때 호출되는 콜백
    /// </summary>
    private void OnTimerStateChanged(TimerState previousState, TimerState newState)
    {
        switch (newState)
        {
            case TimerState.Ready:
                // 5초 대기 시작 시점에 직업 패널을 키고 애니메이션 적용.
                jobDisplayPanel.SetActive(true);
                ShowLocalRole();
                break;

            case TimerState.Playing:
                // 3. 5초가 지나 본 게임 상태가 되면 직업 패널을 닫는다.
                if (jobDisplayCanvas != null)
                {
                    jobDisplayCanvas.SetActive(false);
                    jobDisplayPanel.SetActive(false);
                }
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
    /// <summary>
    /// 배정 목록은 서버에만 있으므로 내 플레이어의 RoleManager에서 직접 읽는다.
    /// 값이 늦게 복제될 수 있어 변경 이벤트도 같이 받는다.
    /// </summary>
    private void ShowLocalRole()
    {
        if (_localRole == null)
        {
            NetworkObject player = NetworkManager.Singleton.LocalClient?.PlayerObject;

            if (player != null && player.TryGetComponent(out RoleManager role))
            {
                _localRole = role;
                _localRole.RoleChanged += OnLocalRoleChanged;
            }
        }

        SetPlayerRoleNotification(_localRole != null && _localRole.CurrentRole == PlayerRole.Mafia);
    }

    private void OnLocalRoleChanged(PlayerRole role) => SetPlayerRoleNotification(role == PlayerRole.Mafia);

    public void SetPlayerRoleNotification(bool isMafia)
    {
        // 1. 해당 언어에 맞춰 번역된 직업 이름("마피아" 혹은 "Mafia")을 먼저 뽑아옵니다.
        string jobTableKey = isMafia ? "job_mafia" : "job_citizen";
        string translatedJobName = LocalizationSettings.StringDatabase.GetLocalizedString("InGame", jobTableKey);
        
        Color roleColor = isMafia ? mafiaColor : citizenColor;

        // 문장 안에서 직업 단어만 색을 입혀 눈에 먼저 들어오게 한다
        _argsBuffer[0] = $"<color=#{ColorUtility.ToHtmlStringRGB(roleColor)}>{translatedJobName}</color>";

        // 인자를 직접 넘겨야 {0}이 치환된다
        jobNotificationText.text = jobNotificationString.GetLocalizedString(_argsBuffer);

        if (roleAccent != null) roleAccent.color = roleColor;

        LocalizedString descriptionString = isMafia ? mafiaDesString : citizenDesString;
        jobDescriptionText.text = descriptionString.GetLocalizedString();
    }

    public void ShowResult(PlayerRole winner, float trash, float box, float paint, float mafiaWinRatio)
    {
        bool mafiaWon = winner == PlayerRole.Mafia;
        winnerText.StringReference.TableEntryReference = mafiaWon ? "result_mafia_win" : "result_citizen_win";
        winnerText.GetComponent<TMP_Text>().color = mafiaWon ? mafiaColor : citizenColor;
        SetArgs(contaminationText, Percent(trash + box + paint), Percent(mafiaWinRatio));
        SetArgs(breakdownText, Percent(trash), Percent(box), Percent(paint));

        jobDisplayCanvas.SetActive(true);
        returnButton.SetActive(IsServer);
        waitHostText.SetActive(!IsServer);
        resultPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static int Percent(float ratio) => Mathf.RoundToInt(ratio * 100f);

    private static void SetArgs(LocalizeStringEvent text, params object[] args)
    {
        text.StringReference.Arguments = args;
        text.RefreshString();
    }
}
