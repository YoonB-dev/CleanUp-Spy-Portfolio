using UnityEngine;
using UnityEditor;
using System.Linq;

/// <summary>
/// 청소 로봇 래그돌 실험용 툴. Tools > CleanUp Mafia 메뉴에서 순서대로 실행한다.
/// 게임에 들어갈 최종 래그돌은 dev 담당이고, 이건 모델러 쪽 확인용 샌드박스다.
/// </summary>
public static class ActiveRagdollTools
{
    // ── 튜닝 값 (2026-07-17 실험으로 찾은 값) ──────────────────────
    // Spring 높이면 뻣뻣하게 버티고, 낮추면 갱비스트처럼 흐물거린다.
    const float SPRING = 400f;      // 자세를 되돌리려는 힘
    const float DAMPER = 30f;       // 출렁임 감쇠. 낮으면 부들부들 떤다
    const float MAX_FORCE = 10000f; // Infinity로 두면 폭발할 수 있어 상한을 둔다
    const float PROP_MASS = 0.4f;   // 손에 드는 소품 무게(kg)

    const string BONE_HAND_R = "Hand.R";
    const string BONE_HAND_L = "Hand.L";
    const string BONE_FOREARM_R = "Forearm.R";
    const string SOCKET_NAME = "Socket_Prop_R";

    // ──────────────────────────────────────────────────────────────
    // 1. CharacterJoint(패시브) → ConfigurableJoint(액티브) 변환
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/1. 액티브 래그돌로 변환", false, 1)]
    static void ConvertToActiveRagdoll()
    {
        var root = RequireSelectedRoot();
        if (root == null) return;

        var charJoints = root.GetComponentsInChildren<CharacterJoint>();
        if (charJoints.Length == 0)
        {
            EditorUtility.DisplayDialog("변환할 게 없음",
                "CharacterJoint가 하나도 없다.\n이미 변환했거나 래그돌이 아닌 로봇을 골랐다.\n" +
                "(래그돌이 붙은 건 SM_Cleaner_Robot_Humanoid_01 쪽이다)", "확인");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(root, "액티브 래그돌로 변환");

        // ConfigurableJoint는 "만들어진 시점의 자세"를 0점으로 잡는다.
        // 지금이 rest 포즈이므로, targetRotation을 건드리지 않으면 rest 포즈로 돌아가려 한다.
        foreach (var cj in charJoints)
        {
            var go = cj.gameObject;

            // 원래 CharacterJoint 설정을 먼저 복사해둔다
            var connected = cj.connectedBody;
            var anchor = cj.anchor;
            var axis = cj.axis;
            var swingAxis = cj.swingAxis;
            var twistLow = cj.lowTwistLimit.limit;
            var twistHigh = cj.highTwistLimit.limit;
            var swing1 = cj.swing1Limit.limit;
            var swing2 = cj.swing2Limit.limit;

            Undo.DestroyObjectImmediate(cj);

            var j = Undo.AddComponent<ConfigurableJoint>(go);
            j.connectedBody = connected;
            j.anchor = anchor;
            j.axis = axis;
            j.secondaryAxis = swingAxis;
            j.autoConfigureConnectedAnchor = true;

            // 위치는 잠그고 회전만 허용
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;

            // ⚠ Ragdoll Wizard가 만든 한계는 "시체가 부자연스럽게 안 꺾이게" 막는 용도라
            //   swing1=0 / swing2=0 처럼 거의 잠겨 있다(우리 Prefab 실측).
            //   그대로 두면 관절이 트위스트 한 축으로만 움직여서, 어떤 각도를 줘도 같은 데로 꺾인다.
            //   액티브 래그돌은 slerpDrive(스프링)가 자세를 잡아주므로 한계는 풀어도 된다.
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;

            // 한계값 자체는 남겨둔다 (메뉴에서 Limited로 되돌리면 이 값이 쓰인다)
            j.lowAngularXLimit = new SoftJointLimit { limit = twistLow };
            j.highAngularXLimit = new SoftJointLimit { limit = twistHigh };
            j.angularYLimit = new SoftJointLimit { limit = swing1 };
            j.angularZLimit = new SoftJointLimit { limit = swing2 };

            // 여기가 액티브 래그돌의 핵심. 근육에 해당한다.
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = SPRING,
                positionDamper = DAMPER,
                maximumForce = MAX_FORCE
            };
            j.targetRotation = Quaternion.identity; // = 지금 자세(rest)를 유지하려 함

            // 래그돌 안정화용. 끄면 관절이 덜 튄다
            j.enablePreprocessing = false;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.05f;
            j.projectionAngle = 5f;
        }

        Debug.Log($"[액티브 래그돌] 관절 {charJoints.Length}개를 ConfigurableJoint로 변환했다. " +
                  $"Spring={SPRING} / Damper={DAMPER}", root);
        EditorUtility.SetDirty(root);
    }

