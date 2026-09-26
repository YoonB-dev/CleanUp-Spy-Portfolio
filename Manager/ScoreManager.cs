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

    [Header("오염도 밸런스 (승패 판정과 게이지가 같이 쓴다)")]
    [Tooltip("오염도 100%에 해당하는 목표 총점")]
    [SerializeField] private float maxContaminationScore = 1000f;
    [Tooltip("배치되지 않은 박스 1개당 환산 점수")]
    [SerializeField] private float scorePerBox = 50f;

    // =====전체 쓰레기 점수 (네트워크 동기화 필요 시 사용)=====
    private readonly NetworkVariable<int> _networkTotalTrashScore = new(
        0, // 서버가 스폰 시 맵에 배치된 쓰레기로 계산한다 (InitializeTotalTrashScore)
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
        0, // 서버가 스폰 시 맵에 있는 상자로 계산한다 (InitializeBoxCount)
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

    // =====배율이 적용된 최종 오염 점수 (서버가 계산해 모든 클라이언트에 전달, 승패 판정 기준)=====
    private readonly NetworkVariable<float> _trashContamination = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<float> _boxContamination = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<float> _paintContamination = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 방장이 설정한 오염도 배율. 서버만 계산에 쓰므로 동기화하지 않는다
    private float _trashMultiplier = 1f;
    private float _boxMultiplier = 1f;
    private float _paintMultiplier = 1f;

    public float TrashContamination => _trashContamination.Value;
    public float BoxContamination => _boxContamination.Value;
    public float PaintContamination => _paintContamination.Value;
    public float TotalContamination => TrashContamination + BoxContamination + PaintContamination;
    public float MaxContaminationScore => maxContaminationScore;

    /// <summary>목표 총점 대비 현재 오염도. 1 이상이면 목표치에 도달</summary>
    public float ContaminationRatio => maxContaminationScore > 0f ? TotalContamination / maxContaminationScore : 0f;

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
        _trashContamination.OnValueChanged += OnWeightedScoreChanged;
        _boxContamination.OnValueChanged += OnWeightedScoreChanged;
        _paintContamination.OnValueChanged += OnWeightedScoreChanged;

        if (IsServer)
        {
            // RoomSettings는 서버에만 확실히 남아 있으므로 서버가 읽어 둔다
            // 배율은 게임 시작 시점의 방 설정으로 고정한다 (배율 변경 UI는 로비에만 있어 게임 중에는 바뀌지 않음)
            if (RoomSettings.Instance != null)
            {
                _trashMultiplier = RoomSettings.Instance.TrashContaminationMultiplier.Value;
                _boxMultiplier = RoomSettings.Instance.BoxContaminationMultiplier.Value;
                _paintMultiplier = RoomSettings.Instance.PaintContaminationMultiplier.Value;
            }
            InitializeTotalTrashScore();
            InitializeBoxCount();
            RecalculateWeightedScores();

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
        _trashContamination.OnValueChanged -= OnWeightedScoreChanged;
        _boxContamination.OnValueChanged -= OnWeightedScoreChanged;
        _paintContamination.OnValueChanged -= OnWeightedScoreChanged;
    }
    public void InitScoreText()
    {
        UpdateScoreUI();
    }

    // 원본 수치가 바뀌면 서버가 최종 점수를 다시 계산한다. 클라이언트는 최종 점수가 도착할 때 UI를 갱신
    private void OnScoreChanged(int previousValue, int newValue)
    {
        if (IsServer) RecalculateWeightedScores();
    }
    private void OnContaminationChanged(float previousValue, float newValue)
    {
        if (IsServer) RecalculateWeightedScores();
    }
    private void OnWeightedScoreChanged(float previousValue, float newValue)
    {
        UpdateScoreUI();
    }

    /// <summary>
    /// 원본 수치에 배율을 곱해 최종 오염 점수를 만든다 (서버 전용).
    /// </summary>
    private void RecalculateWeightedScores()
    {
        if (!IsServer) return;

        // 1. 미배치 박스 수 = (전체 배치 가능 수 - 현재 배치된 수)
        int unplacedBoxCount = Mathf.Max(0, _networkTotalBoxCount.Value - _networkPlacedBoxCount.Value);

        // 2. 남은 쓰레기 점수 = (전체 쓰레기 총점 - 청소한 쓰레기 점수)
        float remainingTrashScore = Mathf.Max(0, _networkTotalTrashScore.Value - _networkCleanedTrashScore.Value);

        // 3. 방장이 설정한 오염도 배율 적용
        _trashContamination.Value = remainingTrashScore * _trashMultiplier;
        _boxContamination.Value = unplacedBoxCount * scorePerBox * _boxMultiplier;
        _paintContamination.Value = _contaminationLevel.Value * _paintMultiplier;

        // 값이 그대로면 OnValueChanged가 안 불리므로 서버 쪽 UI는 직접 갱신
        UpdateScoreUI();
    }

    private void UpdateScoreUI()
    {
        if (propertyGauge != null)
        {
            propertyGauge.CalculateGaugeValues(TrashContamination, BoxContamination, PaintContamination, maxContaminationScore);
        }
    }
    /// <summary>
    /// 게임 시작 시 맵에 배치된 쓰레기 점수를 합산해 전체 쓰레기 점수로 설정 (서버 전용).
    /// 이후 마피아가 만드는 쓰레기는 AddTotalTrashScore로 따로 더해진다.
    /// </summary>
    private void InitializeTotalTrashScore()
    {
        if (!IsServer) return;

        int total = 0;
        foreach (TrashObject trash in FindObjectsByType<TrashObject>(FindObjectsInactive.Exclude))
        {
            // TrashData가 없는 쓰레기는 쓰레기통에서도 기본 점수 10으로 처리하므로 똑같이 센다
            total += trash.Data != null ? trash.Data.score : 10;
        }

        _networkTotalTrashScore.Value = total;
        Debug.Log($"[ScoreManager] 맵에 배치된 쓰레기 점수 합계: {total}");
    }

    /// <summary>
    /// 게임 시작 시 맵에 있는 상자를 세어 전체 상자 수와 이미 배치된 상자 수를 설정 (서버 전용).
    /// 미배치 상자 수(전체 - 배치)가 오염도에 반영된다.
    /// </summary>
    private void InitializeBoxCount()
    {
        if (!IsServer) return;

        int total = 0;
        int placed = 0;
        foreach (PlaceableBox box in FindObjectsByType<PlaceableBox>(FindObjectsInactive.Exclude))
        {
            total++;
            if (box.IsPlaced) placed++;
        }

        _networkTotalBoxCount.Value = total;
        _networkPlacedBoxCount.Value = placed;
        Debug.Log($"[ScoreManager] 맵의 상자: 전체 {total}개, 배치됨 {placed}개, 미배치 {total - placed}개");
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
