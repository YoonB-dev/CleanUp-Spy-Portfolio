using UnityEngine;

public class PickupHighlight : MonoBehaviour
{
    [Header("Target Mesh Setup")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private MeshFilter targetMeshFilter;

    [Header("Outline Settings")]
    [SerializeField] private Color outlineColor = new(1f, 0.92f, 0.16f, 1f);
    [SerializeField] private float outlineWidth = 0.03f;
    [SerializeField] private float occludedAlpha = 0.3f;
    [SerializeField] private Shader outlineShader;

    // 자식 아웃라인 오브젝트 참조 (에디터/Awake에서 보장)
    [SerializeField] private MeshFilter outlineMeshFilter;
    [SerializeField] private MeshRenderer outlineRenderer;

    private Material outlineMaterialInstance;

    private void Awake()
    {
        InitComponents();
        BuildOutlineMaterial();

        if (targetMeshFilter != null && targetMeshFilter.sharedMesh != null)
        {
            Mesh targetMesh = targetMeshFilter.sharedMesh;
            RefreshOutlineMesh(targetMesh, targetMesh.subMeshCount);
        }
        
        SetHighlighted(false);
    }

    private void InitComponents()
    {
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<MeshRenderer>();
        if (targetMeshFilter == null && targetRenderer != null) targetMeshFilter = targetRenderer.GetComponent<MeshFilter>();

        // 자식 아웃라인 오브젝트가 없다면 Awake 시 1회 고정 구조로 보장
        if (outlineRenderer == null && targetRenderer != null)
        {
            Transform existingOutline = targetRenderer.transform.Find("DedicatedOutlineObject");
            GameObject outlineObj;

            if (existingOutline != null)
            {
                outlineObj = existingOutline.gameObject;
            }
            else
            {
                outlineObj = new GameObject("DedicatedOutlineObject");
                outlineObj.transform.SetParent(targetRenderer.transform, false);
                outlineObj.transform.localPosition = Vector3.zero;
                outlineObj.transform.localRotation = Quaternion.identity;
                outlineObj.transform.localScale = Vector3.one;
            }

            outlineMeshFilter = outlineObj.GetOrAddComponent<MeshFilter>();
            outlineRenderer = outlineObj.GetOrAddComponent<MeshRenderer>();
        }
    }

    private void BuildOutlineMaterial()
    {
        Shader shader = outlineShader != null ? outlineShader : Shader.Find("Custom/OutlineHull");
        if (shader == null || outlineRenderer == null) return;

        if (outlineMaterialInstance == null)
        {
            outlineMaterialInstance = new Material(shader);
            outlineMaterialInstance.SetColor("_OutlineColor", outlineColor);
            outlineMaterialInstance.SetFloat("_OutlineWidth", outlineWidth);
            outlineMaterialInstance.SetFloat("_OccludedAlpha", occludedAlpha);
        }

        outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outlineRenderer.receiveShadows = false;
    }

    /// <summary>
    /// TrashObject 등에서 메쉬가 변경되었을 때 외부에서 호출해 주는 핵심 함수
    /// </summary>
    public void RefreshOutlineMesh(Mesh newMesh, int submeshCount)
    {
        if (outlineMeshFilter == null || outlineRenderer == null) InitComponents();

        // 1. 메쉬 동기화
        outlineMeshFilter.sharedMesh = newMesh;

        // 2. 서브메쉬 개수에 맞게 머티리얼 배열 재할당
        if (outlineMaterialInstance != null)
        {
            Material[] outlineMaterials = new Material[submeshCount];
            for (int i = 0; i < submeshCount; i++)
            {
                outlineMaterials[i] = outlineMaterialInstance;
            }
            outlineRenderer.sharedMaterials = outlineMaterials;
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (outlineRenderer != null)
        {
            outlineRenderer.gameObject.SetActive(highlighted);
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

// GetOrAddComponent 확장 메서드 (유틸리티)
public static class ComponentExtensions
{
    public static T GetOrAddComponent<T>(this GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : go.AddComponent<T>();
    }
}