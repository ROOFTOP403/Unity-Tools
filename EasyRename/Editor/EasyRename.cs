#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Collections.Generic;

/// <summary>
/// Unity 에디터에서 선택된 에셋들의 이름을 일괄적으로 변경해주는 에디터 윈도우
/// Tools/RoofTop Studio/Easy Rename 메뉴를 통해 접근 가능
/// 
/// Original Author: Bonnate (https://github.com/bonnate)
/// Enhanced by: RoofTopKeeper
///   - 제로 패딩 기능 추가 (01, 02, 03...)
///   - Suffix 기능 추가 (접미사 지원)
///   - 기존 이름 유지 + Suffix만 추가하는 모드 지원
///   - 특정 키워드 삭제 기능 추가
///   - 키워드 충돌 경고 기능 추가 (단어 단위)
///   - 이름 변경 실패 감지 및 로깅 추가
///   - 이름 충돌 방지 (Two-Pass Rename 전략)
/// </summary>
public class EasyRenameWindow : EditorWindow
{
    private string mBaseName = ""; // 기본 이름 (접두사로 사용됨, 비어있으면 기존 이름 유지)
    private int mStartIdx = 0; // 시작 인덱스 번호
    private string mSuffix = ""; // 접미사 (인덱스 뒤에 붙을 텍스트)
    private string mRemoveKeyword = ""; // 삭제할 키워드
    private int mAssetsCount = 0; // 선택된 에셋의 개수

    /// <summary>
    /// 에디터 윈도우를 표시하는 메뉴 아이템
    /// Tools > Easy Rename 경로에 메뉴가 생성됨
    /// </summary>
    [MenuItem("Tools/RoofTop Studio/Easy Rename")]
    public static void ShowWindow()
    {
        GetWindow<EasyRenameWindow>("Easy Rename");
    }

