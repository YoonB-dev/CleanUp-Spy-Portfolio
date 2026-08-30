using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 기존 래그돌의 물리 셋업(리지드바디, 조인트, 콜라이더, 드라이버)을 같은 뼈 이름을 가진 새 모델로 이식
/// </summary>
public class RagdollTransplant : EditorWindow
{
    private Transform _source;
    private Transform _target;

    [MenuItem("Tools/Ragdoll/Transplant Physics")]
    private static void Open()
    {
        GetWindow<RagdollTransplant>("Ragdoll Transplant");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Source: 기존 CleanerRobotRagdoll 씬 인스턴스\n" +
            "Target: 새 모델 씬 인스턴스\n" +
            "둘 다 씬에 올린 뒤 지정하고 Transplant. 끝나면 Target을 Project로 드래그해 프리팹 저장.",
            MessageType.Info);

        _source = (Transform)EditorGUILayout.ObjectField(
            "Source (old)", _source, typeof(Transform), true);
        _target = (Transform)EditorGUILayout.ObjectField(
            "Target (new)", _target, typeof(Transform), true);

        GUI.enabled = _source != null && _target != null;
        if (GUILayout.Button("Transplant"))
        {
            Transplant();
        }
        GUI.enabled = true;
    }

    private void Transplant()
    {
        Undo.RegisterFullObjectHierarchyUndo(_target.gameObject, "Ragdoll Transplant");

        Dictionary<string, Transform> targetMap = BuildNameMap(_target);

        // 대상의 기존 물리/드라이버 컴포넌트 제거
        foreach (Transform t in _target.GetComponentsInChildren<Transform>(true))
        {
            RemovePhysics(t.gameObject);
        }

        RemoveScripts(_target.gameObject);

        // 리지드바디와 콜라이더 복사
        foreach (Transform src in _source.GetComponentsInChildren<Transform>(true))
        {
            if (src.GetComponent<Rigidbody>() == null && src.GetComponent<Collider>() == null)
            {
                continue;
            }

            Transform dst = ResolveTarget(src, targetMap);
            if (dst == null)
            {
                continue;
            }

            Rigidbody srcRigidbody = src.GetComponent<Rigidbody>();
            if (srcRigidbody != null)
            {
                ComponentUtility.CopyComponent(srcRigidbody);
                ComponentUtility.PasteComponentAsNew(dst.gameObject);
            }

            foreach (Collider srcCollider in src.GetComponents<Collider>())
            {
                ComponentUtility.CopyComponent(srcCollider);
                ComponentUtility.PasteComponentAsNew(dst.gameObject);
            }
        }

        // 조인트 복사 후 connectedBody를 같은 이름의 새 리지드바디로 재매핑
        foreach (Transform src in _source.GetComponentsInChildren<Transform>(true))
        {
            ConfigurableJoint srcJoint = src.GetComponent<ConfigurableJoint>();
            if (srcJoint == null || !targetMap.TryGetValue(src.name, out Transform dst))
            {
                continue;
            }

            ComponentUtility.CopyComponent(srcJoint);
            ComponentUtility.PasteComponentAsNew(dst.gameObject);

            ConfigurableJoint dstJoint = dst.GetComponent<ConfigurableJoint>();
            if (srcJoint.connectedBody != null &&
                targetMap.TryGetValue(srcJoint.connectedBody.name, out Transform connected))
            {
                dstJoint.connectedBody = connected.GetComponent<Rigidbody>();
            }
        }

        // 루트 스크립트 복사
        RagdollDriver srcDriver = _source.GetComponent<RagdollDriver>();
        if (srcDriver != null)
        {
            ComponentUtility.CopyComponent(srcDriver);
            ComponentUtility.PasteComponentAsNew(_target.gameObject);
        }

        foreach (MonoBehaviour srcScript in _source.GetComponents<MonoBehaviour>())
        {
            if (srcScript is RagdollDriver)
            {
                continue;
            }

            ComponentUtility.CopyComponent(srcScript);
            ComponentUtility.PasteComponentAsNew(_target.gameObject);
        }

        RagdollDriver dstDriver = _target.GetComponent<RagdollDriver>();
        if (dstDriver != null)
        {
            if (targetMap.TryGetValue("Hips", out Transform hips))
            {
                dstDriver.Hips = hips;
            }

            if (targetMap.TryGetValue("Head", out Transform head))
            {
                dstDriver.Head = head;
            }
        }

        EditorUtility.SetDirty(_target.gameObject);
        Debug.Log("[RagdollTransplant] 이식 완료");
    }

    private static Dictionary<string, Transform> BuildNameMap(Transform root)
    {
        var map = new Dictionary<string, Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            map[t.name] = t;
        }

        return map;
    }

    // 대상에 같은 이름 노드가 없으면 소스 부모와 같은 이름 아래에 생성
    private Transform ResolveTarget(Transform src, Dictionary<string, Transform> targetMap)
    {
        if (targetMap.TryGetValue(src.name, out Transform existing))
        {
            return existing;
        }

        if (src.parent == null || !targetMap.TryGetValue(src.parent.name, out Transform parent))
        {
            Debug.LogWarning($"[RagdollTransplant] '{src.name}' 부모를 대상에서 못 찾아 건너뜀");
            return null;
        }

        GameObject created = new GameObject(src.name);
        created.transform.SetParent(parent, false);
        created.transform.SetLocalPositionAndRotation(src.localPosition, src.localRotation);
        created.transform.localScale = src.localScale;
        created.layer = src.gameObject.layer;
        targetMap[src.name] = created.transform;
        return created.transform;
    }

    private static void RemovePhysics(GameObject go)
    {
        foreach (ConfigurableJoint joint in go.GetComponents<ConfigurableJoint>())
        {
            DestroyImmediate(joint);
        }

        foreach (Rigidbody rigidbody in go.GetComponents<Rigidbody>())
        {
            DestroyImmediate(rigidbody);
        }

        foreach (Collider col in go.GetComponents<Collider>())
        {
            DestroyImmediate(col);
        }
    }

    private static void RemoveScripts(GameObject root)
    {
        RagdollPoser poser = root.GetComponent<RagdollPoser>();
        if (poser != null)
        {
            DestroyImmediate(poser);
        }

        RagdollDriver driver = root.GetComponent<RagdollDriver>();
        if (driver != null)
        {
            DestroyImmediate(driver);
        }
    }
}
