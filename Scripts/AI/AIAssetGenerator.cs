using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

/// <summary>
/// 실험용 로컬/외부 AI 생성 도구.
/// - ComfyUI/Pollinations/Tripo와 같은 외부 AI 서비스를 사용할 수 있는 에디터 전용 툴
/// - 런타임 게임 로직이 아니라 프로토타이핑용 도구
/// - 사용 전 로컬 환경, 모델, API 키, 네트워크 권한이 준비되어 있어야 함
/// </summary>
public class AIAssetGenerator : EditorWindow
{
    private string prompt = "knight character";

    // 시스템 프롬프트 분리 (기본 제약 조건 및 스타일 정의)
    private string systemPrompt = "2D character concept art, full body, front view, T-pose, standing, white background";
    private bool useLowPoly = true;

    // 실험용 토큰. 커밋/공개 저장소에 실제 키를 남기지 않도록 로컬에서만 입력.
    private string tripoApiKey = string.Empty;

    private Texture2D generatedPreviewTexture;
    private string generatedImageUrl;
    private bool isProcessing = false;

    [MenuItem("Tools/Experimental/AI Asset Auto Generator (Local)")]
    public static void ShowWindow()
    {
        GetWindow<AIAssetGenerator>("AI 3D Generator");
    }

    private void OnGUI()
    {
        // 이 도구는 플레이 중 로직이 아니라 에디터에서만 쓰는 실험용 도구다.
        // 로컬 모델/외부 API 연결이 필요하므로 공개용 빌드나 포트폴리오 설명에는
        // "실험용 프로토타입"으로 분리해서 다루는 것이 좋다.
        GUILayout.Label("생성형 AI 에셋 자동화 파이프라인 (실험용 로컬 프로토타입)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. 사용자 프롬프트 & 스타일 옵션 UI
        prompt = EditorGUILayout.TextField("사용자 프롬프트", prompt);
        useLowPoly = EditorGUILayout.Toggle("로우폴리 스타일 적용", useLowPoly);

        EditorGUILayout.Space();

        // Tool 영역: 시스템 프롬프트 설정 (접었다 폈다 할 수 있는 Foldout 방식)
        GUILayout.Label("시스템 설정 (System Tool)", EditorStyles.boldLabel);
        systemPrompt = EditorGUILayout.TextArea(systemPrompt, GUILayout.Height(40));

        EditorGUILayout.Space();
        tripoApiKey = EditorGUILayout.TextField("Tripo API Key", tripoApiKey);

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(isProcessing);

        // ===== 1단계: T-Pose 이미지 미리보기 =====
        if (GUILayout.Button("1. T-Pose 이미지 미리보기 생성", GUILayout.Height(30)))
        {
            GenerateTPoseImage();
        }

        if (generatedPreviewTexture != null)
        {
            EditorGUILayout.Space();
            GUILayout.Label("생성된 T-Pose 이미지 미리보기:", EditorStyles.boldLabel);
            GUILayout.Box(generatedPreviewTexture, GUILayout.Width(256), GUILayout.Height(256));

            EditorGUILayout.Space();

            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("2. [승인] 이 이미지로 3D 에셋 생성 및 씬 배치 (Tripo3D)", GUILayout.Height(35)))
            {
                ConvertImageTo3DAndImport();
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUI.EndDisabledGroup();
    }

    private async void GenerateTPoseImage()
    {
        isProcessing = true;
        try
        {
            EditorUtility.DisplayProgressBar("이미지 연동 중", "T-Pose 이미지 생성 요청 중...", 0.5f);

            string finalPrompt = BuildFinalPrompt(prompt, systemPrompt, useLowPoly);

            // 1. 이미지 URL 생성
            string encodedPrompt = UnityWebRequest.EscapeURL(finalPrompt);
            generatedImageUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width=512&height=512&nologo=true";

            // 2. 미리보기용 텍스처 다운로드
            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(generatedImageUrl))
            {
                var asyncOp = www.SendWebRequest();
                while (!asyncOp.isDone) await Task.Delay(100);

                if (www.result != UnityWebRequest.Result.Success)
                    throw new Exception($"이미지 다운로드 실패: {www.error}");

                generatedPreviewTexture = DownloadHandlerTexture.GetContent(www);
            }

            Debug.Log("[1단계 성공] T-Pose 이미지 생성이 완료되었습니다.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[이미지 생성 오류] {ex.Message}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isProcessing = false;
        }
    }

    // 프롬프트 조합 모듈
    private string BuildFinalPrompt(string userPrompt, string sysPrompt, bool isLowPoly)
    {
        StringBuilder sb = new StringBuilder();

        // 1. 시스템 기본 지침 (포즈, 앵글, 배경 등)
        sb.Append(sysPrompt);

        // 2. 로우폴리 스타일 옵션 가공
        if (isLowPoly)
        {
            sb.Append(", low poly style, low poly 3d art, flat shaded");
        }

        // 3. 사용자 입력 프롬프트
        if (!string.IsNullOrEmpty(userPrompt))
        {
            sb.Append($", {userPrompt}");
        }

        return sb.ToString();
    }

    private async Task<string> RequestImageFromOpenApi(string fullPrompt)
    {
        string encodedPrompt = UnityWebRequest.EscapeURL(fullPrompt);
        string imageUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width=512&height=512&nologo=true";

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            var asyncOp = www.SendWebRequest();
            while (!asyncOp.isDone) await Task.Delay(100);

            if (www.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"이미지 생성 실패: {www.error}");
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(www);
            byte[] imageBytes = texture.EncodeToPNG();
            return Convert.ToBase64String(imageBytes);
        }
    }

    private async void ConvertImageTo3DAndImport()
    {
        if (string.IsNullOrEmpty(tripoApiKey) || string.IsNullOrEmpty(generatedImageUrl))
        {
            EditorUtility.DisplayDialog("경고", "Tripo API Key가 없거나 생성된 이미지가 없습니다.", "확인");
            return;
        }

        isProcessing = true;
        try
        {
            EditorUtility.DisplayProgressBar("Tripo3D 연동 중", "1/3. 3D 메쉬 생성 요청 중 (Image-to-3D)...", 0.2f);

            // Base64 대신 generatedImageUrl 전달
            string taskId = await RequestTripoImageToModel(generatedImageUrl);

            EditorUtility.DisplayProgressBar("Tripo3D 연동 중", "2/3. 3D 메쉬 생성 및 FBX 변환 중...", 0.6f);
            string fbxDownloadUrl = await PollTaskResult(taskId);

            EditorUtility.DisplayProgressBar("Tripo3D 연동 중", "3/3. FBX 자동 다운로드 및 프로젝트 임포트...", 0.9f);
            string savePath = "Assets/GeneratedAssets";
            if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

            string fileName = $"Model_{DateTime.Now:yyyyMMdd_HHmmss}.fbx";
            string fullPath = Path.Combine(savePath, fileName);

            await DownloadFileAsync(fbxDownloadUrl, fullPath);

            AssetDatabase.Refresh();

            GameObject loadedModel = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (loadedModel != null)
            {
                GameObject spawnedObj = PrefabUtility.InstantiatePrefab(loadedModel) as GameObject;
                Selection.activeGameObject = spawnedObj;
                Debug.Log($"[2단계 성공] 3D 에셋이 성공적으로 자동 배치되었습니다: {fullPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Tripo3D 오류] {ex.Message}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isProcessing = false;
        }
    }

    private async Task<string> RequestTripoImageToModel(string imageUrl)
    {
        string url = "https://openapi.tripo3d.ai/v3/generation/image-to-model";

        // Tripo3D v3 규격: file 객체 내부에 type과 url 명시
        JObject json = new JObject
        {
            ["type"] = "image_to_model",
            ["model"] = "v2.5-20250123",
            ["file"] = new JObject
            {
                ["type"] = "png",
                ["url"] = imageUrl
            },
            ["format"] = "FBX"
        };

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json.ToString());
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {tripoApiKey.Trim()}");

            var asyncOp = www.SendWebRequest();
            while (!asyncOp.isDone) await Task.Delay(100);

            if (www.result != UnityWebRequest.Result.Success)
                throw new Exception($"Tripo Image-to-Model 요청 실패: {www.downloadHandler.text}");

            JObject res = JObject.Parse(www.downloadHandler.text);
            return res["data"]["task_id"].ToString();
        }
    }

    private async Task<string> PollTaskResult(string taskId)
    {
        string url = $"https://openapi.tripo3d.ai/v3/tasks/{taskId}";

        while (true)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", $"Bearer {tripoApiKey.Trim()}");
                var asyncOp = www.SendWebRequest();
                while (!asyncOp.isDone) await Task.Delay(100);

                JObject res = JObject.Parse(www.downloadHandler.text);
                string status = res["data"]["status"].ToString();

                if (status == "success")
                {
                    return res["data"]["output"]["model_urls"]["fbx"].ToString();
                }
                else if (status == "failed")
                {
                    throw new Exception("Tripo3D 메쉬 생성 실패");
                }

                await Task.Delay(3000);
            }
        }
    }

    private async Task DownloadFileAsync(string fileUrl, string savePath)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(fileUrl))
        {
            var asyncOp = www.SendWebRequest();
            while (!asyncOp.isDone) await Task.Delay(100);

            if (www.result == UnityWebRequest.Result.Success)
            {
                File.WriteAllBytes(savePath, www.downloadHandler.data);
            }
            else
            {
                throw new Exception($"파일 다운로드 실패: {www.error}");
            }
        }
    }
}