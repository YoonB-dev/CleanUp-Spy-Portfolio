using Unity.Netcode;
using UnityEngine;

public class TrashObject : NetworkBehaviour
{
    [Header("Components")]
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private MeshCollider meshCollider;

    [Header("1. 에디터 맵 배치용 (직접 지정)")]
    [SerializeField] private TrashData defaultTrashData;

    [Header("2. 동기화용 데이터베이스")]
    [SerializeField] private TrashData[] trashDatabase;

    [Header("3. 상호작용 하이라이트")]
    [SerializeField] private PickupHighlight pickupHighlight;

    // 쓰레기 종류의 인덱스를 동기화 (기본값 -1)
    private NetworkVariable<int> trashDataIndex = new NetworkVariable<int>(-1);

    public TrashData Data
    {
        get
        {
            if (trashDatabase != null &&
                trashDataIndex.Value >= 0 &&
                trashDataIndex.Value < trashDatabase.Length)
            {
                return trashDatabase[trashDataIndex.Value];
            }
            return defaultTrashData;
        }
    }

    #region Unity Editor
    private void OnValidate()
    {
        if (!Application.isPlaying && defaultTrashData != null)
        {
            ApplyTrashDataSO(defaultTrashData);
        }
    }
    #endregion

    public override void OnNetworkSpawn()
    {
        // 1. 이벤트 중복 등록 제거 (1회만 등록)
        trashDataIndex.OnValueChanged += OnTrashDataChanged;

        // 2. 맵에 직접 배치된 기본 쓰레기 (서버 판정)
        if (IsServer && defaultTrashData != null && trashDataIndex.Value == -1)
        {
            int index = System.Array.IndexOf(trashDatabase, defaultTrashData);
            if (index != -1)
            {
                trashDataIndex.Value = index;
            }
        }

        // 3. 스폰 순간 이미 동기화된 인덱스가 있다면 즉시 적용 (클라이언트 대응)
        if (trashDataIndex.Value >= 0 && trashDataIndex.Value < trashDatabase.Length)
        {
            ApplyTrashDataSO(trashDatabase[trashDataIndex.Value]);
        }
        else if (defaultTrashData != null)
        {
            ApplyTrashDataSO(defaultTrashData);
        }
    }

    public override void OnNetworkDespawn()
    {
        trashDataIndex.OnValueChanged -= OnTrashDataChanged;
    }

    // --- 서버 전용 초기화 ---
    public void ServerInitialize(TrashData data)
    {
        if (!IsServer || data == null) return;

        // 1. 전달받은 TrashData가 내 데이터베이스(trashDatabase)의 몇 번째 인덱스인지 조회
        int targetIndex = System.Array.IndexOf(trashDatabase, data);

        // 2. 데이터베이스에 존재하는 데이터라면 해당 로컬 인덱스로 동기화
        if (targetIndex != -1)
        {
            trashDataIndex.Value = targetIndex;
            ApplyTrashDataByIndex(targetIndex);
        }
        else
        {
            Debug.LogWarning($"[TrashObject] {data.name} 데이터가 trashDatabase에 존재하지 않습니다.");
        }
    }

    // --- NetworkVariable 변경 콜백 ---
    private void OnTrashDataChanged(int previousValue, int newValue)
    {
        ApplyTrashDataByIndex(newValue);
    }

    private void ApplyTrashDataByIndex(int index)
    {
        if (trashDatabase == null || index < 0 || index >= trashDatabase.Length) return;
        ApplyTrashDataSO(trashDatabase[index]);
    }

    // --- 실제 3D 모델 / 재질 / 콜라이더 교체 ---
    private void ApplyTrashDataSO(TrashData data)
    {
        if (data == null) return;

        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
        if (pickupHighlight == null) pickupHighlight = GetComponent<PickupHighlight>();

        // 1. Mesh 교체
        if (meshFilter != null) meshFilter.sharedMesh = data.trashMesh;
        // 2. Material 교체
        if (meshRenderer != null && data.trashMaterial != null && data.trashMesh != null)
        {
            int subMeshCount = data.trashMesh.subMeshCount;

            // SubMesh 개수만큼 배열 생성
            Material[] newMaterials = new Material[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
            {
                newMaterials[i] = data.trashMaterial;
            }

            // materials 프로퍼티에 배열을 통째로 할당
            meshRenderer.materials = newMaterials;
        }
        // 3. Collider 교체 (MeshCollider인 경우)
        if (meshCollider != null && data.trashMesh != null)
        {
            meshCollider.sharedMesh = data.trashMesh;
        }
        // 4. Scale 교체
        if (data.modelScale != Vector3.zero)
        {
            transform.localScale = data.modelScale;
        }

        // 4. 하이라이트용 Mesh 갱신
        if (pickupHighlight != null && data.trashMesh != null && meshRenderer != null)
        {
            int submeshCount = data.trashMesh.subMeshCount;
            pickupHighlight.RefreshOutlineMesh(data.trashMesh, submeshCount);
        }
    }
}