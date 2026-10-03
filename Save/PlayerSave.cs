using System;
using System.IO;
using System.Text;
using Steamworks;
using UnityEngine;

public static class PlayerSave
{
    private const string FILE_NAME = "player_save.json";

    private static PlayerSaveData _data;

    public static PlayerSaveData Data => _data ??= Load();

    private static string LocalPath => Path.Combine(Application.persistentDataPath, FILE_NAME);

    public static void Save()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(Data, true));

        if (SteamClient.IsValid)
        {
            if (SteamRemoteStorage.FileWrite(FILE_NAME, bytes)) return;

            Debug.LogWarning("[PlayerSave] Steam 클라우드 저장에 실패해 로컬에 저장합니다");
        }

        File.WriteAllBytes(LocalPath, bytes);
    }

    private static PlayerSaveData Load()
    {
        byte[] bytes = SteamClient.IsValid ? SteamRemoteStorage.FileRead(FILE_NAME) : null;
        if (bytes == null && File.Exists(LocalPath)) bytes = File.ReadAllBytes(LocalPath);
        if (bytes == null) return new PlayerSaveData();

        try
        {
            return JsonUtility.FromJson<PlayerSaveData>(Encoding.UTF8.GetString(bytes)) ?? new PlayerSaveData();
        }
        catch (ArgumentException e)
        {
            Debug.LogError($"[PlayerSave] 저장 파일을 읽지 못해 새로 시작합니다: {e.Message}");
            return new PlayerSaveData();
        }
    }
}
