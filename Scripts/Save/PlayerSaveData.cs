using System;
using System.Collections.Generic;

[Serializable]
public class PlayerSaveData
{
    public const int CURRENT_VERSION = 1;

    public int saveVersion = CURRENT_VERSION;
    public Currencies currencies = new();
    public List<int> unlockedSkinIds = new();
    public EquippedSkins equippedSkins = new();
    public GameSettings settings = new();
    public string keyBindings = "";
    public PlayStats stats = new();
}

[Serializable]
public class Currencies
{
    public int coin;
    public int cash;
}

[Serializable]
public class EquippedSkins
{
    public const int NONE = -1;

    public int head = NONE;
    public int top = NONE;
    public int bottom = NONE;
    public int shoes = NONE;
    public int bag = NONE;
}

[Serializable]
public class PlayStats
{
    public int totalGamesPlayed;
    public int mafiaPlayed;
    public int mafiaWins;
    public int citizenPlayed;
    public int citizenWins;
    public int totalCleanCount;
    public int totalTrashCount;
    public int totalItemsOrganized;
    public int totalItemsMessUp;
}
