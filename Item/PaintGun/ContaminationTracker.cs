using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 전체 오염도(%)를 서버 권위(Server Authoritative)로 관리하는 NetworkVariable.
/// RenderTexture의 실제 픽셀을 읽지 않고, 스트로크가 발생할 때마다 숫자로만 증감시켜서
/// 승패 판정/UI 표시에 GPU readback 없이 즉시 사용 가능하게 한다.
/// 씬에 미리 배치된 NetworkObject(예: GameManager)에 부착.
/// </summary>
public class ContaminationTracker : NetworkBehaviour
{
    public static ContaminationTracker Instance { get; private set; }

    [Header("Balance")]
    [SerializeField] private float paintAmountPerStroke = 0.5f;
    [SerializeField] private float cleanAmountPerStroke = 0.5f;
    [Tooltip("라운드 시작 시 기본으로 깔고 가는 초반 오염도")]
    [SerializeField] private float initialContamination = 10f;

    private readonly NetworkVariable<float> _contaminationLevel = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public float ContaminationLevel => _contaminationLevel.Value;

    /// <summary>오염도가 변경될 때마다 호출 (UI 바인딩용)</summary>
    public event System.Action<float> OnContaminationChanged;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        _contaminationLevel.OnValueChanged += HandleValueChanged;

        if (IsServer)
        {
            _contaminationLevel.Value = Mathf.Clamp(initialContamination, 0f, 100f);
        }
    }

    public override void OnNetworkDespawn()
    {
        _contaminationLevel.OnValueChanged -= HandleValueChanged;
    }

    private void HandleValueChanged(float previous, float current)
    {
        OnContaminationChanged?.Invoke(current);
    }

    /// <summary>
    /// 브러시가 적용될 때마다 로컬에서 호출됨. 서버가 아니면 아무 일도 하지 않는다.
    /// (모든 클라이언트에서 DrawAt이 실행되지만, 실질적인 값 반영은 서버 인스턴스에서만 일어남)
    /// </summary>
    public void OnBrushApplied(bool isPaint)
    {
        if (!IsServer) return;

        float delta = isPaint ? paintAmountPerStroke : -cleanAmountPerStroke;
        _contaminationLevel.Value = Mathf.Clamp(_contaminationLevel.Value + delta, 0f, 100f);
    }
}