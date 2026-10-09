using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using UnityEditor.SceneManagement;

public class ComfyUIEditorWindow : EditorWindow
{
    private string serverUrl = "http://localhost:8188";
    private string promptText = "a cute character, t-pose, full body, whithout background, 3d game asset, Low poly Style, simple shapes, minimalist";
    private string imageName = "Gemini_Generated_Image_p3b1ybp3b1ybp3b1.png";

    // 상태 관리 변수
    private enum State { Idle, GeneratingImage, WaitingForAccept, Generating3D }
    private State currentState = State.Idle;
    private string statusMessage = "대기 중...";
    private Texture2D previewTexture;
    private string currentPromptId;
    private string generatedImageFilename; // 1단계에서 생성된 파일명 보관 (output 폴더 기준)

    // 폴링 타임아웃 (초). SDXL 계열은 CPU/저사양 GPU에서 60초를 훌쩍 넘길 수 있으므로 넉넉하게 설정
    private const float POLL_INTERVAL = 1.5f;
    private const float MAX_WAIT_SECONDS = 180f;

    [MenuItem("Tools/ComfyUI Generator Tool")]
    public static void ShowWindow()
    {
        GetWindow<ComfyUIEditorWindow>("ComfyUI Controller");
    }

    private void OnGUI()
    {
        GUILayout.Label("ComfyUI Unity Editor Controller (2-Step)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        serverUrl = EditorGUILayout.TextField("ComfyUI Base URL", serverUrl);
        imageName = EditorGUILayout.TextField("Reference Image Name", imageName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Prompt Input:");
        promptText = EditorGUILayout.TextArea(promptText, GUILayout.Height(60));

        EditorGUILayout.Space();

        bool isBusy = (currentState != State.Idle && currentState != State.WaitingForAccept);
        GUI.enabled = !isBusy;

        string mainButtonText = "1. 2D 이미지 생성하기";
        if (currentState == State.GeneratingImage) mainButtonText = "2D 이미지 생성 중...";
        else if (currentState == State.Generating3D) mainButtonText = "3D 모델 변환 중...";

        if (GUILayout.Button(mainButtonText, GUILayout.Height(40)))
        {
            EditorCoroutines.Execute(RequestStep1_GenerateImage());
        }

        GUI.enabled = true;

        EditorGUILayout.Space();
        GUILayout.Label($"상태: {statusMessage}", EditorStyles.helpBox);

        if (previewTexture != null)
        {
            EditorGUILayout.Space();
            GUILayout.Label("--- 생성된 2D 이미지 확인 ---", EditorStyles.boldLabel);

            Rect rect = GUILayoutUtility.GetRect(250, 250, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, previewTexture);

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("수락 & 3D 변환 진행", GUILayout.Height(35)))
            {
                EditorCoroutines.Execute(RequestStep2_Generate3D(generatedImageFilename));
            }
            if (GUILayout.Button("거절 (재생성)", GUILayout.Height(35)))
            {
                previewTexture = null;
                currentState = State.Idle;
                statusMessage = "거절됨. 다시 생성할 수 있습니다.";
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // ==========================================
    // 1단계: 2D 이미지 생성 워크플로우 요청
    // ==========================================
    private IEnumerator RequestStep1_GenerateImage()
    {
        currentState = State.GeneratingImage;
        statusMessage = "ComfyUI에 2D 이미지 생성 요청 중...";
        previewTexture = null;
        currentPromptId = null;

        string jsonBody = GetStep1WorkflowJson(promptText, imageName);

        // 1. 프롬프트 전송 + 응답에서 prompt_id 추출
        string postResponseText = null;
        bool postFailed = false;

        using (UnityWebRequest request = new UnityWebRequest($"{serverUrl}/prompt", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone) yield return null;

            postResponseText = request.downloadHandler != null ? request.downloadHandler.text : null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 서버가 워크플로우 검증(validation)에서 즉시 거부하는 경우가 많으므로
                // 응답 본문을 반드시 로그로 남겨서 원인을 확인할 수 있게 함
                Debug.LogError($"[통신 에러] {request.error}\n서버 응답: {postResponseText}");
                statusMessage = "요청 실패! (Console 로그의 서버 응답 확인)";
                currentState = State.Idle;
                postFailed = true;
            }
        }

        if (postFailed) yield break;

        currentPromptId = ExtractPromptId(postResponseText);
        if (string.IsNullOrEmpty(currentPromptId))
        {
            Debug.LogError($"prompt_id 추출 실패. 서버 응답: {postResponseText}");
            statusMessage = "prompt_id를 받지 못했습니다.";
            currentState = State.Idle;
            yield break;
        }

        Debug.Log($"1단계 프롬프트 전송 완료. prompt_id = {currentPromptId}");

        // 2. /history/{prompt_id} 를 폴링하여 "이 작업이 실제로 완료되었는지"를 직접 확인
        //    (queue_pending/queue_running이 비어있는 것만으로는 "즉시 실패"와 "정상 완료"를 구분할 수 없음)
        statusMessage = "2D 이미지 생성 중 (결과 대기)...";

        string historyJson = null;
        float elapsed = 0f;

        while (true)
        {
            double waitStart = EditorApplication.timeSinceStartup;
            yield return WaitRealSeconds(POLL_INTERVAL);
            elapsed += (float)(EditorApplication.timeSinceStartup - waitStart);

            if (elapsed > MAX_WAIT_SECONDS)
            {
                Debug.LogError("[ComfyUI] 렌더링 대기 타임아웃!");
                statusMessage = "타임아웃 발생! (서버가 살아있는지, Console에 ComfyUI 에러가 없는지 확인)";
                currentState = State.Idle;
                yield break;
            }

            using (UnityWebRequest histReq = UnityWebRequest.Get($"{serverUrl}/history/{currentPromptId}"))
            {
                // 주의: UnityWebRequestAsyncOperation은 IEnumerator가 아니라서
                // EditorCoroutines의 재귀 처리로도 자동으로 대기되지 않는다.
                // 반드시 isDone을 직접 폴링해야 한다.
                var histOp = histReq.SendWebRequest();
                while (!histOp.isDone) yield return null;

                if (histReq.result != UnityWebRequest.Result.Success)
                {
                    continue; // 일시적 통신 실패는 무시하고 계속 폴링
                }

                string text = histReq.downloadHandler.text;

                // 아직 실행 중이면 서버는 빈 오브젝트 "{}" 를 반환함.
                // prompt_id 키가 포함된 응답이 오면 (성공이든 에러든) 작업이 종료된 것.
                if (!string.IsNullOrEmpty(text) && text.Contains(currentPromptId) && text.Trim() != "{}")
                {
                    historyJson = text;
                    break;
                }
            }
        }

        Debug.Log("ComfyUI 작업 완료 확인!");

        // 3. history 응답에서 실제 생성된 파일명을 파싱 (하드코딩 X)
        string targetFilename = ParseOutputFilename(historyJson);

        if (string.IsNullOrEmpty(targetFilename))
        {
            Debug.LogError($"history 응답에서 파일명을 찾지 못했습니다. (워크플로우 실행 중 에러가 났을 수 있음)\n{historyJson}");
            statusMessage = "결과 파일명을 찾지 못했습니다. Console 로그 확인.";
            currentState = State.Idle;
            yield break;
        }

        string imageUrl = $"{serverUrl}/view?filename={UnityWebRequest.EscapeURL(targetFilename)}&type=output&subfolder=";

        statusMessage = "생성된 이미지 다운로드 중...";
        using (UnityWebRequest imgReq = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            var imgOp = imgReq.SendWebRequest();
            while (!imgOp.isDone) yield return null;

            if (imgReq.result == UnityWebRequest.Result.Success)
            {
                previewTexture = DownloadHandlerTexture.GetContent(imgReq);
                generatedImageFilename = targetFilename;
                statusMessage = "이미지 생성 완료! 수락 또는 거절을 선택하세요.";
                currentState = State.WaitingForAccept;
                Debug.Log($"[성공] 유니티 에디터로 이미지 다운로드 완료! ({targetFilename})");
            }
            else
            {
                Debug.LogError($"이미지 다운로드 실패. URL: {imageUrl}\n{imgReq.error}");
                statusMessage = "이미지 다운로드 실패";
                currentState = State.Idle;
            }
        }
    }

    // ==========================================
    // 2단계: 수락 시 TripoSR 3D 모델링 워크플로우 요청 + 결과 임포트/배치
    // ==========================================
    private IEnumerator RequestStep2_Generate3D(string targetImageName)
    {
        currentState = State.Generating3D;
        statusMessage = "3D 모델 변환 작업 요청 중 (TripoSR)...";

        string jsonBody = GetStep2WorkflowJson(targetImageName);
        string postResponseText = null;

        using (UnityWebRequest request = new UnityWebRequest($"{serverUrl}/prompt", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone) yield return null;

            postResponseText = request.downloadHandler != null ? request.downloadHandler.text : null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[3D 변환 통신 에러] {request.error}\n서버 응답: {postResponseText}");
                statusMessage = "3D 변환 통신 실패!";
                currentState = State.WaitingForAccept;
                yield break;
            }
        }

        string promptId = ExtractPromptId(postResponseText);
        if (string.IsNullOrEmpty(promptId))
        {
            Debug.LogError($"3D 변환 prompt_id 추출 실패. 서버 응답: {postResponseText}");
            statusMessage = "3D 변환 prompt_id를 받지 못했습니다.";
            currentState = State.WaitingForAccept;
            yield break;
        }

        // history 폴링 (Step1과 동일한 패턴)
        statusMessage = "3D 모델 생성 중 (결과 대기)...";
        string historyJson = null;
        float elapsed = 0f;

        while (true)
        {
            double waitStart = EditorApplication.timeSinceStartup;
            yield return WaitRealSeconds(POLL_INTERVAL);
            elapsed += (float)(EditorApplication.timeSinceStartup - waitStart);

            if (elapsed > MAX_WAIT_SECONDS)
            {
                Debug.LogError("[ComfyUI] 3D 변환 대기 타임아웃!");
                statusMessage = "3D 변환 타임아웃 발생!";
                currentState = State.Idle;
                yield break;
            }

            using (UnityWebRequest histReq = UnityWebRequest.Get($"{serverUrl}/history/{promptId}"))
            {
                var histOp = histReq.SendWebRequest();
                while (!histOp.isDone) yield return null;

                if (histReq.result != UnityWebRequest.Result.Success) continue;

                string text = histReq.downloadHandler.text;
                if (!string.IsNullOrEmpty(text) && text.Contains(promptId) && text.Trim() != "{}")
                {
                    historyJson = text;
                    break;
                }
            }
        }

        // TripoSRViewer의 ui 출력에서 mesh 파일명 + subfolder 파싱
        if (!ParseMeshOutput(historyJson, out string meshFilename, out string meshSubfolder))
        {
            Debug.LogError($"history 응답에서 mesh 파일명을 찾지 못했습니다.\n{historyJson}");
            statusMessage = "3D 모델 파일명을 찾지 못했습니다.";
            currentState = State.Idle;
            yield break;
        }

        statusMessage = "생성된 3D 모델 다운로드 중...";
        string meshUrl = $"{serverUrl}/view?filename={UnityWebRequest.EscapeURL(meshFilename)}&type=output&subfolder={UnityWebRequest.EscapeURL(meshSubfolder)}";

        using (UnityWebRequest meshReq = UnityWebRequest.Get(meshUrl))
        {
            var meshOp = meshReq.SendWebRequest();
            while (!meshOp.isDone) yield return null;

            if (meshReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"3D 모델 다운로드 실패. URL: {meshUrl}\n{meshReq.error}");
                statusMessage = "3D 모델 다운로드 실패";
                currentState = State.Idle;
                yield break;
            }

            ImportMeshIntoProjectAndPlaceInScene(meshReq.downloadHandler.data);
        }

        currentState = State.Idle;
    }

    private bool ParseMeshOutput(string historyJson, out string filename, out string subfolder)
    {
        filename = null;
        subfolder = "";

        try
        {
            int meshKeyIndex = historyJson.IndexOf("\"mesh\"");
            if (meshKeyIndex == -1) return false;

            int filenameKeyIndex = historyJson.IndexOf("\"filename\"", meshKeyIndex);
            if (filenameKeyIndex == -1) return false;

            int colonIndex = historyJson.IndexOf(":", filenameKeyIndex);
            int startIndex = historyJson.IndexOf("\"", colonIndex) + 1;
            int endIndex = historyJson.IndexOf("\"", startIndex);
            if (startIndex == 0 || endIndex == -1) return false;
            filename = historyJson.Substring(startIndex, endIndex - startIndex);

            int subfolderKeyIndex = historyJson.IndexOf("\"subfolder\"", endIndex);
            if (subfolderKeyIndex != -1)
            {
                int sColon = historyJson.IndexOf(":", subfolderKeyIndex);
                int sStart = historyJson.IndexOf("\"", sColon) + 1;
                int sEnd = historyJson.IndexOf("\"", sStart);
                if (sStart != 0 && sEnd != -1)
                    subfolder = historyJson.Substring(sStart, sEnd - sStart);
            }

            return !string.IsNullOrEmpty(filename);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"mesh 파일명 파싱 중 에러: {e.Message}");
            return false;
        }
    }

