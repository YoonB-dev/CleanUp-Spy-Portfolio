using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 점수 관련 클래스로 데이터와 UI를 관리한다.
/// </summary>
public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance { get; private set; }
    [SerializeField] private TextMeshProUGUI totalScoreText;
    [SerializeField] private TextMeshProUGUI trashScoreText;
    [SerializeField] private TextMeshProUGUI boxScoreText;
    [SerializeField] private TextMeshProUGUI paintScoreText;
    public int CurrentTotalScore => _networkTrashScore.Value + _networkPlacedBoxScore.Value;
    // =====쓰레기 버리기 점수=====
    private readonly NetworkVariable<int> _networkTrashScore = new (
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    // =====배치된 박스 점수=====
    private readonly NetworkVariable<int> _networkPlacedBoxScore = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private const int SCORE_BOX_REWARD = 10;// 배치된 박스가 부여할 기본 점수

    // =====페인트 점수=====
    private readonly NetworkVariable<float> _contaminationLevel = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    public float ContaminationLevel => _contaminationLevel.Value;
    private const float SCORE_PAINT = 0.5f; // 배치된 박스가 부여할 기본 점수
    private const float SCORE_CLEAR_PAINT = 0.5f; 


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
        _networkTrashScore.OnValueChanged += OnScoreChanged;
        _networkPlacedBoxScore.OnValueChanged += OnScoreChanged;
        _contaminationLevel.OnValueChanged += OnContaminationChanged;
    }

    public override void OnNetworkDespawn()
    {
        _networkTrashScore.OnValueChanged -= OnScoreChanged;
        _networkPlacedBoxScore.OnValueChanged -= OnScoreChanged;
        _contaminationLevel.OnValueChanged -= OnContaminationChanged;
    }
    public void InitScoreText()
    {
        if (totalScoreText != null)
        {
            totalScoreText.text = $"Total: {CurrentTotalScore}";
        }
        if (trashScoreText != null)
        {
            trashScoreText.text = $"Trash: {_networkTrashScore.Value}";
        }
        if (boxScoreText != null)
        {
            boxScoreText.text = $"Box: {_networkPlacedBoxScore.Value}";
        } 
        if (paintScoreText != null)
        {
            paintScoreText.text = $"Paint: {_contaminationLevel.Value}";
        }
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
        if (totalScoreText != null)
        {
            totalScoreText.text = $"Total: {CurrentTotalScore}";
        }
        if (trashScoreText != null)
        {
            trashScoreText.text = $"Trash: {_networkTrashScore.Value}";
        }
        if (boxScoreText != null)
        {
            boxScoreText.text = $"Box: {_networkPlacedBoxScore.Value}";
        }
        if (paintScoreText != null)
        {
            paintScoreText.text = $"Paint: {_contaminationLevel.Value}";
        }
    }
    /// 일반 쓰레기 점수
    public void AddTrashScore(int score = 10)
    {
        if (!IsServer)
        {
            return;
        }
        _networkTrashScore.Value += score;
    }

    //상자가 배치되었을 때 점수를 누적
    public void AddBoxScore()
    {
        if (!IsServer)
        {
            return;
        }
        _networkPlacedBoxScore.Value += SCORE_BOX_REWARD;
    }

    //상자가 무너지거나 다시 주워졌을 때 점수를 차감
    public void SubtractBoxScore()
    {
        if (!IsServer)
        {
            return;
        }

        // 점수가 음수로 내려가는 예외 방어
        if (_networkPlacedBoxScore.Value >= SCORE_BOX_REWARD)
        {
            _networkPlacedBoxScore.Value -= SCORE_BOX_REWARD;
        }
        else
        {
            _networkPlacedBoxScore.Value = 0;
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
        _contaminationLevel.Value = Mathf.Clamp(averageContamination, 0f, 100f);
    }
}
