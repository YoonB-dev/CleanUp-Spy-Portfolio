using UnityEngine;

public class PickupHighlight : MonoBehaviour
{
    [SerializeField] private Color outlineColor = new(1f, 0.92f, 0.16f, 1f);
    [SerializeField] private float outlinePadding = 0.03f;
    [SerializeField] private float outlineWidth = 0.015f;

    private GameObject outlineRoot;
    private LineRenderer[] edgeRenderers;
    private Collider targetCollider;

    private void Awake()
    {
        targetCollider = GetComponentInChildren<Collider>();
        BuildOutline();
        SetHighlighted(false);
    }

    private void LateUpdate()
    {
        if (outlineRoot == null || !outlineRoot.activeSelf)
        {
            return;
        }

        UpdateOutlinePositions();
    }

    public void SetHighlighted(bool highlighted)
    {
        if (outlineRoot != null)
        {
            outlineRoot.SetActive(highlighted);

            if (highlighted)
            {
                UpdateOutlinePositions();
            }
        }
    }

    private void BuildOutline()
    {
        if (outlineRoot != null)
        {
            return;
        }

        if (targetCollider == null)
        {
            return;
        }

        outlineRoot = new GameObject("Outline");
        outlineRoot.transform.SetParent(transform, false);
        outlineRoot.transform.localPosition = Vector3.zero;
        outlineRoot.transform.localRotation = Quaternion.identity;
        outlineRoot.transform.localScale = Vector3.one;

        edgeRenderers = new LineRenderer[12];

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material outlineMaterial = null;
        if (shader != null)
        {
            outlineMaterial = new Material(shader);
            outlineMaterial.color = outlineColor;
        }

        for (int index = 0; index < edgeRenderers.Length; index++)
        {
            GameObject edgeObject = new GameObject($"Edge_{index}");
            edgeObject.transform.SetParent(outlineRoot.transform, false);

            LineRenderer lineRenderer = edgeObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.loop = false;
            lineRenderer.startWidth = outlineWidth;
            lineRenderer.endWidth = outlineWidth;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.numCapVertices = 2;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.alignment = LineAlignment.View;

            if (outlineMaterial != null)
            {
                lineRenderer.material = outlineMaterial;
            }

            edgeRenderers[index] = lineRenderer;
        }

        UpdateOutlinePositions();
    }

    private void UpdateOutlinePositions()
    {
        if (targetCollider == null || edgeRenderers == null || edgeRenderers.Length != 12)
        {
            return;
        }

        Bounds bounds = targetCollider.bounds;
        bounds.Expand(outlinePadding);

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        Vector3 p000 = new(min.x, min.y, min.z);
        Vector3 p001 = new(min.x, min.y, max.z);
        Vector3 p010 = new(min.x, max.y, min.z);
        Vector3 p011 = new(min.x, max.y, max.z);
        Vector3 p100 = new(max.x, min.y, min.z);
        Vector3 p101 = new(max.x, min.y, max.z);
        Vector3 p110 = new(max.x, max.y, min.z);
        Vector3 p111 = new(max.x, max.y, max.z);

        SetEdge(0, p000, p001);
        SetEdge(1, p001, p011);
        SetEdge(2, p011, p010);
        SetEdge(3, p010, p000);

        SetEdge(4, p100, p101);
        SetEdge(5, p101, p111);
        SetEdge(6, p111, p110);
        SetEdge(7, p110, p100);

        SetEdge(8, p000, p100);
        SetEdge(9, p001, p101);
        SetEdge(10, p010, p110);
        SetEdge(11, p011, p111);
    }

    private void SetEdge(int index, Vector3 start, Vector3 end)
    {
        if (edgeRenderers == null || index < 0 || index >= edgeRenderers.Length || edgeRenderers[index] == null)
        {
            return;
        }

        edgeRenderers[index].SetPosition(0, start);
        edgeRenderers[index].SetPosition(1, end);
    }
}