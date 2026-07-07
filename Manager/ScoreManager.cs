using TMPro;
using Unity.Netcode;
using UnityEngine;

public class ScoreManager : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI scoreText;
    public int CurrentScore => NetworkTrashScore.Value;
    private readonly NetworkVariable<int> NetworkTrashScore = new (
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 쓰레기통이 부여할 기본 점수
    private int _scoreTrashReward = 10;

    public override void OnNetworkSpawn()
    {
        NetworkTrashScore.OnValueChanged += OnScoreChanged;
        Debug.Log($"초기 점수: {NetworkTrashScore.Value}");
    }

    public override void OnNetworkDespawn()
    {
        NetworkTrashScore.OnValueChanged -= OnScoreChanged;
    }

    public void initScoreText()
    {
        if (scoreText != null)
        {
            scoreText.text = $"Score: {NetworkTrashScore.Value}";
        }
    }

    // 값이 변경되면 모든 클라이언트에서 이 함수가 실행됨
    private void OnScoreChanged(int previousValue, int newValue)
    {
        Debug.Log($"점수 변경됨! 이전: {previousValue} -> 현재: {newValue}");
        // 값이 변화하면 UI 업데이트.
        if (scoreText != null)
        {
            scoreText.text = $"Score: {newValue}";
        }
    }

    public void AddTrashScore()
    {
        // NetworkVariable 수정을 서버 전용으로 막아놨기 때문에, 안전장치를 걸어줍니다.
        if (!IsServer)
        {
            return;
        }
        // 기존 값에 점수를 더해줍니다. (자동으로 모든 클라이언트 동기화)
        NetworkTrashScore.Value += _scoreTrashReward;
    }
}