    // ──────────────────────────────────────────────────────────────
    // 2. 손·발 콜라이더 추가
    //    Ragdoll Wizard는 손도 발도 안 잡는다(작업로그 함정 19) → 발이 바닥에 묻힌다.
    //    Rigidbody는 안 붙인다. 콜라이더만 두면 조상 뼈(Forearm/Shin)의 Rigidbody에
    //    복합 콜라이더로 합쳐져서 한 몸으로 움직인다. 관절을 늘리는 것보다 안정적이다.
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/2. 손·발 콜라이더 추가", false, 2)]
    static void AddHandAndFootColliders()
    {
        var root = RequireSelectedRoot();
        if (root == null) return;

        Undo.RegisterFullObjectHierarchyUndo(root, "손·발 콜라이더 추가");
        int added = 0;

        // ── 손: 미튼 장갑이라 구체가 형태에 제일 가깝다 ──
        foreach (var boneName in new[] { BONE_HAND_R, BONE_HAND_L })
        {
            var hand = FindBone(root.transform, boneName);
            if (hand == null) { Debug.LogWarning($"뼈 '{boneName}'을 못 찾았다."); continue; }
            if (hand.GetComponent<Collider>() != null) continue;

            var sc = Undo.AddComponent<SphereCollider>(hand.gameObject);
            sc.radius = 0.06f;
            added++;
        }

        // ── 발 ──
        // BoxCollider엔 Rotation 항목이 없다. 박스는 항상 자기 GameObject의 축에 정렬된다.
        // 발 뼈는 크게 기울어 있어서(-70°대) 박스를 직접 붙이면 부츠와 어긋난다.
        // → 회전시킨 자식을 하나 끼우고 거기에 박스를 붙인다.
        //   자식엔 Rigidbody가 없으니 여전히 Shin의 복합 콜라이더로 합쳐진다.
        Quaternion bodyRot = ComputeBodyRotation(root.transform);

        foreach (var boneName in new[] { "Foot.R", "Foot.L" })
        {
            var foot = FindBone(root.transform, boneName);
            if (foot == null) { Debug.LogWarning($"뼈 '{boneName}'을 못 찾았다."); continue; }

            // 예전 버전이 뼈에 직접 붙여둔 기울어진 박스는 걷어낸다
            var stale = foot.GetComponent<BoxCollider>();
            if (stale != null) Undo.DestroyObjectImmediate(stale);

            // 이미 있으면 새로 만들지 말고 각도·크기를 다시 잡는다 (재실행으로 고칠 수 있게)
            string holderName = boneName.Replace("Foot", "FootCollider"); // FootCollider.R
            var holder = foot.Find(holderName);
            GameObject go;
            if (holder != null)
            {
                go = holder.gameObject;
                Undo.RecordObject(go.transform, "발 콜라이더 각도 재계산");
            }
            else
            {
                go = new GameObject(holderName);
                Undo.RegisterCreatedObjectUndo(go, "발 콜라이더");
                go.transform.SetParent(foot, false);
            }

            // 발이 향한 방향 = 발 뼈의 로컬 Y(뼈 방향 = 발끝 쪽)를 수평면에 투영한 것.
            // 몸 전체 방향으로 두 발을 똑같이 맞추면 안 된다 — 발은 좌우가 다르게 벌어져 있다.
            // 위쪽만 월드 up으로 고정하면 "똑바로 서서 발끝을 향하는" 박스가 된다.
            Vector3 toeDir = foot.rotation * Vector3.up;
            Vector3 flatToe = Vector3.ProjectOnPlane(toeDir, Vector3.up);
            if (flatToe.sqrMagnitude < 1e-4f) flatToe = bodyRot * Vector3.forward; // 발이 수직이면 몸 기준
            go.transform.rotation = Quaternion.LookRotation(flatToe.normalized, Vector3.up);

            var bc = go.GetComponent<BoxCollider>();
            if (bc == null) bc = Undo.AddComponent<BoxCollider>(go);
            else Undo.RecordObject(bc, "발 콜라이더 재계산");

            // 작업로그: 모델은 발=원점(바닥). 루트 높이가 곧 발바닥이다.
            float ankleHeight = Mathf.Max(0.02f, foot.position.y - root.transform.position.y);
            Vector3 soleCenterWorld = foot.position
                                    - Vector3.up * (ankleHeight * 0.5f)
                                    + go.transform.forward * 0.03f; // 발끝 쪽으로 살짝
            bc.center = go.transform.InverseTransformPoint(soleCenterWorld);

            // 축이 이제 "선 발" 기준이다: X=발 폭, Y=발 두께, Z=발 길이
            bc.size = new Vector3(0.12f, ankleHeight, 0.22f);
            added++;
        }

        Debug.Log($"[손·발 콜라이더] {added}개 처리.\n" +
                  $"발 박스는 'FootCollider.R/L' 자식에 있다 — BoxCollider엔 회전이 없어서\n" +
                  $"각도를 주려면 자식을 끼우는 수밖에 없다.\n" +
                  $"각도는 자식의 Transform Rotation, 크기는 Box의 Size로 손볼 것. 재실행하면 다시 계산한다.", root);
    }