    private void ImportMeshIntoProjectAndPlaceInScene(byte[] meshBytes)
    {
        const string targetFolder = "Assets/AI/Model";

        if (!AssetDatabase.IsValidFolder("Assets/AI"))
            AssetDatabase.CreateFolder("Assets", "AI");
        if (!AssetDatabase.IsValidFolder(targetFolder))
            AssetDatabase.CreateFolder("Assets/AI", "Model");

        string safeName = $"Model_{System.DateTime.Now:yyyyMMdd_HHmmss}.obj";
        string assetPath = $"{targetFolder}/{safeName}";
        string fullPath = System.IO.Path.Combine(Application.dataPath, "..", assetPath);

        System.IO.File.WriteAllBytes(fullPath, meshBytes);

        // 동기 임포트: 바로 이어서 로드/배치 가능하도록
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.Refresh();

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (modelAsset == null)
        {
            Debug.LogError($"임포트된 모델을 로드하지 못했습니다: {assetPath}");
            statusMessage = "모델 임포트 실패 (Console 확인)";
            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
        if (instance == null)
            instance = Instantiate(modelAsset); // OBJ는 프리팹으로 안 잡힐 수 있어 fallback

        instance.transform.position = GetSpawnPositionInFrontOfSceneCamera();
        instance.name = System.IO.Path.GetFileNameWithoutExtension(safeName);

        Undo.RegisterCreatedObjectUndo(instance, "Import AI Generated Model");
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        statusMessage = $"3D 모델 씬 배치 완료! ({safeName})";
        Debug.Log($"[성공] 모델을 {assetPath}에 저장하고 씬에 배치했습니다.");
    }
    private Vector3 GetSpawnPositionInFrontOfSceneCamera()
    {
        const float spawnDistance = 2f;

        SceneView sceneView = SceneView.lastActiveSceneView;

        // Scene 뷰가 아직 한 번도 열린 적 없거나 닫혀있는 경우를 대비한 fallback
        if (sceneView == null || sceneView.camera == null)
            return Vector3.zero;

        Transform camTransform = sceneView.camera.transform;
        return camTransform.position + camTransform.forward * spawnDistance;
    }

    // --- 1단계 JSON (2D 생성 + SaveImage) ---
    private string GetStep1WorkflowJson(string userPrompt, string imgName)
    {
        long randomSeed = System.DateTime.Now.Ticks;

        string safePrompt = EscapeJsonString(userPrompt);
        string safeImgName = EscapeJsonString(imgName);

        return $@"{{
            ""prompt"": {{
                ""91"": {{ ""inputs"": {{ ""ckpt_name"": ""animagine-xl-3.1.safetensors"" }}, ""class_type"": ""CheckpointLoaderSimple"" }},
                ""92"": {{ ""inputs"": {{ ""text"": ""{safePrompt}"", ""clip"": [ ""91"", 1 ] }}, ""class_type"": ""CLIPTextEncode"" }},
                ""93"": {{ ""inputs"": {{ ""seed"": {randomSeed}, ""steps"": 20, ""cfg"": 8, ""sampler_name"": ""euler"", ""scheduler"": ""simple"", ""denoise"": 0.6, ""model"": [ ""91"", 0 ], ""positive"": [ ""92"", 0 ], ""negative"": [ ""94"", 0 ], ""latent_image"": [ ""101"", 0 ] }}, ""class_type"": ""KSampler"" }},
                ""94"": {{ ""inputs"": {{ ""text"": ""ugly, low quality, cropped, bad hands, complex details, photorealistic, realistic, 2d, pixel art, voxel, grid"", ""clip"": [ ""91"", 1 ] }}, ""class_type"": ""CLIPTextEncode"" }},
                ""95"": {{ ""inputs"": {{ ""samples"": [ ""93"", 0 ], ""vae"": [ ""91"", 2 ] }}, ""class_type"": ""VAEDecode"" }},
                ""100"": {{ ""inputs"": {{ ""image"": ""{safeImgName}"" }}, ""class_type"": ""LoadImage"" }},
                ""101"": {{ ""inputs"": {{ ""pixels"": [ ""100"", 0 ], ""vae"": [ ""91"", 2 ] }}, ""class_type"": ""VAEEncode"" }},
                ""105"": {{ ""inputs"": {{ ""filename_prefix"": ""ComfyUI_Step1"", ""images"": [ ""95"", 0 ] }}, ""class_type"": ""SaveImage"" }}
            }}
        }}";
    }

    // --- 2단계 JSON (방금 만든 이미지를 바탕으로 TripoSR 실행) ---
    private string GetStep2WorkflowJson(string confirmedImageName)
    {
        // 중요: Step1의 SaveImage는 파일을 "output" 폴더에 저장하지만
        // LoadImage 노드는 기본적으로 "input" 폴더에서만 파일을 찾는다.
        // 파일명 뒤에 " [output]" 어노테이션을 붙여야 output 폴더를 읽는다.
        string safeImgName = EscapeJsonString(confirmedImageName + " [output]");

        return $@"{{
        ""prompt"": {{
            ""79"": {{ ""inputs"": {{ ""model"": ""u2net: general purpose"", ""providers"": ""CPU"" }}, ""class_type"": ""RemBGSession+"" }},
            ""80"": {{ ""inputs"": {{ ""rembg_session"": [ ""79"", 0 ], ""image"": [ ""100"", 0 ] }}, ""class_type"": ""ImageRemoveBackground+"" }},
            ""81"": {{ ""inputs"": {{ ""model"": ""model.ckpt"", ""chunk_size"": 8192 }}, ""class_type"": ""TripoSRModelLoader"" }},
            ""82"": {{ ""inputs"": {{ ""geometry_resolution"": 256, ""threshold"": 25, ""model"": [ ""81"", 0 ], ""reference_image"": [ ""100"", 0 ], ""reference_mask"": [ ""80"", 1 ] }}, ""class_type"": ""TripoSRSampler"" }},
            ""83"": {{ ""inputs"": {{ ""preview3d"": null, ""mesh"": [ ""82"", 0 ] }}, ""class_type"": ""TripoSRViewer"" }},
            ""100"": {{ ""inputs"": {{ ""image"": ""{safeImgName}"" }}, ""class_type"": ""LoadImage"" }}
        }}
    }}";
    }

    // JSON 문자열 내부에 안전하게 넣기 위한 최소한의 이스케이프 처리
    // (프롬프트에 따옴표, 백슬래시, 줄바꿈 등이 들어가면 JSON이 깨지는 것을 방지)
    private string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // Editor 코루틴 드라이버(EditorCoroutines)는 WaitForSecondsRealtime 같은
    // YieldInstruction을 해석하지 않고 매 tick마다 그냥 MoveNext()만 호출한다.
    // 그래서 "몇 초 기다리기"는 아래처럼 EditorApplication.timeSinceStartup을
    // 직접 비교하는 nested IEnumerator로 구현해야 실제로 대기가 된다.
    private static IEnumerator WaitRealSeconds(float seconds)
    {
        double start = EditorApplication.timeSinceStartup;
        while (EditorApplication.timeSinceStartup - start < seconds)
        {
            yield return null;
        }
    }

    private string ExtractPromptId(string json)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, @"""prompt_id""\s*:\s*""([^""]+)""");
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Prompt ID 추출 중 에러: {e.Message}");
        }

        return "";
    }

    private string ParseOutputFilename(string historyJson)
    {
        try
        {
            int filenameKeyIndex = historyJson.IndexOf("\"filename\"");
            if (filenameKeyIndex != -1)
            {
                int colonIndex = historyJson.IndexOf(":", filenameKeyIndex);
                int startIndex = historyJson.IndexOf("\"", colonIndex) + 1;
                int endIndex = historyJson.IndexOf("\"", startIndex);

                if (startIndex != 0 && endIndex != -1)
                {
                    return historyJson.Substring(startIndex, endIndex - startIndex);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"파일명 파싱 중 에러 발생: {e.Message}");
        }

        return "";
    }
}

public static class EditorCoroutines
{
    public static void Execute(IEnumerator routine)
    {
        EditorApplication.update += Update;
        void Update()
        {
            if (!MoveNextRecursive(routine))
            {
                EditorApplication.update -= Update;
            }
        }
    }

    // 이 드라이버는 UnityEngine의 실제 코루틴 엔진이 아니므로
    // WaitForSeconds류의 YieldInstruction을 자동으로 해석해주지 않는다.
    // 대신 "yield return 다른IEnumerator" 형태의 중첩 코루틴은 지원하도록
    // 재귀적으로 내부 IEnumerator를 먼저 끝까지 진행시킨 뒤 바깥으로 돌아온다.
    // (WaitRealSeconds() 같은 헬퍼가 이 방식으로 정상 동작함)
    private static bool MoveNextRecursive(IEnumerator routine)
    {
        if (routine.Current is IEnumerator nested)
        {
            if (MoveNextRecursive(nested))
            {
                return true; // 중첩 코루틴이 아직 진행 중이면 바깥은 아직 한 스텝도 진행하지 않음
            }
        }

        return routine.MoveNext();
    }
}