#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(PaintableSurface))]
[CanEditMultipleObjects]
public class PaintableSurfaceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(0.3f, 1.0f, 0.4f);
        if (GUILayout.Button("씬 내 모든 Surface ID 자동 재정렬", GUILayout.Height(35)))
        {
            ReassignAllSurfaceIds();
        }
        GUI.backgroundColor = Color.white;
    }

    [MenuItem("Tools/Paint System/Reassign All Surface IDs")]
    public static void ReassignAllSurfaceIds()
    {
        // 씬 내의 모든 PaintableSurface 검색
        PaintableSurface[] surfaces = Object.FindObjectsByType<PaintableSurface>(FindObjectsInactive.Include);

        if (surfaces == null || surfaces.Length == 0)
        {
            Debug.LogWarning("씬에서 PaintableSurface 컴포넌트를 찾을 수 없습니다.");
            return;
        }

        // 오브젝트 이름순 정렬
        System.Array.Sort(surfaces, (a, b) => string.Compare(a.gameObject.name, b.gameObject.name, System.StringComparison.Ordinal));

        // Undo(되돌리기) 등록
        Undo.RecordObjects(surfaces, "Reassign Surface IDs");

        for (int i = 0; i < surfaces.Length; i++)
        {
            int newId = i + 1; // 1부터 부여

            // [핵심 해결책] SerializedObject를 통해 개별 오브젝트의 직렬화 프로퍼티를 직접 수정
            SerializedObject serializedObj = new SerializedObject(surfaces[i]);
            SerializedProperty idProp = serializedObj.FindProperty("surfaceId");

            if (idProp != null)
            {
                idProp.intValue = newId;
                serializedObj.ApplyModifiedProperties(); // 변경사항 확정 및 저장
            }
            else
            {
                Debug.LogError($"{surfaces[i].name}에서 'surfaceId' 필드를 찾을 수 없습니다.");
            }
        }

        // 씬 저장 필요 상태로 전환
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"<color=cyan>[PaintSystem]</color> 총 {surfaces.Length}개의 PaintableSurface ID 재정렬 완료 (1 ~ {surfaces.Length})");
    }
}
#endif