    // ──────────────────────────────────────────────────────────────
    // 3. 소품을 손에 미리보기로 붙이기 (물리 없음 — 회전 맞추는 단계)
    //    ⚠ 선택할 것 = 그립 원점을 가진 자식 (총이면 'Handgun')
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/3. 소품 그립 — 미리보기 부착", false, 20)]
    static void AttachPropPreview()
    {
        var grip = Selection.activeGameObject;
        if (grip == null || grip.GetComponent<MeshFilter>() == null)
        {
            EditorUtility.DisplayDialog("그립 오브젝트를 선택할 것",
                "FBX 루트(SM_Handgun_01)가 아니라, 원점이 그립에 있는 자식을 고를 것.\n" +
                "총이면 → 'Handgun'\n\n" +
                "(FBX 루트는 Blender 월드원점에 있는 빈 껍데기다 — 작업로그 함정 30)", "확인");
            return;
        }

        var ragdoll = FindRagdollRoot();
        if (ragdoll == null)
        {
            EditorUtility.DisplayDialog("래그돌을 못 찾음",
                "씬에서 래그돌(Rigidbody+Joint)이 붙은 로봇을 못 찾았다.\n" +
                "SM_Cleaner_Robot_Humanoid_01을 씬에 넣었는지 확인할 것.", "확인");
            return;
        }

        var hand = FindBone(ragdoll.transform, BONE_HAND_R);
        if (hand == null)
        {
            EditorUtility.DisplayDialog("손을 못 찾음", $"'{ragdoll.name}' 밑에 '{BONE_HAND_R}'이 없다.", "확인");
            return;
        }

        // ⚠ 안전장치: 이미 손에 붙은 걸 다시 실행하면 "FBX 루트"를 찾다가 로봇 루트를 집어
        // 로봇을 언팩하고 몸통 메쉬를 뜯어가는 사고가 난다. 그래서 두 겹으로 막는다.
        var fbxRoot = GetSceneRootOf(grip);
        bool alreadyAttached = grip.transform.parent != null && grip.transform.parent.name == SOCKET_NAME;
        bool insideRagdoll = grip.transform.IsChildOf(ragdoll.transform);

        if (alreadyAttached || insideRagdoll || fbxRoot == ragdoll)
        {
            Debug.Log($"[미리보기] '{grip.name}'은 이미 로봇에 붙어 있다. 조각 합치기는 건너뛴다.\n" +
                      $"회전만 다시 맞추면 된다.", grip);
            fbxRoot = grip; // 아래 합치기/정리 단계를 타지 않게 한다
        }

        // 총은 Handgun/Paint/Paint_Tank 3조각이다. 그립 조각만 손에 붙이면 나머지가 공중에 남으므로
        // 형제 조각들을 먼저 그립 밑으로 모아서 한 덩어리로 만든다.
        if (fbxRoot != grip)
        {
            // 모델 프리팹 인스턴스는 구조 변경이 막혀 있다 → 먼저 언팩
            // (씬 인스턴스만 풀리는 거고 Assets의 FBX 원본은 그대로다)
            if (PrefabUtility.IsPartOfPrefabInstance(fbxRoot))
                PrefabUtility.UnpackPrefabInstance(fbxRoot, PrefabUnpackMode.Completely,
                                                   InteractionMode.AutomatedAction);

            var siblings = fbxRoot.transform.Cast<Transform>()
                                  .Where(t => t != grip.transform).ToArray();
            foreach (var s in siblings)
                Undo.SetTransformParent(s, grip.transform, "총 조각 합치기");

            if (siblings.Length > 0)
                Debug.Log($"[조각 합치기] {string.Join(", ", siblings.Select(s => s.name))} " +
                          $"→ '{grip.name}' 밑으로 옮겼다. 이제 한 덩어리로 움직인다.");
        }

        // 소켓을 하나 끼워서 그 밑에 소품을 붙인다.
        // 소켓의 로컬 오프셋 = 나중에 대걸레/페인트 롤러에 그대로 재사용할 그립 값.
        var socket = hand.Find(SOCKET_NAME);
        if (socket == null)
        {
            var go = new GameObject(SOCKET_NAME);
            Undo.RegisterCreatedObjectUndo(go, "소켓 생성");
            socket = go.transform;
            socket.SetParent(hand, false);
            socket.localPosition = Vector3.zero;
            socket.localRotation = Quaternion.identity;
        }

        Undo.SetTransformParent(grip.transform, socket, "소품 미리보기 부착");
        grip.transform.localPosition = Vector3.zero;
        grip.transform.localRotation = Quaternion.identity;

        // 자식을 다 빼내서 껍데기만 남은 FBX 루트는 정리
        if (fbxRoot != grip && fbxRoot.transform.childCount == 0
            && fbxRoot.GetComponents<Component>().Length == 1) // Transform 하나뿐
            Undo.DestroyObjectImmediate(fbxRoot);

        Selection.activeGameObject = grip;
        Debug.Log(
            $"[미리보기] '{grip.name}'을 {ragdoll.name}/{BONE_HAND_R}/{SOCKET_NAME} 밑에 붙였다.\n" +
            $"이 오브젝트의 원점 = 그립이라 위치는 이미 맞다.\n" +
            $"→ 남은 건 회전뿐. Rotation만 돌려서 총구 방향을 맞출 것.\n" +
            $"→ 다 맞췄으면 메뉴 4번을 실행할 것.", grip);
    }

