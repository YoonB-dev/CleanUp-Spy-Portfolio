using System.Text;

/// <summary>
/// 초대 코드. SteamID64는 17자리라 손으로 옮기기 어려우므로 계정 번호만 남겨 7자로 줄인다.
/// 개인 계정 SteamID64는 모두 같은 값에 32비트 계정 번호를 더한 형태라 앞부분은 버려도 된다.
/// 길이가 정확히 LENGTH가 아니면 받지 않는다. 입력칸이 7칸 고정이라 잘린 값이 우연히 유효해지면 엉뚱한 계정에 붙는다.
/// </summary>
public static class InviteCode
{
    public const int LENGTH = 7;   // 32비트를 5비트씩 담으려면 7자가 최소다

    private const ulong INDIVIDUAL_BASE = 76561197960265728UL;

    // Crockford Base32. 손으로 옮길 때 헷갈리는 I L O U 제외
    private const string ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string FromSteamId(ulong steamId)
    {
        if (steamId < INDIVIDUAL_BASE) return steamId.ToString();   // 개인 계정이 아니면 손대지 않는다

        ulong account = steamId - INDIVIDUAL_BASE;
        char[] code = new char[LENGTH];

        for (int i = LENGTH - 1; i >= 0; i--)
        {
            code[i] = ALPHABET[(int)(account & 31)];
            account >>= 5;
        }

        return new string(code);
    }

    public static bool TryParse(string code, out ulong steamId)
    {
        steamId = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;

        string cleaned = Normalize(code);
        if (cleaned.Length != LENGTH) return false;

        ulong account = 0;
        foreach (char c in cleaned)
        {
            int index = ALPHABET.IndexOf(c);
            if (index < 0) return false;

            account = (account << 5) | (uint)index;
        }

        steamId = INDIVIDUAL_BASE + account;
        return true;
    }

    /// <summary>대문자로 맞추고 흔한 오독을 고친다. 알파벳에 없는 글자는 버린다</summary>
    public static string Normalize(string code)
    {
        StringBuilder builder = new(code.Length);

        foreach (char raw in code)
        {
            char c = Sanitize(raw);
            if (c != '\0') builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>한 글자를 코드에 쓸 수 있게 고친다. 못 쓰는 글자면 0을 준다</summary>
    public static char Sanitize(char raw)
    {
        char c = char.ToUpperInvariant(raw) switch
        {
            'O' => '0',
            'I' or 'L' => '1',
            'U' => 'V',
            char other => other
        };

        return ALPHABET.IndexOf(c) >= 0 ? c : '\0';
    }
}
