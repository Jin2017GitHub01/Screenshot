using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Screenshot;

internal enum AppLanguage
{
    English,
    Korean
}

/// <summary>
/// Manages English / Korean UI strings and persists the selected language
/// in %LocalAppData%\CaptureEverything\settings.json.
/// </summary>
internal static class L10n
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaptureEverything",
        "settings.json");

    public static AppLanguage Current { get; private set; } = LoadInitialLanguage();

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        SaveLanguage(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string en, string ko) =>
        Current == AppLanguage.Korean ? ko : en;

    // --- Tray menu ---
    public static string TrayCaptureScreen => Get("Capture Screen", "화면 캡처");
    public static string TrayCaptureAudio => Get("Capture Audio", "오디오 캡처");
    public static string TrayStopAudio => Get("Stop Audio && Save", "오디오 중지 및 저장");
    public static string TrayCaptureVideo => Get("Capture Video", "비디오 캡처");
    public static string TrayStopVideo => Get("Stop Video && Save", "비디오 중지 및 저장");
    public static string TrayOpen => Get("Open", "열기");
    public static string TraySetting => Get("Setting", "설정");
    public static string TrayExit => Get("Exit", "종료");

    public static string TrayIdleTooltip => Get(
        "Capture Everything – click to capture screen",
        "Capture Everything – 클릭하여 화면 캡처");
    public static string TrayRecordingAudioTooltip => Get(
        "Capture Everything – recording audio…",
        "Capture Everything – 오디오 녹음 중…");
    public static string TrayRecordingVideoTooltip => Get(
        "Capture Everything – recording video…",
        "Capture Everything – 비디오 녹화 중…");

    public static string TrayStillRunningTitle => Get(
        "Capture Everything is still running",
        "Capture Everything이 계속 실행 중입니다");
    public static string TrayStillRunningText => Get(
        "Click the tray icon to capture a screenshot. Right-click it for more options.",
        "트레이 아이콘을 클릭하면 화면을 캡처합니다. 우클릭하면 더 많은 메뉴를 볼 수 있습니다.");

    // --- Main window header ---
    public static string AppSubtitle => Get("Catch every pixel", "모든 순간을 선명하게 캡처하세요");
    public static string RecordAudio => Get("Capture Audio", "오디오 캡처");
    public static string RecordAudioTip => Get(
        "Record the sound playing on this computer (Ctrl+R)",
        "컴퓨터에서 재생 중인 소리를 녹음합니다 (Ctrl+R)");
    public static string StopAudioTip => Get(
        "Stop recording and save as MP3 (Ctrl+R)",
        "녹음을 중지하고 MP3로 저장합니다 (Ctrl+R)");
    public static string RecordVideo => Get("Capture Video", "비디오 캡처");
    public static string RecordVideoTip => Get(
        "Record screen video with system audio (Ctrl+Shift+R)",
        "시스템 오디오와 함께 화면 비디오를 녹화합니다 (Ctrl+Shift+R)");
    public static string StopVideoTip => Get(
        "Stop video recording and save as MP4 (Ctrl+Shift+R)",
        "비디오 녹화를 중지하고 MP4로 저장합니다 (Ctrl+Shift+R)");
    public static string NewCapture => Get("Capture Screen", "화면 캡처");
    public static string NewCaptureTip => Get("Capture screen region (Ctrl+N)", "화면 영역 캡처 (Ctrl+N)");
    public static string StopPrefix => Get("Stop", "중지");

    // --- Empty state ---
    public static string EmptyTitle => Get("Nothing caught yet", "아직 캡처된 화면이 없습니다");
    public static string EmptyPrefix => Get("Click ", "");
    public static string EmptyOrPress => Get(" or press ", " 버튼을 누르거나 ");
    public static string EmptySuffix => Get(
        ", then drag a rectangle anywhere on your screens to catch it.",
        " 키를 누른 뒤, 화면에서 원하는 영역을 드래그하여 캡처하세요.");

    // --- Annotation toolbar ---
    public static string ToolText => Get("Text", "텍스트");
    public static string ToolTextTip => Get(
        "Add text (T) – then click on the image to type",
        "텍스트 추가 (T) – 이미지를 클릭하여 입력하세요");
    public static string ToolArrow => Get("Arrow", "화살표");
    public static string ToolArrowTip => Get(
        "Add arrow (A) – then drag on the image",
        "화살표 추가 (A) – 이미지 위에서 드래그하세요");
    public static string SwatchTip => Get("Color (text & arrows)", "색상 (텍스트 및 화살표)");
    public static string SmallerTip => Get("Smaller (text size / arrow thickness)", "작게 (글자 크기 / 화살표 굵기)");
    public static string LargerTip => Get("Larger (text size / arrow thickness)", "크게 (글자 크기 / 화살표 굵기)");
    public static string UndoTip => Get("Undo last annotation (Ctrl+Z)", "마지막 주석 실행 취소 (Ctrl+Z)");
    public static string ClearTip => Get("Remove all annotations", "모든 주석 지우기");
    public static string ZoomOutTip => Get("Zoom out (Mouse wheel down)", "축소 (마우스 휠 아래로)");
    public static string ZoomResetTip => Get(
        "Mouse wheel to zoom • Click to reset to 100% (Ctrl+0)",
        "마우스 휠로 확대/축소 • 클릭하면 100%로 초기화 (Ctrl+0)");
    public static string ZoomInTip => Get("Zoom in (Mouse wheel up)", "확대 (마우스 휠 위로)");

    // --- Footer ---
    public static string Ready => Get("Ready", "준비됨");
    public static string Copy => Get("Copy", "복사");
    public static string CopyTip => Get("Copy to clipboard (Ctrl+C)", "클립보드에 복사 (Ctrl+C)");
    public static string SaveAs => Get("Save As…", "다른 이름으로 저장…");
    public static string SaveAsTip => Get("Save as image file (Ctrl+S)", "이미지 파일로 저장 (Ctrl+S)");

    // --- Toasts & dialogs ---
    public static string ToastCaughtCopied => Get(
        "Gotcha! Caught & copied to clipboard",
        "캡처 완료! 클립보드에 복사되었습니다");
    public static string ToastCopied => Get("Copied to clipboard", "클립보드에 복사되었습니다");
    public static string ToastSaved(string fileName) => Get($"Saved {fileName}", $"{fileName} 저장됨");
    public static string ToastTextHint => Get(
        "Click anywhere on the image to add text",
        "이미지에서 원하는 위치를 클릭하여 텍스트를 추가하세요");
    public static string ToastArrowHint => Get(
        "Drag on the image to draw an arrow",
        "이미지 위에서 드래그하여 화살표를 그리세요");
    public static string ToastRecordingAudio => Get("Recording system audio…", "시스템 오디오 녹음 중…");
    public static string ToastNothingRecorded => Get("Nothing was recorded", "녹음된 내용이 없습니다");
    public static string ToastAudioDiscarded => Get("Recording discarded", "녹음이 취소되었습니다");
    public static string ToastVideoDiscarded => Get("Video recording discarded", "비디오 녹화가 취소되었습니다");
    public static string SavingMp3 => Get("Saving MP3…", "MP3 저장 중…");
    public static string SavingMp4 => Get("Saving MP4…", "MP4 저장 중…");

    public static string DiscardAudioQuestion => Get("Discard this recording?", "이 녹음을 삭제하시겠습니까?");
    public static string DiscardVideoQuestion => Get("Discard this video recording?", "이 비디오 녹화를 삭제하시겠습니까?");
    public static string ExitWhileAudioRecording => Get(
        "An audio recording is in progress.\n\nSave it before exiting?",
        "오디오 녹음이 진행 중입니다.\n\n종료하기 전에 저장하시겠습니까?");
    public static string ExitWhileVideoRecording => Get(
        "A video recording is in progress.\n\nSave it before exiting?",
        "비디오 녹화가 진행 중입니다.\n\n종료하기 전에 저장하시겠습니까?");

    // --- Region selector ---
    public static string SelectorScreenPrimary => Get("Drag to catch a region", "드래그하여 영역 캡처");
    public static string SelectorScreenSecondary => Get(
        "   •   Esc or right-click to cancel",
        "   •   Esc 또는 우클릭으로 취소");
    public static string SelectorVideoPrimary => Get("Drag a region to record", "드래그하여 영역 녹화");
    public static string SelectorVideoSecondary => Get(
        "   •   Click or Enter for full screen   •   Esc to cancel",
        "   •   클릭 또는 Enter로 전체 화면   •   Esc로 취소");

    // --- Recording overlay ---
    public static string OverlayDragTip => Get(
        "Drag to move • Excluded from video capture",
        "드래그하여 이동 • 비디오 녹화에는 표시되지 않습니다");
    public static string OverlayAudioMeterTip => Get("System audio level", "시스템 오디오 음량");

    // --- Settings window ---
    public static string SettingsTitle => Get("Setting", "설정");
    public static string SettingsLanguageHeader => Get("Language", "언어");
    public static string SettingsLanguageDesc => Get(
        "Choose the display language for menus, buttons, and messages.",
        "메뉴, 버튼 및 메시지에 사용할 표시 언어를 선택하세요.");
    public static string SettingsClose => Get("Close", "닫기");

    private sealed class SettingsData
    {
        public string? Language { get; set; }
    }

    private static AppLanguage LoadInitialLanguage()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsPath));
                if (string.Equals(data?.Language, "ko", StringComparison.OrdinalIgnoreCase))
                    return AppLanguage.Korean;
                if (string.Equals(data?.Language, "en", StringComparison.OrdinalIgnoreCase))
                    return AppLanguage.English;
            }
        }
        catch { /* ignore corrupt settings */ }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Korean
            : AppLanguage.English;
    }

    private static void SaveLanguage(AppLanguage language)
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new SettingsData
            {
                Language = language == AppLanguage.Korean ? "ko" : "en"
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best effort */ }
    }
}

