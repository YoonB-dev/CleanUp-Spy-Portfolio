using TMPro;
using Unity.Netcode;
using UnityEngine;

public class ScoreManager : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI totalScoreText;
    [SerializeField] private TextMeshProUGUI trashScoreText;
    [SerializeField] private TextMeshProUGUI boxScoreText;
    public int CurrentTotalScore => networkTrashScore.Value + networkPlacedBoxScore.Value;
    // 쓰레기 버리기 점수
    private readonly NetworkVariable<int> networkTrashScore = new (
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    // 배치된 박스 점수
    private readonly NetworkVariable<int> networkPlacedBoxScore = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 쓰레기통이 부여할 기본 점수
    private const int SCORE_TRASH_REWARD = 10;
    // 배치된 박스가 부여할 기본 점수
    private const int SCORE_BOX_REWARD = 10;

    public override void OnNetworkSpawn()
    {
        networkTrashScore.OnValueChanged += OnScoreChanged;
        networkPlacedBoxScore.OnValueChanged += OnScoreChanged;
    }

    public override void OnNetworkDespawn()
    {
        networkTrashScore.OnValueChanged -= OnScoreChanged;
    }

    public void InitScoreText()
    {
        if (totalScoreText != null)
        {
            totalScoreText.text = $"Total: {CurrentTotalScore}";
        }
        if (trashScoreText != null)
        {
            trashScoreText.text = $"Trash: {networkTrashScore.Value}";
        }
        if (boxScoreText != null)
        {
            boxScoreText.text = $"Box: {networkPlacedBoxScore.Value}";
        } 
    }

    // 값이 변경되면 모든 클라이언트에서 이 함수가 실행됨
    private void OnScoreChanged(int previousValue, int newValue)
    {
        Debug.Log($"점수 변경됨! 이전: {previousValue} -> 현재: {newValue}");
        // 값이 변화하면 UI 업데이트.
        // if (scoreText != null)
        // {
        //     scoreText.text = $"Score: {newValue}";
        // }
    }

    public void AddTrashScore()
    {
        // NetworkVariable 수정을 서버 전용으로 막아놨기 때문에, 안전장치를 걸어줍니다.
        if (!IsServer)
        {
            return;
        }
        // 기존 값에 점수를 더해줍니다. (자동으로 모든 클라이언트 동기화)
        networkTrashScore.Value += SCORE_TRASH_REWARD;
    }
}
