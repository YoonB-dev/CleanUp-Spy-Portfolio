using UnityEngine;
using UnityEditor;
using System.Linq;

/// <summary>
/// 래그돌 스프링을 Play 중에 실시간으로 조절한다.
/// 1번 메뉴(변환)는 CharacterJoint가 있어야 도는데 이미 변환한 뒤엔 없으므로,
/// 변환 후 튜닝은 이 창으로 한다.
/// </summary>
public class RagdollTuner : EditorWindow
{
    // 2026-07-17 실험으로 찾은 값
    float spring = 400f;
    float damper = 30f;
    float maxForce = 10000f;

    [MenuItem("Tools/CleanUp Mafia/스프링 튜너 (Play 중 실시간)", false, 41)]
    static void Open() => GetWindow<RagdollTuner>("래그돌 튜너");

    // Play 중에도 창이 갱신되게
    void OnInspectorUpdate() => Repaint();

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Play 중에 슬라이더를 움직이면 바로 반영된다.\n" +
            "Spring = 자세를 되돌리려는 힘. 낮출수록 흐물흐물해진다.\n" +
            "Damper = 출렁임 감쇠. 너무 낮으면 부들부들 떤다.", MessageType.Info);

        var root = FindRagdoll();
        if (root == null)
        {
            EditorGUILayout.HelpBox("씬에서 래그돌(ConfigurableJoint)을 못 찾았다.\n" +
                                    "먼저 메뉴 1번으로 액티브 래그돌 변환을 할 것.", MessageType.Warning);
            return;
        }

        var joints = root.GetComponentsInChildren<ConfigurableJoint>();
        EditorGUILayout.LabelField("대상", $"{root.name} (관절 {joints.Length}개)");
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        spring = EditorGUILayout.Slider("Spring (뻣뻣함)", spring, 0f, 3000f);
        damper = EditorGUILayout.Slider("Damper (감쇠)", damper, 0f, 200f);
        maxForce = EditorGUILayout.Slider("Max Force (상한)", maxForce, 100f, 20000f);
        if (EditorGUI.EndChangeCheck()) Apply(joints);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("프리셋", EditorStyles.boldLabel);

        if (GUILayout.Button("흐물 (갱비스트에 가까움)")) Set(joints, 150f, 12f);
        if (GUILayout.Button("★ 400 / 30 (찾아둔 기본값)")) Set(joints, 400f, 30f);
        if (GUILayout.Button("뻣뻣")) Set(joints, 1000f, 50f);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "마음에 드는 값을 찾았으면 ActiveRagdollTools.cs 맨 위의 SPRING / DAMPER 상수에 적어둘 것.\n" +
            "Play 중 바꾼 값은 Play를 멈추면 날아간다.", MessageType.Warning);
    }

    void Set(ConfigurableJoint[] joints, float s, float d)
    {
        spring = s; damper = d;
        Apply(joints);
    }

    void Apply(ConfigurableJoint[] joints)
    {
        foreach (var j in joints)
        {
            if (!Application.isPlaying) Undo.RecordObject(j, "스프링 조절");
            j.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = maxForce
            };
            if (!Application.isPlaying) EditorUtility.SetDirty(j);
        }
    }

    static GameObject FindRagdoll() =>
        Object.FindObjectsByType<ConfigurableJoint>(FindObjectsInactive.Include)
              .Select(j => j.transform.root.gameObject)
              .FirstOrDefault();
}
