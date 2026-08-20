using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 점수 관련 클래스로 데이터와 UI를 관리한다.
/// </summary>
public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance { get; private set; }
    [Header("Gauge UI Reference")]
    [SerializeField] private UIPropertyGauge propertyGauge;

    // =====전체 쓰레기 점수 (네트워크 동기화 필요 시 사용)=====
    private readonly NetworkVariable<int> _networkTotalTrashScore = new(
        50,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    // =====쓰레기 버리기 점수 (청소해서 제거한 점수)=====
    private readonly NetworkVariable<int> _networkCleanedTrashScore = new (
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    // =====전체 배치 가능한 상자 개수 (네트워크 동기화 필요 시 사용)=====
    private readonly NetworkVariable<int> _networkTotalBoxCount = new(
        10,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    // =====배치된 박스 개수=====
    private readonly NetworkVariable<int> _networkPlacedBoxCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // =====페인트 점수=====
    private readonly NetworkVariable<float> _contaminationLevel = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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
        _networkTotalTrashScore.OnValueChanged += OnScoreChanged;
        _networkCleanedTrashScore.OnValueChanged += OnScoreChanged;
        _networkTotalBoxCount.OnValueChanged += OnScoreChanged;
        _networkPlacedBoxCount.OnValueChanged += OnScoreChanged;
        _contaminationLevel.OnValueChanged += OnContaminationChanged;

        if (IsServer)
        {
            // _networkTotalBoxCount.Value = defaultTotalBoxCount;
            // _networkTotalTrashScore.Value = defaultTotalTrashScore;
        }

        UpdateScoreUI();
    }

    public override void OnNetworkDespawn()
    {
        _networkTotalTrashScore.OnValueChanged -= OnScoreChanged;
        _networkCleanedTrashScore.OnValueChanged -= OnScoreChanged;
        _networkTotalBoxCount.OnValueChanged -= OnScoreChanged;
        _networkPlacedBoxCount.OnValueChanged -= OnScoreChanged;
        _contaminationLevel.OnValueChanged -= OnContaminationChanged;
    }
    public void InitScoreText()
    {
        UpdateScoreUI();
    }

    // 값이 변경되면 모든 클라이언트에서 이 함수가 실행됨
    private void OnScoreChanged(int previousValue, int newValue)
    {
        UpdateScoreUI();
    }
    private void OnContaminationChanged(float previousValue, float newValue)
    {
        UpdateScoreUI();
    }
    private void UpdateScoreUI()
    {
        if (propertyGauge != null)
        {
            // 1. 미배치 박스 수 = (전체 배치 가능 수 - 현재 배치된 수)
            int unplacedBoxCount = Mathf.Max(0, _networkTotalBoxCount.Value - _networkPlacedBoxCount.Value);

            // 2. 남은 쓰레기 점수 = (전체 쓰레기 총점 - 청소한 쓰레기 점수)
            float remainingTrashScore = Mathf.Max(0, _networkTotalTrashScore.Value - _networkCleanedTrashScore.Value);

            propertyGauge.CalculateGaugeValues(
                remainingTrashScore,
                unplacedBoxCount,
                _contaminationLevel.Value
            );
        }
    }
    /// <summary>
    /// 맵 초기화 시 씬에 존재하는 전체 쓰레기 점수 및 배치 가능한 상자 개수를 설정
    /// </summary>
    public void SetTotalTrashScore(int totalScore)
    {
        if (!IsServer) return;
        _networkTotalTrashScore.Value = totalScore;
    }
    public void SetTotalBoxCount(int count)
    {
        if (!IsServer) return;
        _networkTotalBoxCount.Value = count;
    }
    /// <summary>
    /// 마피아 능력 등으로 새로운 쓰레기가 생성되었을 때 전체 쓰레기 점수를 누적 (서버 전용)
    /// </summary>
    public void AddTotalTrashScore(int score)
    {
        if (!IsServer) return;
        _networkTotalTrashScore.Value += score;
    }
    /// <summary>
    /// 쓰레기를 치웠을 때 청소 점수 누적
    /// </summary>
    public void AddTrashScore(int score = 10)
    {
        if (!IsServer)
        {
            return;
        }
        _networkCleanedTrashScore.Value += score;
    }

    //상자가 배치되었을 때 점수를 누적
    public void AddBoxScore()
    {
        if (!IsServer)
        {
            return;
        }
        _networkPlacedBoxCount.Value ++;
    }

    //상자가 무너지거나 다시 주워졌을 때 점수를 차감
    public void SubtractBoxScore()
    {
        if (!IsServer)
        {
            return;
        }

        // 점수가 음수로 내려가는 예외 방어
        if (_networkPlacedBoxCount.Value >= 1)
        {
            _networkPlacedBoxCount.Value --;
        }
        else
        {
            _networkPlacedBoxCount.Value = 0;
        }
    }

    /// <summary>
    /// 표면 하나의 오염도 계산이 끝날 때마다 PaintSurfaceManager가 호출.
    /// PaintSurfaceManager가 이미 들고 있는 등록 목록을 재사용해 씬 전체를 매번 다시 스캔하지 않도록 함.
    /// </summary>
    public void RecalculateTotalContamination()
    {
        if (!IsServer) return;
        if (PaintSurfaceManager.Instance == null) return;

        float sumPercent = 0f;
        int count = 0;

        foreach (var surface in PaintSurfaceManager.Instance.AllSurfaces)
        {
            sumPercent += surface.ContaminationPercent;
            count++;
        }

        if (count == 0) return;

        float averageContamination = sumPercent / count;
        // 소수점 2자리로 반올림해서 동기화 (불필요한 NetworkVariable 갱신도 줄어듦)
        averageContamination = Mathf.Round(averageContamination * 100f) / 100f;
        _contaminationLevel.Value = Mathf.Clamp(averageContamination, 0f, 100f) * 100; // 페인트의 점수를 확산
    }
}