    // ──────────────────────────────────────────────────────────────
    // 4. 맞춰둔 위치 그대로 물리 그립으로 전환 (FixedJoint)
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/4. 소품 그립 — 물리 고정(FixedJoint)", false, 21)]
    static void LockPropWithJoint()
    {
        var prop = Selection.activeGameObject;
        if (prop == null) { EditorUtility.DisplayDialog("소품을 선택할 것", "3번에서 맞춰둔 소품을 고르고 다시 실행할 것.", "확인"); return; }

        var ragdoll = FindRagdollRoot();
        if (ragdoll == null) { EditorUtility.DisplayDialog("래그돌을 못 찾음", "씬에 래그돌 로봇이 없다.", "확인"); return; }

        // Hand.R엔 Rigidbody가 없다(래그돌 11바디에서 빠짐) → 팔뚝에 매단다
        var forearm = FindBone(ragdoll.transform, BONE_FOREARM_R);
        var armRb = forearm != null ? forearm.GetComponent<Rigidbody>() : null;
        if (armRb == null)
        {
            EditorUtility.DisplayDialog("팔뚝 Rigidbody 없음",
                $"'{BONE_FOREARM_R}'에 Rigidbody가 없다. 래그돌이 맞는지 확인할 것.", "확인");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(prop, "물리 그립으로 전환");

        // 맞춰둔 월드 자세를 유지한 채 부모에서 떼어낸다.
        // FixedJoint가 잡아줄 거라 부모-자식 관계는 필요 없다.
        Undo.SetTransformParent(prop.transform, null, "소품 분리");

        if (prop.GetComponent<Collider>() == null)
        {
            var mf = prop.GetComponent<MeshFilter>();
            if (mf != null)
            {
                var mc = Undo.AddComponent<MeshCollider>(prop);
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true; // 움직이는 물체는 convex여야 함
            }
            else
            {
                Undo.AddComponent<BoxCollider>(prop);
            }
        }

        var rb = prop.GetComponent<Rigidbody>();
        if (rb == null) rb = Undo.AddComponent<Rigidbody>(prop);
        rb.mass = PROP_MASS;

        var fj = prop.GetComponent<FixedJoint>();
        if (fj == null) fj = Undo.AddComponent<FixedJoint>(prop);
        fj.connectedBody = armRb;
        fj.enablePreprocessing = false;
        // breakForce를 유한값으로 두면 세게 부딪힐 때 총을 놓친다. 안 놓치게 하려면 Infinity.
        fj.breakForce = 2000f;
        fj.breakTorque = 2000f;

        var ign = prop.GetComponent<IgnoreRagdollCollision>();
        if (ign == null) ign = Undo.AddComponent<IgnoreRagdollCollision>(prop);
        ign.RagdollRoot = ragdoll.transform;

        Debug.Log(
            $"[물리 그립] '{prop.name}' → {BONE_FOREARM_R}에 FixedJoint로 연결했다.\n" +
            $"무게 {PROP_MASS}kg / breakForce {fj.breakForce} (세게 부딪히면 놓친다. 안 놓치게 하려면 Infinity로).\n" +
            $"Animator를 끄고 Play할 것.", prop);
    }

    // ──────────────────────────────────────────────────────────────
    // 5. 조작 세팅 — 1인칭/3인칭으로 직접 몰고 다니기
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/5. 조작 세팅 (1인칭·3인칭)", false, 22)]
    static void SetupDriver()
    {
        var ragdoll = FindRagdollRoot();
        if (ragdoll == null) { EditorUtility.DisplayDialog("래그돌을 못 찾음", "씬에 래그돌 로봇이 없다.", "확인"); return; }

        var driver = ragdoll.GetComponent<RagdollDriver>();
        if (driver == null) driver = Undo.AddComponent<RagdollDriver>(ragdoll);

        Undo.RecordObject(driver, "조작 세팅");
        driver.Hips = FindBone(ragdoll.transform, "Hips");
        driver.Head = FindBone(ragdoll.transform, "Head");
        // 조준 자세 + 걷기 흔들기
        if (ragdoll.GetComponent<RagdollPoser>() == null) Undo.AddComponent<RagdollPoser>(ragdoll);

        Selection.activeGameObject = ragdoll;
        Debug.Log(
            $"[조작 세팅] '{ragdoll.name}'에 RagdollDriver + RagdollPoser를 붙였다.\n" +
            $"부모 플레이어의 이동·시점 상태를 사용한다.\n" +
            $"조준 각도·걷기 진폭은 Play 중에 RagdollPoser Inspector에서 맞출 것.", ragdoll);
    }

    // ──────────────────────────────────────────────────────────────
    // 관절 한계 토글 — Ragdoll Wizard 한계는 자세 잡기엔 너무 빡빡하다
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/관절 한계 토글 (Free ↔ Limited)", false, 42)]
    static void ToggleJointLimits()
    {
        var ragdoll = FindRagdollRoot();
        if (ragdoll == null) { EditorUtility.DisplayDialog("래그돌을 못 찾음", "씬에 래그돌 로봇이 없다.", "확인"); return; }

        var joints = ragdoll.GetComponentsInChildren<ConfigurableJoint>();
        if (joints.Length == 0)
        {
            EditorUtility.DisplayDialog("관절 없음", "ConfigurableJoint가 없다. 메뉴 1번을 먼저 할 것.", "확인");
            return;
        }

        bool anyLimited = joints.Any(j => j.angularXMotion == ConfigurableJointMotion.Limited);
        var mode = anyLimited ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Limited;

        foreach (var j in joints)
        {
            if (!Application.isPlaying) Undo.RecordObject(j, "관절 한계 토글");
            j.angularXMotion = j.angularYMotion = j.angularZMotion = mode;
        }

        Debug.Log(mode == ConfigurableJointMotion.Free
            ? $"[관절 한계 Free] 관절 {joints.Length}개를 풀었다. 이제 X/Y/Z 각도가 각각 따로 먹는다.\n" +
              $"자세는 스프링이 잡아준다. 대신 무릎이 반대로 꺾일 수도 있다."
            : $"[관절 한계 Limited] 관절 {joints.Length}개를 원래 한계로 되돌렸다.\n" +
              $"⚠ Ragdoll Wizard 한계는 swing1=0/swing2=0이라 거의 잠긴다. 자세가 다시 안 잡힐 것이다.",
            ragdoll);
    }

    // ──────────────────────────────────────────────────────────────
    // 힙 고정 토글 — 켜면 인형처럼 매달려 서 있고, 끄면 무너진다
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/힙 고정 토글 (퍼펫 모드)", false, 40)]
    static void ToggleHipAnchor()
    {
        var ragdoll = FindRagdollRoot();
        if (ragdoll == null) { EditorUtility.DisplayDialog("래그돌을 못 찾음", "씬에 래그돌 로봇이 없다.", "확인"); return; }

        var hips = FindBone(ragdoll.transform, "Hips");
        var rb = hips != null ? hips.GetComponent<Rigidbody>() : null;
        if (rb == null) { Debug.LogWarning("Hips에 Rigidbody가 없다."); return; }

        Undo.RecordObject(rb, "힙 고정 토글");
        rb.isKinematic = !rb.isKinematic;

        Debug.Log(rb.isKinematic
            ? "[퍼펫 모드 ON] 힙이 공중에 고정된다. 팔다리가 rest 포즈로 버티는 걸 보기 좋다."
            : "[퍼펫 모드 OFF] 힙이 풀렸다. 균형 제어가 없으니 결국 쓰러진다(갱비스트도 그렇다).", rb);
    }

    // ──────────────────────────────────────────────────────────────
    // 복구 — 3번 메뉴 옛 버그로 로봇 밖으로 뜯겨나간 메쉬를 되돌린다
    // ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/CleanUp Mafia/Hierarchy 복구 (뜯겨나간 메쉬 되돌리기)", false, 60)]
    static void RepairHierarchy()
    {
        var ragdoll = FindRagdollRoot();
        if (ragdoll == null) { EditorUtility.DisplayDialog("래그돌을 못 찾음", "씬에 래그돌 로봇이 없다.", "확인"); return; }

        int moved = 0;
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include))
        {
            if (smr.transform.IsChildOf(ragdoll.transform)) continue;

            // 이 메쉬가 이 로봇 뼈를 쓰고 있나? 그렇다면 원래 이 로봇 것이다.
            var bone = smr.rootBone != null ? smr.rootBone
                     : (smr.bones != null && smr.bones.Length > 0 ? smr.bones[0] : null);
            if (bone == null || !bone.IsChildOf(ragdoll.transform)) continue;

            Undo.SetTransformParent(smr.transform, ragdoll.transform, "메쉬 되돌리기");
            moved++;
        }

