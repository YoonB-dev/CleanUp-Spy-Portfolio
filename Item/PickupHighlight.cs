using UnityEngine;

public class PickupHighlight : MonoBehaviour
{
    [Header("Target Mesh Setup")]
    [SerializeField] private MeshRenderer targetRenderer;

    [Header("Outline Settings")]
    [SerializeField] private Color outlineColor = new(1f, 0.92f, 0.16f, 1f);
    [SerializeField] private float outlineWidth = 0.03f;
    [SerializeField] private float occludedAlpha = 0.3f;
    [SerializeField] private Shader outlineShader;

    private GameObject outlineObject;
    private Material outlineMaterialInstance;

    private void Awake()
    {
        BuildSingleOutline();
        SetHighlighted(false);
    }

    private void BuildSingleOutline()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<MeshRenderer>();
        }

        if (targetRenderer == null)
        {
            Debug.LogError($"{gameObject.name}: 아웃라인을 적용할 MeshRenderer를 찾을 수 없습니다.");
            return;
        }

        MeshFilter targetFilter = targetRenderer.GetComponent<MeshFilter>();
        if (targetFilter == null || targetFilter.sharedMesh == null) return;

        Shader shader = outlineShader != null ? outlineShader : Shader.Find("Custom/OutlineHull");
        if (shader == null)
        {
            Debug.LogWarning("PickupHighlight: OutlineHull 셰이더를 찾을 수 없습니다.");
            return;
        }

        // 1. 공용 아웃라인 머티리얼 인스턴스 1개 생성
        outlineMaterialInstance = new Material(shader);
        outlineMaterialInstance.SetColor("_OutlineColor", outlineColor);
        outlineMaterialInstance.SetFloat("_OutlineWidth", outlineWidth);
        outlineMaterialInstance.SetFloat("_OccludedAlpha", occludedAlpha);

        // 2. 자식 오브젝트 생성 및 트랜스폼 동기화
        outlineObject = new GameObject($"Outline_{targetRenderer.gameObject.name}_Dedicated");
        outlineObject.transform.SetParent(targetRenderer.transform, false);
        outlineObject.transform.localPosition = Vector3.zero;
        outlineObject.transform.localRotation = Quaternion.identity;
        outlineObject.transform.localScale = Vector3.one;

        MeshFilter outlineMeshFilter = outlineObject.AddComponent<MeshFilter>();
        outlineMeshFilter.sharedMesh = targetFilter.sharedMesh;

        MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();

        // -------------------------------------------------------------
        // 원본 MeshRenderer가 사용하는 머티리얼의 개수(서브메쉬 개수)를 구한다.
        // -------------------------------------------------------------
        int submeshCount = targetRenderer.sharedMaterials.Length;
        Material[] outlineMaterials = new Material[submeshCount];

        // 5개의 모든 슬롯에 아웃라인 머티리얼 인스턴스를 빈틈없이 채워 넣습니다.
        for (int i = 0; i < submeshCount; i++)
        {
            outlineMaterials[i] = outlineMaterialInstance;
        }

        // 배열을 통째로 자식 렌더러에 할당합니다.
        outlineRenderer.sharedMaterials = outlineMaterials;

        outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outlineRenderer.receiveShadows = false;
    }

    public void SetHighlighted(bool highlighted)
    {
        if (outlineObject != null)
        {
            outlineObject.SetActive(highlighted);
        }
    }

    private void OnDestroy()
    {
        if (outlineMaterialInstance != null)
        {
            Destroy(outlineMaterialInstance);
        }
    }
}