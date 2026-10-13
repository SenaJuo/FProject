using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 글꼴 예열용 글자 목록을 만든다: 게임 코드(Scripts·Editor 빌더)에 나오는 모든 글자 + 영문·숫자·기호.
/// 대사·안내·튜토리얼·장소 이름이 전부 코드 안에 있으니 코드만 훑으면 된다(주석 글자도 섞여 조금 넉넉하다).
/// 새 대사를 넣으면 다시 만든다(메뉴 Game/Build Font Warmup Chars). 결과: Resources/GameChars.txt
/// </summary>
public static class GameChars
{
    const string OutPath = "Assets/Game/Resources/GameChars.txt";

    [MenuItem("Game/Build Font Warmup Chars")]
    public static string Build()
    {
        var set = new System.Collections.Generic.SortedSet<char>();
        for (char c = ' '; c <= '~'; c++) set.Add(c);
        foreach (var path in System.IO.Directory.GetFiles("Assets/Game", "*.cs", System.IO.SearchOption.AllDirectories))
            foreach (char c in System.IO.File.ReadAllText(path))
                if (c > 127 && !char.IsControl(c) && !char.IsSurrogate(c) && c != '﻿') set.Add(c);
        System.IO.File.WriteAllText(OutPath, new string(set.ToArray()), new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(OutPath);
        return $"글자 {set.Count}개 → {OutPath}";
    }
}