        if (moved == 0)
            Debug.Log("[복구] 뜯겨나간 메쉬가 없다. Hierarchy는 멀쩡하다.", ragdoll);
        else
            Debug.Log($"[복구] 메쉬 {moved}개를 '{ragdoll.name}' 밑으로 되돌렸다.\n" +
                      $"스킨드 메쉬는 뼈를 따라 그려지므로 겉보기는 안 변한다.", ragdoll);
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────
    static GameObject RequireSelectedRoot()
    {
        var go = Selection.activeGameObject;
        if (go == null)
            EditorUtility.DisplayDialog("선택 없음", "씬에서 로봇 최상위 오브젝트를 고르고 다시 실행할 것.", "확인");
        return go;
    }

    static Transform FindBone(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    /// <summary>
    /// 캐릭터가 향한 방향. 뼈 로컬축은 Blender 관례(Y=뼈 방향)라 못 믿으니
    /// 골격 형태에서 직접 구한다: 엉덩이→머리 = 위, 왼어깨→오른어깨 = 오른쪽, 외적 = 앞.
    /// RagdollDriver.ComputeBodyFrame과 같은 계산이다.
    /// </summary>
    static Quaternion ComputeBodyRotation(Transform root)
    {
        var hips = FindBone(root, "Hips");
        var head = FindBone(root, "Head");
        var shL = FindBone(root, "Shoulder.L");
        var shR = FindBone(root, "Shoulder.R");
        if (hips == null || head == null || shL == null || shR == null)
        {
            Debug.LogWarning("골격에서 몸 방향을 못 구했다. 루트 회전을 그대로 쓴다.");
            return root.rotation;
        }

        Vector3 up = (head.position - hips.position).normalized;
        Vector3 right = (shR.position - shL.position).normalized;
        Vector3 fwd = Vector3.Cross(right, up).normalized;
        up = Vector3.Cross(fwd, right).normalized; // 직교화
        return Quaternion.LookRotation(fwd, up);
    }

    /// <summary>
    /// 씬에 로봇이 여러 개다(SM_Cleaner_Robot_01 / _Humanoid_01).
    /// 이름으로 찍지 말고 "관절이 붙어 있는 쪽"을 래그돌로 본다.
    /// </summary>
    static GameObject FindRagdollRoot()
    {
        var hits = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include)
                         .Where(rb => rb.GetComponent<Joint>() != null)
                         .Select(rb => rb.transform.root.gameObject)
                         .Distinct()
                         .ToArray();

        if (hits.Length > 1)
            Debug.LogWarning($"씬에 래그돌이 {hits.Length}개다({string.Join(", ", hits.Select(h => h.name))}). " +
                             $"'{hits[0].name}'을 쓴다.");
        return hits.FirstOrDefault();
    }

    /// <summary>선택한 오브젝트가 속한 FBX 루트(씬 최상위)를 찾는다.</summary>
    static GameObject GetSceneRootOf(GameObject go)
    {
        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        return prefabRoot != null ? prefabRoot : go.transform.root.gameObject;
    }
}
