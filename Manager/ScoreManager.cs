using TMPro;
using Unity.Netcode;
using UnityEngine;

public class ScoreManager : NetworkBehaviour
{
    public static ScoreManager Instance { get; private set; }
    [SerializeField] private TextMeshProUGUI totalScoreText;
    [SerializeField] private TextMeshProUGUI trashScoreText;
    [SerializeField] private TextMeshProUGUI boxScoreText;
    public int CurrentTotalScore => _networkTrashScore.Value + _networkPlacedBoxScore.Value;
    // 쓰레기 버리기 점수
    private readonly NetworkVariable<int> _networkTrashScore = new (
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    // 배치된 박스 점수
    private readonly NetworkVariable<int> _networkPlacedBoxScore = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 쓰레기통이 부여할 기본 점수
    private const int SCORE_TRASH_REWARD = 10;
    // 배치된 박스가 부여할 기본 점수
    private const int SCORE_BOX_REWARD = 10;
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
    }

    public override void OnNetworkDespawn()
    {
        _networkTrashScore.OnValueChanged -= OnScoreChanged;
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
    }

    // 값이 변경되면 모든 클라이언트에서 이 함수가 실행됨
    private void OnScoreChanged(int previousValue, int newValue)
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
    }
    /// 일반 쓰레기 점수
    public void AddTrashScore()
    {
        if (!IsServer)
        {
            return;
        }
        _networkTrashScore.Value += SCORE_TRASH_REWARD;
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
}