    /// <summary>
    /// 에디터 윈도우의 GUI를 그리는 메서드
    /// Unity 에디터에서 매 프레임마다 호출됨
    /// </summary>
    private void OnGUI()
    {
        // 기본 이름 입력 필드 (예: "Enemy", "Item" 등)
        // 비어있으면 기존 에셋 이름 유지
        mBaseName = EditorGUILayout.TextField("기본 이름 (선택)", mBaseName);
        
        // 시작 인덱스 입력 필드 (예: 0이면 Enemy00, Enemy01... / 1이면 Enemy01, Enemy02...)
        // Base Name이 비어있으면 무시됨
        GUI.enabled = !string.IsNullOrEmpty(mBaseName);
        mStartIdx = EditorGUILayout.IntField("시작 번호", mStartIdx);
        GUI.enabled = true;

        // 접미사 입력 필드 (예: "_Loop", "_C", "_L", "_Tile" 등)
        // 비어있으면 아무것도 붙지 않음
        mSuffix = EditorGUILayout.TextField("접미사 (선택)", mSuffix);

        GUILayout.Space(5);

        // 구분선
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        
        // 키워드 삭제 입력 필드 (예: "_old", "(1)", "Copy" 등)
        // 비어있으면 삭제 기능 사용 안함
        mRemoveKeyword = EditorGUILayout.TextField("삭제할 키워드 (선택)", mRemoveKeyword);

        // 키워드 충돌 경고 표시
        DisplayConflictWarning();

        GUILayout.Space(5);

        // 미리보기 예시 표시
        DisplayPreview();

        GUILayout.Space(10);

        // 버튼 활성화 조건 확인
        // - (Base Name이 있거나 Suffix가 있거나 Remove Keyword가 있어야 함) AND 선택된 오브젝트가 1개 이상
        bool canRename = (!string.IsNullOrEmpty(mBaseName) || !string.IsNullOrEmpty(mSuffix) || !string.IsNullOrEmpty(mRemoveKeyword)) 
                         && Selection.objects.Length > 0;

        GUI.enabled = canRename; // 조건에 따라 버튼 활성화/비활성화

        // "선택한 에셋 이름 변경" 버튼 클릭 시 이름 변경 실행
        if (GUILayout.Button("선택한 에셋 이름 변경"))
            RenameSelectedAssets();

        GUI.enabled = true; // 다른 GUI 요소를 위해 활성화 상태 복원

        GUILayout.Space(20);

        // 하단 정보 영역 (수평 레이아웃)
        GUILayout.BeginHorizontal();

        // 제작자 정보 표시
        EditorGUILayout.LabelField("원작: Bonnate | 개선: RoofTopKeeper");

        // GitHub 링크 버튼 (하이퍼링크 스타일)
        if (GUILayout.Button("깃허브", GetHyperlinkLabelStyle()))
        {
            OpenURL("https://github.com/bonnate");
        }

        // 블로그 링크 버튼 (하이퍼링크 스타일)
        if (GUILayout.Button("블로그", GetHyperlinkLabelStyle()))
        {
            OpenURL("https://bonnate.tistory.com/");
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// 문자열에서 특정 키워드가 완전한 단어로 포함되어 있는지 확인
    /// 예: "Loop"를 검색할 때
    ///     "_Loop" → true (단어 경계)
    ///     "LoopTexture" → true (단어 경계)
    ///     "Lo" → false (부분 일치)
    /// </summary>
    /// <param name="text">검색할 문자열</param>
    /// <param name="keyword">찾을 키워드</param>
    /// <returns>완전한 단어로 포함되어 있으면 true</returns>
    private bool ContainsAsWholeWord(string text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
            return false;

        // 정규식 패턴: \b는 단어 경계(word boundary)를 의미
        // 알파벳, 숫자가 아닌 문자(_등 특수문자)를 경계로 인식
        // Regex.Escape로 특수문자 이스케이프 처리
        string pattern = @"\b" + Regex.Escape(keyword) + @"\b";
        
        return Regex.IsMatch(text, pattern);
    }

    /// <summary>
    /// Remove Keyword와 Base Name/Suffix 간의 충돌을 감지하고 경고 표시
    /// 단어 단위로 완전히 일치할 때만 경고
    /// </summary>
    private void DisplayConflictWarning()
    {
        // Remove Keyword가 비어있으면 검사 불필요
        if (string.IsNullOrEmpty(mRemoveKeyword))
            return;

        bool hasConflict = false;
        string conflictMessage = "";

        // Base Name에 Remove Keyword가 완전한 단어로 포함되어 있는지 확인
        if (!string.IsNullOrEmpty(mBaseName) && ContainsAsWholeWord(mBaseName, mRemoveKeyword))
        {
            hasConflict = true;
            conflictMessage = $"경고: '삭제할 키워드'가 '기본 이름'에 포함되어 있습니다!\n" +
                            $"기본 이름 모드에서는 삭제할 키워드가 무시됩니다.";
        }
        // Suffix에 Remove Keyword가 완전한 단어로 포함되어 있는지 확인
        else if (!string.IsNullOrEmpty(mSuffix) && ContainsAsWholeWord(mSuffix, mRemoveKeyword))
        {
            hasConflict = true;
            conflictMessage = $"경고: '삭제할 키워드'가 '접미사'에 포함되어 있습니다!\n" +
                            $"추가한 접미사가 의도치 않게 삭제될 수 있습니다.";
        }

        // 충돌이 있으면 경고 메시지 표시
        if (hasConflict)
        {
            EditorGUILayout.HelpBox(conflictMessage, MessageType.Warning);
        }
    }

    /// <summary>
    /// 미리보기 UI를 표시하는 메서드
    /// </summary>
    private void DisplayPreview()
    {
        if (!string.IsNullOrEmpty(mBaseName))
        {
            // Base Name이 있는 경우: BaseName + Index + Suffix
            string previewName = mBaseName + "01" + mSuffix;
            EditorGUILayout.HelpBox($"예시: {previewName}", MessageType.Info);
        }
        else if (!string.IsNullOrEmpty(mSuffix) || !string.IsNullOrEmpty(mRemoveKeyword))
        {
            // Base Name이 없고 Suffix 또는 Remove Keyword가 있는 경우
            string preview = "[기존 이름]";
            
            // 키워드 삭제 시뮬레이션
            if (!string.IsNullOrEmpty(mRemoveKeyword))
            {
                string sampleName = "OldTexture_old";
                string processedName = sampleName.Replace(mRemoveKeyword, "");
                
                // Suffix도 추가하는 경우
                if (!string.IsNullOrEmpty(mSuffix))
                {
                    processedName += mSuffix;
                }
                
                preview = $"{sampleName} → {processedName}";
            }
            else
            {
                preview += mSuffix;
            }
            
            EditorGUILayout.HelpBox($"예시: {preview}", MessageType.Info);
        }
        else
        {
            // 모두 비어있는 경우
            EditorGUILayout.HelpBox("기본 이름, 접미사, 또는 삭제할 키워드를 입력하세요.", MessageType.Warning);
        }
    }

    /// <summary>
    /// 선택된 에셋들의 이름을 일괄 변경하는 메서드
    /// Two-Pass 전략 사용:
    ///   1단계: 모든 파일을 임시 이름으로 변경 (충돌 방지)
    ///   2단계: 임시 이름을 최종 이름으로 변경
    /// 
    /// 모드 1: BaseName이 있을 때
    ///   예시: BaseName="Texture", StartIndex=1, Suffix="_Loop"
    ///   결과: Texture01_Loop, Texture02_Loop, Texture03_Loop
    /// 
    /// 모드 2: BaseName이 없고 Suffix/Remove Keyword만 있을 때
    ///   예시: BaseName="", Suffix="_Loop", RemoveKeyword="_old"
    ///   결과: [기존이름1_old] → [기존이름1_Loop], [기존이름2_old] → [기존이름2_Loop]
    /// </summary>
    private void RenameSelectedAssets()
    {
        Object[] selectedAssets = Selection.objects;

        // 선택된 에셋이 없으면 경고 메시지 출력 후 종료
        if (selectedAssets.Length == 0)
        {
            Debug.LogWarning("No assets selected.");
            return;
        }

        mAssetsCount = selectedAssets.Length;

        // Base Name이 있는지 확인 (이름 변경 모드 결정)
        bool useBaseName = !string.IsNullOrEmpty(mBaseName);

        int digitCount = 0;
        if (useBaseName)
        {
            // 필요한 자릿수 계산 (마지막 인덱스 번호 기준)
            // 최소 2자리를 보장
            int maxIndex = mStartIdx + mAssetsCount - 1;
            digitCount = Mathf.Max(2, maxIndex.ToString().Length);
        }

        // 성공/실패 카운트 및 실패 목록
        int successCount = 0;
        int failCount = 0;
        List<string> failedAssets = new List<string>();

        // 에셋 정보를 저장할 구조체 리스트
        List<AssetRenameInfo> renameInfos = new List<AssetRenameInfo>();

        // ===== 1단계: 최종 이름 계산 및 임시 이름으로 변경 =====
        Debug.Log("[EasyRename] 1단계: 임시 이름으로 변경 시작...");
        
        for (int i = 0; i < mAssetsCount; i++)
        {
            Object asset = selectedAssets[i];
            string oldName = asset.name;
            string finalName;

            if (useBaseName)
            {
                // 모드 1: Base Name을 사용한 완전한 이름 변경
                int currentIndex = mStartIdx + i;
                string indexStr = currentIndex.ToString("D" + digitCount);
                finalName = mBaseName + indexStr + mSuffix;
            }
            else
            {
                // 모드 2: 기존 이름 유지 + 키워드 삭제 + Suffix 추가
                string currentName = asset.name;
                
                if (!string.IsNullOrEmpty(mRemoveKeyword))
                {
                    currentName = currentName.Replace(mRemoveKeyword, "");
                }
                
                finalName = currentName + mSuffix;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);

            if (string.IsNullOrEmpty(assetPath))
            {
                failCount++;
                failedAssets.Add($"{oldName} (경로 없음)");
                Debug.LogWarning($"[EasyRename] 에셋 경로를 찾을 수 없습니다: {oldName}");
                continue;
            }

            // 임시 이름 생성 (충돌 방지용 - GUID 사용)
            string tempName = $"TEMP_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}_{i}";

            // Undo 기록
            Undo.RecordObject(asset, "Rename Asset - Step 1");

            // 임시 이름으로 변경
            string errorMessage = AssetDatabase.RenameAsset(assetPath, tempName);

            if (string.IsNullOrEmpty(errorMessage))
            {
                // 임시 변경 성공 - 정보 저장
                renameInfos.Add(new AssetRenameInfo
                {
                    asset = asset,
                    oldName = oldName,
                    tempName = tempName,
                    finalName = finalName,
                    assetPath = assetPath
                });
                
                Debug.Log($"[EasyRename] 1단계 성공: {oldName} → {tempName}");
            }
            else
            {
                failCount++;
                string fileExtension = System.IO.Path.GetExtension(assetPath);
                failedAssets.Add($"{oldName}{fileExtension} - 1단계 실패: {errorMessage}");
                Debug.LogError($"[EasyRename] 1단계 실패: {oldName}\n사유: {errorMessage}");
            }
        }

        // AssetDatabase 새로고침 (1단계 완료)
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ===== 2단계: 임시 이름을 최종 이름으로 변경 =====
        Debug.Log("[EasyRename] 2단계: 최종 이름으로 변경 시작...");

        foreach (var info in renameInfos)
        {
            // 업데이트된 경로 가져오기 (1단계에서 이름이 변경되었으므로)
            string currentPath = AssetDatabase.GetAssetPath(info.asset);

            // Undo 기록
            Undo.RecordObject(info.asset, "Rename Asset - Step 2");

            // 최종 이름으로 변경
            string errorMessage = AssetDatabase.RenameAsset(currentPath, info.finalName);

            if (string.IsNullOrEmpty(errorMessage))
            {
                // 성공
                info.asset.name = info.finalName;
                EditorUtility.SetDirty(info.asset);
                successCount++;
                Debug.Log($"[EasyRename] 2단계 성공: {info.tempName} → {info.finalName} (원래: {info.oldName})");
            }
            else
            {
                // 실패 - 원래 이름으로 복구 시도
                failCount++;
                string fileExtension = System.IO.Path.GetExtension(currentPath);
                failedAssets.Add($"{info.oldName}{fileExtension} - 2단계 실패: {errorMessage}");
                Debug.LogError($"[EasyRename] 2단계 실패: {info.tempName} → {info.finalName}\n사유: {errorMessage}");
                
                // 원래 이름으로 되돌리기 시도
                string rollbackError = AssetDatabase.RenameAsset(currentPath, info.oldName);
                if (string.IsNullOrEmpty(rollbackError))
                {
                    Debug.Log($"[EasyRename] 롤백 성공: {info.tempName} → {info.oldName}");
                }
            }
        }

        // 모든 변경 사항을 디스크에 저장
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        // 최종 결과 메시지 출력
        if (failCount == 0)
        {
            Debug.Log($"<color=green>[EasyRename] ✅ 완료: {successCount}개의 에셋 이름이 변경되었습니다.</color>");
        }
        else
        {
            Debug.LogWarning($"[EasyRename] ⚠️ 부분 완료:\n" +
                           $"성공: {successCount}개\n" +
                           $"실패: {failCount}개\n\n" +
                           $"실패 목록:\n{string.Join("\n", failedAssets)}");
            
            EditorUtility.DisplayDialog(
                "Easy Rename - 일부 실패",
                $"성공: {successCount}개\n실패: {failCount}개\n\n" +
                $"자세한 내용은 Console 창을 확인하세요.",
                "확인"
            );
        }
    }

    /// <summary>
    /// 에셋 이름 변경 정보를 담는 구조체
    /// </summary>
    private struct AssetRenameInfo
    {
        public Object asset;      // Unity 오브젝트
        public string oldName;    // 원래 이름
        public string tempName;   // 임시 이름 (1단계)
        public string finalName;  // 최종 이름 (2단계)
        public string assetPath;  // 에셋 경로
    }

    /// <summary>
    /// 하이퍼링크처럼 보이는 GUI 스타일을 생성하는 메서드
    /// 파란색 텍스트로 링크처럼 보이게 만듦
    /// </summary>
    /// <returns>하이퍼링크 스타일의 GUIStyle</returns>
    private GUIStyle GetHyperlinkLabelStyle()
    {
        // 기본 라벨 스타일을 기반으로 새 스타일 생성
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.normal.textColor = new Color(0f, 0.5f, 1f); // 파란색
        style.stretchWidth = false; // 텍스트 길이만큼만 너비 설정
        style.wordWrap = false; // 줄바꿈 비활성화
        return style;
    }

    /// <summary>
    /// 제공된 URL을 기본 웹 브라우저로 여는 메서드
    /// </summary>
    /// <param name="url">열고자 하는 URL</param>
    private void OpenURL(string url)
    {
        Application.OpenURL(url);
    }
}

#endif
