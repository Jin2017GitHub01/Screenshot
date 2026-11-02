using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Screenshot;

internal enum AppLanguage
{
    English,
    Korean,
    Japanese,
    ChineseSimplified,
    ChineseTraditional,
    Hindi,
    Urdu,
    Russian,
    Spanish,
    French,
    German,
    Portuguese
}

/// <summary>
/// Manages multilingual UI strings and persists the selected language
/// in %LocalAppData%\CaptureEverything\settings.json.
/// </summary>
internal static class L10n
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaptureEverything",
        "settings.json");

    public static IReadOnlyList<(AppLanguage Language, string Code, string NativeName)> SupportedLanguages { get; } =
    [
        (AppLanguage.English,            "en",      "English"),
        (AppLanguage.Korean,             "ko",      "한국어"),
        (AppLanguage.Japanese,           "ja",      "日本語"),
        (AppLanguage.ChineseSimplified,  "zh-Hans", "简体中文"),
        (AppLanguage.ChineseTraditional, "zh-Hant", "繁體中文"),
        (AppLanguage.Hindi,              "hi",      "हिन्दी"),
        (AppLanguage.Urdu,               "ur",      "اردو"),
        (AppLanguage.Russian,            "ru",      "Русский"),
        (AppLanguage.Spanish,            "es",      "Español"),
        (AppLanguage.French,             "fr",      "Français"),
        (AppLanguage.German,             "de",      "Deutsch"),
        (AppLanguage.Portuguese,         "pt",      "Português"),
    ];

    public static AppLanguage Current { get; private set; } = LoadInitialLanguage();
    public static bool UsePrintScreenKey { get; private set; } = LoadInitialPrintScreenKey();

    public static event EventHandler? LanguageChanged;
    public static event EventHandler? PrintScreenKeyChanged;

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        SaveSettings();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void SetUsePrintScreenKey(bool enabled)
    {
        if (UsePrintScreenKey == enabled) return;
        UsePrintScreenKey = enabled;
        SaveSettings();
        PrintScreenKeyChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(
        string en,
        string ko,
        string? ja = null,
        string? zhHans = null,
        string? zhHant = null,
        string? hi = null,
        string? ur = null,
        string? ru = null,
        string? es = null,
        string? fr = null,
        string? de = null,
        string? pt = null) =>
        Current switch
        {
            AppLanguage.Korean             => ko,
            AppLanguage.Japanese           => ja ?? en,
            AppLanguage.ChineseSimplified  => zhHans ?? en,
            AppLanguage.ChineseTraditional => zhHant ?? zhHans ?? en,
            AppLanguage.Hindi              => hi ?? en,
            AppLanguage.Urdu               => ur ?? en,
            AppLanguage.Russian            => ru ?? en,
            AppLanguage.Spanish            => es ?? en,
            AppLanguage.French             => fr ?? en,
            AppLanguage.German             => de ?? en,
            AppLanguage.Portuguese         => pt ?? en,
            _                              => en
        };

    // --- Tray menu ---
    public static string TrayCaptureScreen => Get(
        en: "Capture Screen",
        ko: "화면 캡처",
        ja: "画面をキャプチャ",
        zhHans: "屏幕截图",
        zhHant: "擷取畫面",
        hi: "स्क्रीन कैप्चर करें",
        ur: "اسکرین کیپچر کریں",
        ru: "Снимок экрана",
        es: "Capturar pantalla",
        fr: "Capturer l’écran",
        de: "Bildschirm aufnehmen",
        pt: "Capturar tela");

    public static string TrayCaptureAudio => Get(
        en: "Capture Audio",
        ko: "오디오 캡처",
        ja: "音声を録音",
        zhHans: "录制音频",
        zhHant: "錄製音訊",
        hi: "ऑडियो रिकॉर्ड करें",
        ur: "آڈیو ریکارڈ کریں",
        ru: "Запись аудио",
        es: "Grabar audio",
        fr: "Enregistrer l’audio",
        de: "Audio aufnehmen",
        pt: "Gravar áudio");

    public static string TrayStopAudio => Get(
        en: "Stop Audio && Save",
        ko: "오디오 중지 및 저장",
        ja: "録音を停止して保存",
        zhHans: "停止录音并保存",
        zhHant: "停止錄音並儲存",
        hi: "ऑडियो रोकें और सहेजें",
        ur: "آڈیو روکیں اور محفوظ کریں",
        ru: "Остановить и сохранить аудио",
        es: "Detener audio y guardar",
        fr: "Arrêter l’audio et enregistrer",
        de: "Audio stoppen && speichern",
        pt: "Parar áudio e salvar");

    public static string TrayCaptureVideo => Get(
        en: "Capture Video",
        ko: "비디오 캡처",
        ja: "動画を録画",
        zhHans: "录制视频",
        zhHant: "錄製影片",
        hi: "वीडियो रिकॉर्ड करें",
        ur: "ویڈیو ریکارڈ کریں",
        ru: "Запись видео",
        es: "Grabar video",
        fr: "Enregistrer la vidéo",
        de: "Video aufnehmen",
        pt: "Gravar vídeo");

    public static string TrayStopVideo => Get(
        en: "Stop Video && Save",
        ko: "비디오 중지 및 저장",
        ja: "録画を停止して保存",
        zhHans: "停止录像并保存",
        zhHant: "停止錄影並儲存",
        hi: "वीडियो रोकें और सहेजें",
        ur: "ویڈیو روکیں اور محفوظ کریں",
        ru: "Остановить и сохранить видео",
        es: "Detener video y guardar",
        fr: "Arrêter la vidéo et enregistrer",
        de: "Video stoppen && speichern",
        pt: "Parar vídeo e salvar");

    public static string TrayOpen => Get(
        en: "Open",
        ko: "열기",
        ja: "開く",
        zhHans: "打开",
        zhHant: "開啟",
        hi: "खोलें",
        ur: "کھولیں",
        ru: "Открыть",
        es: "Abrir",
        fr: "Ouvrir",
        de: "Öffnen",
        pt: "Abrir");

    public static string TraySetting => Get(
        en: "Setting",
        ko: "설정",
        ja: "設定",
        zhHans: "设置",
        zhHant: "設定",
        hi: "सेटिंग्स",
        ur: "ترتیبات",
        ru: "Настройки",
        es: "Configuración",
        fr: "Paramètres",
        de: "Einstellungen",
        pt: "Configurações");

    public static string TrayExit => Get(
        en: "Exit",
        ko: "종료",
        ja: "終了",
        zhHans: "退出",
        zhHant: "結束",
        hi: "बाहर निकलें",
        ur: "بند کریں",
        ru: "Выход",
        es: "Salir",
        fr: "Quitter",
        de: "Beenden",
        pt: "Sair");

    public static string TrayIdleTooltip => Get(
        en: "Capture Everything – click to capture screen",
        ko: "Capture Everything – 클릭하여 화면 캡처",
        ja: "Capture Everything – クリックして画面をキャプチャ",
        zhHans: "Capture Everything – 点击截取屏幕",
        zhHant: "Capture Everything – 點擊擷取畫面",
        hi: "Capture Everything – स्क्रीन कैप्चर करने के लिए क्लिक करें",
        ur: "Capture Everything – اسکرین کیپچر کرنے کے لیے کلک کریں",
        ru: "Capture Everything – нажмите для снимка экрана",
        es: "Capture Everything – clic para capturar pantalla",
        fr: "Capture Everything – cliquez pour capturer l’écran",
        de: "Capture Everything – klicken für Bildschirmaufnahme",
        pt: "Capture Everything – clique para capturar a tela");

    public static string TrayRecordingAudioTooltip => Get(
        en: "Capture Everything – recording audio…",
        ko: "Capture Everything – 오디오 녹음 중…",
        ja: "Capture Everything – 音声を録音中…",
        zhHans: "Capture Everything – 正在录制音频…",
        zhHant: "Capture Everything – 正在錄製音訊…",
        hi: "Capture Everything – ऑडियो रिकॉर्ड हो रहा है…",
        ur: "Capture Everything – آڈیو ریکارڈ ہو رہا ہے…",
        ru: "Capture Everything – идёт запись аудио…",
        es: "Capture Everything – grabando audio…",
        fr: "Capture Everything – enregistrement audio…",
        de: "Capture Everything – Audioaufnahme läuft…",
        pt: "Capture Everything – gravando áudio…");

    public static string TrayRecordingVideoTooltip => Get(
        en: "Capture Everything – recording video…",
        ko: "Capture Everything – 비디오 녹화 중…",
        ja: "Capture Everything – 動画を録画中…",
        zhHans: "Capture Everything – 正在录制视频…",
        zhHant: "Capture Everything – 正在錄製影片…",
        hi: "Capture Everything – वीडियो रिकॉर्ड हो रहा है…",
        ur: "Capture Everything – ویڈیو ریکارڈ ہو رہی ہے…",
        ru: "Capture Everything – идёт запись видео…",
        es: "Capture Everything – grabando video…",
        fr: "Capture Everything – enregistrement vidéo…",
        de: "Capture Everything – Videoaufnahme läuft…",
        pt: "Capture Everything – gravando vídeo…");

    public static string TrayStillRunningTitle => Get(
        en: "Capture Everything is still running",
        ko: "Capture Everything이 계속 실행 중입니다",
        ja: "Capture Everything はバックグラウンドで実行中です",
        zhHans: "Capture Everything 仍在后台运行",
        zhHant: "Capture Everything 仍在背景執行",
        hi: "Capture Everything अभी भी बैकग्राउंड में चल रहा है",
        ur: "Capture Everything اب بھی پس منظر میں چل رہا ہے",
        ru: "Capture Everything продолжает работать в фоне",
        es: "Capture Everything sigue en ejecución",
        fr: "Capture Everything est toujours en cours d’exécution",
        de: "Capture Everything läuft weiter im Hintergrund",
        pt: "Capture Everything continua em execução");

    public static string TrayStillRunningText => Get(
        en: "Click the tray icon to capture a screenshot. Right-click it for more options.",
        ko: "트레이 아이콘을 클릭하면 화면을 캡처합니다. 우클릭하면 더 많은 메뉴를 볼 수 있습니다.",
        ja: "トレイアイコンをクリックすると画面をキャプチャします。右クリックでその他のメニューを表示します。",
        zhHans: "点击托盘图标即可截图，右键点击查看更多选项。",
        zhHant: "點擊系統匣圖示即可擷取畫面，按右鍵可查看更多選項。",
        hi: "स्क्रीनशॉट लेने के लिए ट्रे आइकन पर क्लिक करें। अधिक विकल्पों के लिए राइट-क्लिक करें।",
        ur: "اسکرین شاٹ لینے کے لیے ٹرے آئیکن پر کلک کریں۔ مزید اختیارات کے لیے رائٹ کلک کریں۔",
        ru: "Нажмите на значок в трее, чтобы сделать снимок экрана. Нажмите правой кнопкой мыши для других опций.",
        es: "Haz clic en el icono de la bandeja para capturar la pantalla. Haz clic derecho para más opciones.",
        fr: "Cliquez sur l’icône de la barre d’état pour capturer l’écran. Faites un clic droit pour plus d’options.",
        de: "Klicken Sie auf das Taskleistensymbol für einen Screenshot. Rechtsklick für weitere Optionen.",
        pt: "Clique no ícone da bandeja para capturar a tela. Clique com o botão direito para mais opções.");

    // --- Main window header ---
    public static string AppSubtitle => Get(
        en: "Catch every pixel",
        ko: "모든 순간을 선명하게 캡처하세요",
        ja: "あらゆる瞬間を鮮明にキャプチャ",
        zhHans: "清晰捕捉每一个像素",
        zhHant: "清晰捕捉每一個像素",
        hi: "हर पिक्सेल को स्पष्ट रूप से कैप्चर करें",
        ur: "ہر پکسل کو واضح طور پر کیپچر کریں",
        ru: "Захватите каждый пиксель",
        es: "Captura cada píxel",
        fr: "Capturez chaque pixel",
        de: "Jedes Pixel perfekt erfassen",
        pt: "Capture cada pixel");

    public static string RecordAudio => Get(
        en: "Capture Audio",
        ko: "오디오 캡처",
        ja: "音声を録音",
        zhHans: "录制音频",
        zhHant: "錄製音訊",
        hi: "ऑडियो कैप्चर",
        ur: "آڈیو کیپچر",
        ru: "Запись аудио",
        es: "Grabar audio",
        fr: "Audio",
        de: "Audio aufnehmen",
        pt: "Gravar áudio");

    public static string RecordAudioTip => Get(
        en: "Record the sound playing on this computer (Ctrl+R)",
        ko: "컴퓨터에서 재생 중인 소리를 녹음합니다 (Ctrl+R)",
        ja: "PCで再生中の音声を録音します (Ctrl+R)",
        zhHans: "录制电脑正在播放的声音 (Ctrl+R)",
        zhHant: "錄製電腦正在播放的聲音 (Ctrl+R)",
        hi: "इस कंप्यूटर पर बजने वाली आवाज़ रिकॉर्ड करें (Ctrl+R)",
        ur: "اس کمپیوٹر پر چلنے والی آواز ریکارڈ کریں (Ctrl+R)",
        ru: "Записать звук, воспроизводимый на этом компьютере (Ctrl+R)",
        es: "Grabar el sonido que se reproduce en este equipo (Ctrl+R)",
        fr: "Enregistrer le son diffusé sur cet ordinateur (Ctrl+R)",
        de: "Tonaufnahme der aktuellen Computerwiedergabe (Ctrl+R)",
        pt: "Gravar o som reproduzido neste computador (Ctrl+R)");

    public static string StopAudioTip => Get(
        en: "Stop recording and save as MP3 (Ctrl+R)",
        ko: "녹음을 중지하고 MP3로 저장합니다 (Ctrl+R)",
        ja: "録音を停止してMP3として保存します (Ctrl+R)",
        zhHans: "停止录音并保存为 MP3 (Ctrl+R)",
        zhHant: "停止錄音並儲存為 MP3 (Ctrl+R)",
        hi: "रिकॉर्डिंग रोकें और MP3 के रूप में सहेजें (Ctrl+R)",
        ur: "ریکارڈنگ روکیں اور MP3 کے طور پر محفوظ کریں (Ctrl+R)",
        ru: "Остановить запись и сохранить как MP3 (Ctrl+R)",
        es: "Detener grabación y guardar como MP3 (Ctrl+R)",
        fr: "Arrêter l’enregistrement et sauvegarder en MP3 (Ctrl+R)",
        de: "Aufnahme beenden und als MP3 speichern (Ctrl+R)",
        pt: "Parar gravação e salvar como MP3 (Ctrl+R)");

    public static string RecordVideo => Get(
        en: "Capture Video",
        ko: "비디오 캡처",
        ja: "動画を録画",
        zhHans: "录制视频",
        zhHant: "錄製影片",
        hi: "वीडियो कैप्चर",
        ur: "ویڈیو کیپچر",
        ru: "Запись видео",
        es: "Grabar video",
        fr: "Vidéo",
        de: "Video aufnehmen",
        pt: "Gravar vídeo");

    public static string RecordVideoTip => Get(
        en: "Record screen video with system audio (Ctrl+Shift+R)",
        ko: "시스템 오디오와 함께 화면 비디오를 녹화합니다 (Ctrl+Shift+R)",
        ja: "システム音声付きで画面を動画録画します (Ctrl+Shift+R)",
        zhHans: "连同系统声音一起录制屏幕视频 (Ctrl+Shift+R)",
        zhHant: "連同系統聲音一起錄製畫面影片 (Ctrl+Shift+R)",
        hi: "सिस्टम ऑडियो के साथ स्क्रीन वीडियो रिकॉर्ड करें (Ctrl+Shift+R)",
        ur: "سسٹم آڈیو کے ساتھ اسکرین ویڈیو ریکارڈ کریں (Ctrl+Shift+R)",
        ru: "Записать видео с экрана со системным звуком (Ctrl+Shift+R)",
        es: "Grabar video de la pantalla con audio del sistema (Ctrl+Shift+R)",
        fr: "Enregistrer la vidéo de l’écran avec le son du système (Ctrl+Shift+R)",
        de: "Bildschirmvideo mit Systemaudio aufnehmen (Ctrl+Shift+R)",
        pt: "Gravar vídeo da tela com áudio do sistema (Ctrl+Shift+R)");

    public static string StopVideoTip => Get(
        en: "Stop video recording and save as MP4 (Ctrl+Shift+R)",
        ko: "비디오 녹화를 중지하고 MP4로 저장합니다 (Ctrl+Shift+R)",
        ja: "録画を停止してMP4として保存します (Ctrl+Shift+R)",
        zhHans: "停止录像并保存为 MP4 (Ctrl+Shift+R)",
        zhHant: "停止錄影並儲存為 MP4 (Ctrl+Shift+R)",
        hi: "वीडियो रिकॉर्डिंग रोकें और MP4 के रूप में सहेजें (Ctrl+Shift+R)",
        ur: "ویڈیو ریکارڈنگ روکیں اور MP4 کے طور پر محفوظ کریں (Ctrl+Shift+R)",
        ru: "Остановить запись видео и сохранить как MP4 (Ctrl+Shift+R)",
        es: "Detener grabación de video y guardar como MP4 (Ctrl+Shift+R)",
        fr: "Arrêter l’enregistrement vidéo et sauvegarder en MP4 (Ctrl+Shift+R)",
        de: "Videoaufnahme beenden und als MP4 speichern (Ctrl+Shift+R)",
        pt: "Parar gravação de vídeo e salvar como MP4 (Ctrl+Shift+R)");

    public static string NewCapture => Get(
        en: "Capture Screen",
        ko: "화면 캡처",
        ja: "画面キャプチャ",
        zhHans: "屏幕截图",
        zhHant: "擷取畫面",
        hi: "स्क्रीन कैप्चर",
        ur: "اسکرین کیپچر",
        ru: "Снимок экрана",
        es: "Capturar pantalla",
        fr: "Capturer l’écran",
        de: "Bildschirm erfassen",
        pt: "Capturar tela");

    public static string NewCaptureTip => Get(
        en: "Capture screen region (Ctrl+N)",
        ko: "화면 영역 캡처 (Ctrl+N)",
        ja: "画面の範囲をキャプチャ (Ctrl+N)",
        zhHans: "截取屏幕区域 (Ctrl+N)",
        zhHant: "擷取畫面區域 (Ctrl+N)",
        hi: "स्क्रीन क्षेत्र कैप्चर करें (Ctrl+N)",
        ur: "اسکرین کا حصہ کیپچر کریں (Ctrl+N)",
        ru: "Сделать снимок области экрана (Ctrl+N)",
        es: "Capturar región de la pantalla (Ctrl+N)",
        fr: "Capturer une zone de l’écran (Ctrl+N)",
        de: "Bildschirmbereich aufnehmen (Ctrl+N)",
        pt: "Capturar região da tela (Ctrl+N)");

    public static string StopPrefix => Get(
        en: "Stop",
        ko: "중지",
        ja: "停止",
        zhHans: "停止",
        zhHant: "停止",
        hi: "रोकें",
        ur: "روکیں",
        ru: "Стоп",
        es: "Detener",
        fr: "Arrêter",
        de: "Stopp",
        pt: "Parar");

    // --- Empty state ---
    public static string EmptyTitle => Get(
        en: "Nothing caught yet",
        ko: "아직 캡처된 화면이 없습니다",
        ja: "まだキャプチャがありません",
        zhHans: "尚无截图内容",
        zhHant: "尚無擷取內容",
        hi: "अभी तक कुछ भी कैप्चर नहीं किया गया",
        ur: "ابھی تک کچھ کیپچر نہیں کیا گیا",
        ru: "Пока ничего не снято",
        es: "Aún no hay capturas",
        fr: "Aucune capture pour l’instant",
        de: "Noch nichts aufgenommen",
        pt: "Nenhuma captura ainda");

    public static string EmptyPrefix => Get(
        en: "Click ",
        ko: "",
        ja: "",
        zhHans: "点击“",
        zhHant: "點擊「",
        hi: "",
        ur: "",
        ru: "Нажмите ",
        es: "Haz clic en ",
        fr: "Cliquez sur ",
        de: "Klicken Sie auf ",
        pt: "Clique em ");

    public static string EmptyOrPress => Get(
        en: " or press ",
        ko: " 버튼을 누르거나 ",
        ja: " をクリックするか ",
        zhHans: "”或按 ",
        zhHant: "」或按 ",
        hi: " पर क्लिक करें या ",
        ur: " پر کلک کریں یا ",
        ru: " или клавиши ",
        es: " o pulsa ",
        fr: " ou appuyez sur ",
        de: " oder drücken Sie ",
        pt: " ou pressione ");

    public static string EmptySuffix => Get(
        en: ", then drag a rectangle anywhere on your screens to catch it.",
        ko: " 키를 누른 뒤, 화면에서 원하는 영역을 드래그하여 캡처하세요.",
        ja: " を押して、画面上の任意の領域をドラッグしてキャプチャします。",
        zhHans: "，然后在屏幕上拖动选择任意区域进行截图。",
        zhHant: "，然後在畫面上拖曳選取任意區域進行擷取。",
        hi: " दबाएँ, फिर स्क्रीन पर किसी भी क्षेत्र को कैप्चर करने के लिए ड्रैग करें।",
        ur: " دبائیں، پھر اسکرین کے کسی بھی حصے کو کیپچر کرنے کے لیے ڈریگ کریں۔",
        ru: ", затем выделите любую область на экране.",
        es: ", luego arrastra un rectángulo en cualquier parte de la pantalla.",
        fr: ", puis faites glisser un rectangle sur votre écran pour le capturer.",
        de: ", und ziehen Sie dann ein Rechteck auf Ihrem Bildschirm auf.",
        pt: " e arraste um retângulo em qualquer lugar da tela para capturar.");

    // --- Annotation toolbar ---
    public static string ToolText => Get(
        en: "Text",
        ko: "텍스트",
        ja: "テキスト",
        zhHans: "文字",
        zhHant: "文字",
        hi: "टेक्स्ट",
        ur: "متن",
        ru: "Текст",
        es: "Texto",
        fr: "Texte",
        de: "Text",
        pt: "Texto");

    public static string ToolTextTip => Get(
        en: "Add text (T) – then click on the image to type",
        ko: "텍스트 추가 (T) – 이미지를 클릭하여 입력하세요",
        ja: "テキスト追加 (T) – 画像をクリックして入力",
        zhHans: "添加文字 (T) – 点击图像即可输入",
        zhHant: "新增文字 (T) – 點擊圖片即可輸入",
        hi: "टेक्स्ट जोड़ें (T) – टाइप करने के लिए छवि पर क्लिक करें",
        ur: "متن شامل کریں (T) – لکھنے کے لیے تصویر پر کلک کریں",
        ru: "Добавить текст (T) – нажмите на изображение для ввода",
        es: "Añadir texto (T) – haz clic en la imagen para escribir",
        fr: "Ajouter du texte (T) – cliquez sur l’image pour écrire",
        de: "Text hinzufügen (T) – zum Tippen auf das Bild klicken",
        pt: "Adicionar texto (T) – clique na imagem para digitar");

    public static string ToolArrow => Get(
        en: "Arrow",
        ko: "화살표",
        ja: "矢印",
        zhHans: "箭头",
        zhHant: "箭頭",
        hi: "तीर",
        ur: "تیر",
        ru: "Стрелка",
        es: "Flecha",
        fr: "Flèche",
        de: "Pfeil",
        pt: "Seta");

    public static string ToolArrowTip => Get(
        en: "Add arrow (A) – then drag on the image",
        ko: "화살표 추가 (A) – 이미지 위에서 드래그하세요",
        ja: "矢印追加 (A) – 画像上をドラッグして描画",
        zhHans: "添加箭头 (A) – 在图像上拖动绘制",
        zhHant: "新增箭頭 (A) – 在圖片上拖曳繪製",
        hi: "तीर जोड़ें (A) – छवि पर ड्रैग करें",
        ur: "تیر کا نشان لگائیں (A) – تصویر پر ڈریگ کریں",
        ru: "Добавить стрелку (A) – потяните по изображению",
        es: "Añadir flecha (A) – arrastra sobre la imagen",
        fr: "Ajouter une flèche (A) – faites glisser sur l’image",
        de: "Pfeil hinzufügen (A) – auf dem Bild ziehen",
        pt: "Adicionar seta (A) – arraste sobre a imagem");

    public static string ToolCrop => Get(
        en: "Crop",
        ko: "자르기",
        ja: "切り抜き",
        zhHans: "裁剪",
        zhHant: "裁剪",
        hi: "क्रॉप",
        ur: "کراپ",
        ru: "Обрезка",
        es: "Recortar",
        fr: "Rogner",
        de: "Zuschneiden",
        pt: "Cortar");

    public static string ToolCropTip => Get(
        en: "Crop image (K) – drag a rectangle on the image",
        ko: "이미지 자르기 (K) – 이미지 위에서 영역을 드래그하세요",
        ja: "画像の切り抜き (K) – 画像上で範囲をドラッグ",
        zhHans: "裁剪图像 (K) – 在图像上拖动选择区域",
        zhHant: "裁剪圖片 (K) – 在圖片上拖曳選取區域",
        hi: "छवि क्रॉप करें (K) – छवि पर क्षेत्र ड्रैग करें",
        ur: "تصویر کراپ کریں (K) – تصویر پر حصہ ڈریگ کریں",
        ru: "Обрезать изображение (K) – выделите область на изображении",
        es: "Recortar imagen (K) – arrastra un rectángulo sobre la imagen",
        fr: "Rogner l’image (K) – tracez un rectangle sur l’image",
        de: "Bild zuschneiden (K) – ziehen Sie einen Bereich auf dem Bild",
        pt: "Cortar imagem (K) – arraste um retângulo na imagem");

    public static string SwatchTip => Get(
        en: "Color (text & arrows)",
        ko: "색상 (텍스트 및 화살표)",
        ja: "色 (テキストと矢印)",
        zhHans: "颜色（文字和箭头）",
        zhHant: "顏色（文字與箭頭）",
        hi: "रंग (टेक्स्ट और तीर)",
        ur: "رنگ (متن اور تیر)",
        ru: "Цвет (текст и стрелки)",
        es: "Color (texto y flechas)",
        fr: "Couleur (texte et flèches)",
        de: "Farbe (Text & Pfeile)",
        pt: "Cor (texto e setas)");

    public static string SmallerTip => Get(
        en: "Smaller (text size / arrow thickness)",
        ko: "작게 (글자 크기 / 화살표 굵기)",
        ja: "小さく (文字サイズ / 矢印の太さ)",
        zhHans: "缩小（字号 / 箭头粗细）",
        zhHant: "縮小（字體大小 / 箭頭粗細）",
        hi: "छोटा करें (टेक्स्ट आकार / तीर की मोटाई)",
        ur: "چھوٹا کریں (متن کا سائز / تیر کی موٹائی)",
        ru: "Уменьшить (размер текста / толщину стрелки)",
        es: "Más pequeño (tamaño de texto / grosor de flecha)",
        fr: "Plus petit (taille du texte / épaisseur de flèche)",
        de: "Kleiner (Textgröße / Pfeilstärke)",
        pt: "Menor (tamanho do texto / espessura da seta)");

    public static string LargerTip => Get(
        en: "Larger (text size / arrow thickness)",
        ko: "크게 (글자 크기 / 화살표 굵기)",
        ja: "大きく (文字サイズ / 矢印の太さ)",
        zhHans: "放大（字号 / 箭头粗细）",
        zhHant: "放大（字體大小 / 箭頭粗細）",
        hi: "बड़ा करें (टेक्स्ट आकार / तीर की मोटाई)",
        ur: "بڑا کریں (متن کا سائز / تیر کی موٹائی)",
        ru: "Увеличить (размер текста / толщину стрелки)",
        es: "Más grande (tamaño de texto / grosor de flecha)",
        fr: "Plus grand (taille du texte / épaisseur de flèche)",
        de: "Größer (Textgröße / Pfeilstärke)",
        pt: "Maior (tamanho do texto / espessura da seta)");

    public static string UndoTip => Get(
        en: "Undo last annotation (Ctrl+Z)",
        ko: "마지막 주석 실행 취소 (Ctrl+Z)",
        ja: "最後の注釈を元に戻す (Ctrl+Z)",
        zhHans: "撤销上一步标注 (Ctrl+Z)",
        zhHant: "復原上一步標註 (Ctrl+Z)",
        hi: "अंतिम एनोटेशन पूर्ववत करें (Ctrl+Z)",
        ur: "آخری تبدیلی واپس لیں (Ctrl+Z)",
        ru: "Отменить последнюю пометку (Ctrl+Z)",
        es: "Deshacer última anotación (Ctrl+Z)",
        fr: "Annuler la dernière annotation (Ctrl+Z)",
        de: "Letzte Anmerkung rückgängig machen (Ctrl+Z)",
        pt: "Desfazer última anotação (Ctrl+Z)");

    public static string ClearTip => Get(
        en: "Delete selected annotation (Delete)",
        ko: "선택한 주석 삭제 (Delete)",
        ja: "選択した注釈を削除 (Delete)",
        zhHans: "删除选中的标注 (Delete)",
        zhHant: "刪除選取的標註 (Delete)",
        hi: "चयनित एनोटेशन हटाएँ (Delete)",
        ur: "منتخب نشان حذف کریں (Delete)",
        ru: "Удалить выбранную пометку (Delete)",
        es: "Eliminar anotación seleccionada (Delete)",
        fr: "Supprimer l’annotation sélectionnée (Suppr)",
        de: "Ausgewählte Anmerkung löschen (Entf)",
        pt: "Excluir anotação selecionada (Delete)");

    public static string ZoomOutTip => Get(
        en: "Zoom out (Mouse wheel down)",
        ko: "축소 (마우스 휠 아래로)",
        ja: "縮小 (マウスホイール下)",
        zhHans: "缩小（鼠标滚轮向下）",
        zhHant: "縮小（滑鼠滾輪向下）",
        hi: "ज़ूम आउट (माउस व्हील नीचे)",
        ur: "زوم آؤٹ (ماؤس وہیل نیچے)",
        ru: "Отдалить (колесо мыши вниз)",
        es: "Alejar (rueda del ratón hacia abajo)",
        fr: "Zoom arrière (molette vers le bas)",
        de: "Verkleinern (Mausrad nach unten)",
        pt: "Diminuir zoom (roda do mouse para baixo)");

    public static string ZoomResetTip => Get(
        en: "Mouse wheel to zoom • Click to reset to 100% (Ctrl+0)",
        ko: "마우스 휠로 확대/축소 • 클릭하면 100%로 초기화 (Ctrl+0)",
        ja: "ホイールで拡大/縮小 • クリックで100%にリセット (Ctrl+0)",
        zhHans: "滚轮缩放 • 点击重置为 100% (Ctrl+0)",
        zhHant: "滾輪縮放 • 點擊重設為 100% (Ctrl+0)",
        hi: "ज़ूम के लिए माउस व्हील • 100% पर रीसेट करने के लिए क्लिक करें (Ctrl+0)",
        ur: "زوم کے لیے ماؤس وہیل • 100% پر ری سیٹ کرنے کے لیے کلک کریں (Ctrl+0)",
        ru: "Колесо мыши для масштаба • Нажмите для сброса на 100% (Ctrl+0)",
        es: "Rueda para zoom • Clic para restablecer al 100% (Ctrl+0)",
        fr: "Molette pour zoomer • Cliquez pour réinitialiser à 100% (Ctrl+0)",
        de: "Mausrad zum Zoomen • Klicken für 100 % (Ctrl+0)",
        pt: "Roda do mouse para zoom • Clique para redefinir em 100% (Ctrl+0)");

    public static string ZoomInTip => Get(
        en: "Zoom in (Mouse wheel up)",
        ko: "확대 (마우스 휠 위로)",
        ja: "拡大 (マウスホイール上)",
        zhHans: "放大（鼠标滚轮向上）",
        zhHant: "放大（滑鼠滾輪向上）",
        hi: "ज़ूम इन (माउस व्हील ऊपर)",
        ur: "زوم ان (ماؤس وہیل اوپر)",
        ru: "Приблизить (колесо мыши вверх)",
        es: "Acercar (rueda del ratón hacia arriba)",
        fr: "Zoom avant (molette vers le haut)",
        de: "Vergrößern (Mausrad nach oben)",
        pt: "Aumentar zoom (roda do mouse para cima)");

    // --- Footer ---
    public static string Ready => Get(
        en: "Ready",
        ko: "준비됨",
        ja: "準備完了",
        zhHans: "就绪",
        zhHant: "就緒",
        hi: "तैयार",
        ur: "تیار",
        ru: "Готово",
        es: "Listo",
        fr: "Prêt",
        de: "Bereit",
        pt: "Pronto");

    public static string Copy => Get(
        en: "Copy",
        ko: "복사",
        ja: "コピー",
        zhHans: "复制",
        zhHant: "複製",
        hi: "कॉपी करें",
        ur: "کاپی کریں",
        ru: "Копировать",
        es: "Copiar",
        fr: "Copier",
        de: "Kopieren",
        pt: "Copiar");

    public static string CopyTip => Get(
        en: "Copy to clipboard (Ctrl+C)",
        ko: "클립보드에 복사 (Ctrl+C)",
        ja: "クリップボードにコピー (Ctrl+C)",
        zhHans: "复制到剪贴板 (Ctrl+C)",
        zhHant: "複製到剪貼簿 (Ctrl+C)",
        hi: "क्लिपबोर्ड पर कॉपी करें (Ctrl+C)",
        ur: "کلپ بورڈ پر کاپی کریں (Ctrl+C)",
        ru: "Копировать в буфер обмена (Ctrl+C)",
        es: "Copiar al portapapeles (Ctrl+C)",
        fr: "Copier dans le presse-papiers (Ctrl+C)",
        de: "In die Zwischenablage kopieren (Ctrl+C)",
        pt: "Copiar para a área de transferência (Ctrl+C)");

    public static string SaveAs => Get(
        en: "Save As…",
        ko: "다른 이름으로 저장…",
        ja: "名前を付けて保存…",
        zhHans: "另存为…",
        zhHant: "另存新檔…",
        hi: "ऐसे सहेजें…",
        ur: "محفوظ کریں بطور…",
        ru: "Сохранить как…",
        es: "Guardar como…",
        fr: "Enregistrer sous…",
        de: "Speichern unter…",
        pt: "Salvar como…");

    public static string SaveAsTip => Get(
        en: "Save as image file (Ctrl+S)",
        ko: "이미지 파일로 저장 (Ctrl+S)",
        ja: "画像ファイルとして保存 (Ctrl+S)",
        zhHans: "保存为图像文件 (Ctrl+S)",
        zhHant: "儲存為圖片檔案 (Ctrl+S)",
        hi: "छवि फ़ाइल के रूप में सहेजें (Ctrl+S)",
        ur: "تصویری فائل کے طور پر محفوظ کریں (Ctrl+S)",
        ru: "Сохранить как файл изображения (Ctrl+S)",
        es: "Guardar como archivo de imagen (Ctrl+S)",
        fr: "Enregistrer en tant que fichier image (Ctrl+S)",
        de: "Als Bilddatei speichern (Ctrl+S)",
        pt: "Salvar como arquivo de imagem (Ctrl+S)");

    // --- Toasts & dialogs ---
    public static string ToastCaughtCopied => Get(
        en: "Gotcha! Caught & copied to clipboard",
        ko: "캡처 완료! 클립보드에 복사되었습니다",
        ja: "キャプチャ完了！クリップボードにコピーしました",
        zhHans: "截图成功！已复制到剪贴板",
        zhHant: "擷取成功！已複製到剪貼簿",
        hi: "कैप्चर हो गया! क्लिपबोर्ड पर कॉपी किया गया",
        ur: "کیپچر ہو گیا! کلپ بورڈ پر کاپی کر دیا گیا",
        ru: "Снято и скопировано в буфер обмена!",
        es: "¡Capturado y copiado al portapapeles!",
        fr: "Capturé et copié dans le presse-papiers !",
        de: "Erfasst und in die Zwischenablage kopiert!",
        pt: "Capturado e copiado para a área de transferência!");

    public static string ToastCopied => Get(
        en: "Copied to clipboard",
        ko: "클립보드에 복사되었습니다",
        ja: "クリップボードにコピーしました",
        zhHans: "已复制到剪贴板",
        zhHant: "已複製到剪貼簿",
        hi: "क्लिपबोर्ड पर कॉपी किया गया",
        ur: "کلپ بورڈ پر کاپی ہو گیا",
        ru: "Скопировано в буфер обмена",
        es: "Copiado al portapapeles",
        fr: "Copié dans le presse-papiers",
        de: "In die Zwischenablage kopiert",
        pt: "Copiado para a área de transferência");

    public static string ToastSaved(string fileName) => Get(
        en: $"Saved {fileName}",
        ko: $"{fileName} 저장됨",
        ja: $"{fileName} を保存しました",
        zhHans: $"已保存 {fileName}",
        zhHant: $"已儲存 {fileName}",
        hi: $"{fileName} सहेजा गया",
        ur: $"{fileName} محفوظ ہو گیا",
        ru: $"Сохранено: {fileName}",
        es: $"Guardado {fileName}",
        fr: $"{fileName} enregistré",
        de: $"{fileName} gespeichert",
        pt: $"{fileName} salvo");

    public static string ToastTextHint => Get(
        en: "Click anywhere on the image to add text",
        ko: "이미지에서 원하는 위치를 클릭하여 텍스트를 추가하세요",
        ja: "画像上の任意の場所をクリックしてテキストを追加",
        zhHans: "点击图像任意位置添加文字",
        zhHant: "點擊圖片任意位置新增文字",
        hi: "टेक्स्ट जोड़ने के लिए छवि पर कहीं भी क्लिक करें",
        ur: "متن شامل کرنے کے لیے تصویر پر کہیں بھی کلک کریں",
        ru: "Нажмите в любом месте изображения, чтобы добавить текст",
        es: "Haz clic en cualquier parte de la imagen para añadir texto",
        fr: "Cliquez n’importe où sur l’image pour ajouter du texte",
        de: "Klicken Sie auf eine beliebige Stelle im Bild, um Text hinzuzufügen",
        pt: "Clique em qualquer lugar da imagem para adicionar texto");

    public static string ToastArrowHint => Get(
        en: "Drag on the image to draw an arrow",
        ko: "이미지 위에서 드래그하여 화살표를 그리세요",
        ja: "画像上をドラッグして矢印を描画",
        zhHans: "在图像上拖动以绘制箭头",
        zhHant: "在圖片上拖曳以繪製箭頭",
        hi: "तीर बनाने के लिए छवि पर ड्रैग करें",
        ur: "تیر بنانے کے لیے تصویر پر ڈریگ کریں",
        ru: "Потяните по изображению, чтобы нарисовать стрелку",
        es: "Arrastra sobre la imagen para dibujar una flecha",
        fr: "Faites glisser sur l’image pour dessiner une flèche",
        de: "Ziehen Sie auf dem Bild, um einen Pfeil zu zeichnen",
        pt: "Arraste sobre a imagem para desenhar uma seta");

    public static string ToastCropHint => Get(
        en: "Drag on the image to select the region to keep",
        ko: "이미지에서 남길 영역을 드래그하여 자르세요",
        ja: "画像上をドラッグして残す範囲を選択",
        zhHans: "在图像上拖动以选择要保留的区域",
        zhHant: "在圖片上拖曳以選取要保留的區域",
        hi: "रखने के लिए छवि पर क्षेत्र ड्रैग करें",
        ur: "محفوظ رکھنے کے لیے تصویر پر حصہ ڈریگ کریں",
        ru: "Выделите область на изображении для обрезки",
        es: "Arrastra sobre la imagen para seleccionar el área a conservar",
        fr: "Faites glisser sur l’image pour sélectionner la zone à conserver",
        de: "Ziehen Sie auf dem Bild, um den gewünschten Bereich zuzuschneiden",
        pt: "Arraste sobre a imagem para selecionar a área a manter");

    public static string ToastCropped => Get(
        en: "Cropped & copied to clipboard",
        ko: "자르기 완료! 클립보드에 복사되었습니다",
        ja: "切り抜いてクリップボードにコピーしました",
        zhHans: "已裁剪并复制到剪贴板",
        zhHant: "已裁剪並複製到剪貼簿",
        hi: "क्रॉप किया गया और क्लिपबोर्ड पर कॉपी किया गया",
        ur: "کراپ ہو گیا اور کلپ بورڈ پر کاپی کر دیا گیا",
        ru: "Обрезано и скопировано в буфер обмена",
        es: "Recortado y copiado al portapapeles",
        fr: "Rogné et copié dans le presse-papiers",
        de: "Zugeschnitten und in die Zwischenablage kopiert",
        pt: "Cortado e copiado para a área de transferência");

    public static string ToastRecordingAudio => Get(
        en: "Recording system audio…",
        ko: "시스템 오디오 녹음 중…",
        ja: "システム音声を録音中…",
        zhHans: "正在录制系统音频…",
        zhHant: "正在錄製系統音訊…",
        hi: "सिस्टम ऑडियो रिकॉर्ड हो रहा है…",
        ur: "سسٹم آڈیو ریکارڈ ہو رہا ہے…",
        ru: "Запись системного звука…",
        es: "Grabando audio del sistema…",
        fr: "Enregistrement de l’audio système…",
        de: "Systemaudio wird aufgenommen…",
        pt: "Gravando áudio do sistema…");

    public static string ToastNothingRecorded => Get(
        en: "Nothing was recorded",
        ko: "녹음된 내용이 없습니다",
        ja: "記録されたデータがありません",
        zhHans: "未录制到任何内容",
        zhHant: "未錄製到任何內容",
        hi: "कुछ भी रिकॉर्ड नहीं हुआ",
        ur: "کچھ بھی ریکارڈ نہیں ہوا",
        ru: "Ничего не записано",
        es: "No se grabó nada",
        fr: "Rien n’a été enregistré",
        de: "Es wurde nichts aufgenommen",
        pt: "Nada foi gravado");

    public static string ToastAudioDiscarded => Get(
        en: "Recording discarded",
        ko: "녹음이 취소되었습니다",
        ja: "録音を破棄しました",
        zhHans: "已放弃录音",
        zhHant: "已捨棄錄音",
        hi: "रिकॉर्डिंग हटा दी गई",
        ur: "ریکارڈنگ ضائع کر دی گئی",
        ru: "Запись удалена",
        es: "Grabación descartada",
        fr: "Enregistrement supprimé",
        de: "Aufnahme verworfen",
        pt: "Gravação descartada");

    public static string ToastVideoDiscarded => Get(
        en: "Video recording discarded",
        ko: "비디오 녹화가 취소되었습니다",
        ja: "動画の録画を破棄しました",
        zhHans: "已放弃视频录制",
        zhHant: "已捨棄影片錄製",
        hi: "वीडियो रिकॉर्डिंग हटा दी गई",
        ur: "ویڈیو ریکارڈنگ ضائع کر دی گئی",
        ru: "Видеозапись удалена",
        es: "Grabación de video descartada",
        fr: "Enregistrement vidéo supprimé",
        de: "Videoaufnahme verworfen",
        pt: "Gravação de vídeo descartada");

    public static string SavingMp3 => Get(
        en: "Saving MP3…",
        ko: "MP3 저장 중…",
        ja: "MP3を保存中…",
        zhHans: "正在保存 MP3…",
        zhHant: "正在儲存 MP3…",
        hi: "MP3 सहेजा जा रहा है…",
        ur: "MP3 محفوظ کیا جا رہا ہے…",
        ru: "Сохранение MP3…",
        es: "Guardando MP3…",
        fr: "Enregistrement du MP3…",
        de: "MP3 wird gespeichert…",
        pt: "Salvando MP3…");

    public static string SavingMp4 => Get(
        en: "Saving MP4…",
        ko: "MP4 저장 중…",
        ja: "MP4を保存中…",
        zhHans: "正在保存 MP4…",
        zhHant: "正在儲存 MP4…",
        hi: "MP4 सहेजा जा रहा है…",
        ur: "MP4 محفوظ کیا جا رہا ہے…",
        ru: "Сохранение MP4…",
        es: "Guardando MP4…",
        fr: "Enregistrement du MP4…",
        de: "MP4 wird gespeichert…",
        pt: "Salvando MP4…");

    public static string DiscardAudioQuestion => Get(
        en: "Discard this recording?",
        ko: "이 녹음을 삭제하시겠습니까?",
        ja: "この録音を破棄しますか？",
        zhHans: "要放弃这段录音吗？",
        zhHant: "要捨棄這段錄音嗎？",
        hi: "क्या इस रिकॉर्डिंग को हटा दें?",
        ur: "کیا اس ریکارڈنگ کو ضائع کر دیں؟",
        ru: "Удалить эту аудиозапись?",
        es: "¿Descartar esta grabación?",
        fr: "Supprimer cet enregistrement ?",
        de: "Diese Aufnahme verwerfen?",
        pt: "Descartar esta gravação?");

    public static string DiscardVideoQuestion => Get(
        en: "Discard this video recording?",
        ko: "이 비디오 녹화를 삭제하시겠습니까?",
        ja: "この動画の録画を破棄しますか？",
        zhHans: "要放弃这段视频录制吗？",
        zhHant: "要捨棄這段影片錄製嗎？",
        hi: "क्या इस वीडियो रिकॉर्डिंग को हटा दें?",
        ur: "کیا اس ویڈیو ریکارڈنگ کو ضائع کر دیں؟",
        ru: "Удалить эту видеозапись?",
        es: "¿Descartar esta grabación de video?",
        fr: "Supprimer cet enregistrement vidéo ?",
        de: "Diese Videoaufnahme verwerfen?",
        pt: "Descartar esta gravação de vídeo?");

    public static string ExitWhileAudioRecording => Get(
        en: "An audio recording is in progress.\n\nSave it before exiting?",
        ko: "오디오 녹음이 진행 중입니다.\n\n종료하기 전에 저장하시겠습니까?",
        ja: "音声の録音中です。\n\n終了する前に保存しますか？",
        zhHans: "音频录制正在进行中。\n\n退出前是否保存？",
        zhHant: "音訊錄製正在進行中。\n\n結束前是否儲存？",
        hi: "ऑडियो रिकॉर्डिंग चल रही है।\n\nक्या बाहर निकलने से पहले इसे सहेजना चाहते हैं?",
        ur: "آڈیو ریکارڈنگ جاری ہے۔\n\nکیا بند کرنے سے پہلے اسے محفوظ کرنا چاہتے ہیں؟",
        ru: "Идёт запись аудио.\n\nСохранить её перед выходом?",
        es: "Hay una grabación de audio en curso.\n\n¿Deseas guardarla antes de salir?",
        fr: "Un enregistrement audio est en cours.\n\nVoulez-vous l’enregistrer avant de quitter ?",
        de: "Eine Audioaufnahme läuft gerade.\n\nVor dem Beenden speichern?",
        pt: "Uma gravação de áudio está em andamento.\n\nDeseja salvá-la antes de sair?");

    public static string ExitWhileVideoRecording => Get(
        en: "A video recording is in progress.\n\nSave it before exiting?",
        ko: "비디오 녹화가 진행 중입니다.\n\n종료하기 전에 저장하시겠습니까?",
        ja: "動画の録画中です。\n\n終了する前に保存しますか？",
        zhHans: "视频录制正在进行中。\n\n退出前是否保存？",
        zhHant: "影片錄製正在進行中。\n\n結束前是否儲存？",
        hi: "वीडियो रिकॉर्डिंग चल रही है।\n\nक्या बाहर निकलने से पहले इसे सहेजना चाहते हैं?",
        ur: "ویڈیو ریکارڈنگ جاری ہے۔\n\nکیا بند کرنے سے پہلے اسے محفوظ کرنا چاہتے ہیں؟",
        ru: "Идёт запись видео.\n\nСохранить её перед выходом?",
        es: "Hay una grabación de video en curso.\n\n¿Deseas guardarla antes de salir?",
        fr: "Un enregistrement vidéo est en cours.\n\nVoulez-vous l’enregistrer avant de quitter ?",
        de: "Eine Videoaufnahme läuft gerade.\n\nVor dem Beenden speichern?",
        pt: "Uma gravação de vídeo está em andamento.\n\nDeseja salvá-la antes de sair?");

    // --- Region selector ---
    public static string SelectorScreenPrimary => Get(
        en: "Drag to catch a region",
        ko: "드래그하여 영역 캡처",
        ja: "ドラッグして範囲をキャプチャ",
        zhHans: "拖动选择截图区域",
        zhHant: "拖曳選取擷取區域",
        hi: "क्षेत्र कैप्चर करने के लिए ड्रैग करें",
        ur: "حصہ کیپچر کرنے کے لیے ڈریگ کریں",
        ru: "Выделите область для снимка",
        es: "Arrastra para capturar una región",
        fr: "Faites glisser pour capturer une zone",
        de: "Ziehen, um einen Bereich aufzunehmen",
        pt: "Arraste para capturar uma região");

    public static string SelectorScreenSecondary => Get(
        en: "   •   Esc or right-click to cancel",
        ko: "   •   Esc 또는 우클릭으로 취소",
        ja: "   •   Esc または右クリックでキャンセル",
        zhHans: "   •   按 Esc 或右键取消",
        zhHant: "   •   按 Esc 或右鍵取消",
        hi: "   •   रद्द करने के लिए Esc या राइट-क्लिक करें",
        ur: "   •   منسوخ کرنے کے لیے Esc یا رائٹ کلک کریں",
        ru: "   •   Esc или правый клик для отмены",
        es: "   •   Esc o clic derecho para cancelar",
        fr: "   •   Échap ou clic droit pour annuler",
        de: "   •   Esc oder Rechtsklick zum Abbrechen",
        pt: "   •   Esc ou clique direito para cancelar");

    public static string SelectorVideoPrimary => Get(
        en: "Drag a region to record",
        ko: "드래그하여 영역 녹화",
        ja: "ドラッグして録画範囲を選択",
        zhHans: "拖动选择录制区域",
        zhHant: "拖曳選取錄影區域",
        hi: "रिकॉर्ड करने के लिए क्षेत्र ड्रैग करें",
        ur: "ریکارڈ کرنے کے لیے حصہ ڈریگ کریں",
        ru: "Выделите область для записи видео",
        es: "Arrastra una región para grabar",
        fr: "Faites glisser une zone à enregistrer",
        de: "Ziehen, um den Aufnahmebereich zu wählen",
        pt: "Arraste uma região para gravar");

    public static string SelectorVideoSecondary => Get(
        en: "   •   Click or Enter for full screen   •   Esc to cancel",
        ko: "   •   클릭 또는 Enter로 전체 화면   •   Esc로 취소",
        ja: "   •   クリックまたは Enter で全画面   •   Esc でキャンセル",
        zhHans: "   •   点击或按 Enter 录制全屏   •   按 Esc 取消",
        zhHant: "   •   點擊或按 Enter 錄製全螢幕   •   按 Esc 取消",
        hi: "   •   फुल स्क्रीन के लिए क्लिक या Enter करें   •   Esc रद्द करें",
        ur: "   •   فل اسکرین کے لیے کلک یا Enter کریں   •   Esc منسوخ کریں",
        ru: "   •   Клик или Enter — весь экран   •   Esc — отмена",
        es: "   •   Clic o Enter para pantalla completa   •   Esc para cancelar",
        fr: "   •   Clic ou Entrée pour plein écran   •   Échap pour annuler",
        de: "   •   Klick oder Enter für Vollbild   •   Esc zum Abbrechen",
        pt: "   •   Clique ou Enter para tela cheia   •   Esc para cancelar");

    // --- Recording overlay ---
    public static string OverlayDragTip => Get(
        en: "Drag to move • Excluded from video capture",
        ko: "드래그하여 이동 • 비디오 녹화에는 표시되지 않습니다",
        ja: "ドラッグして移動 • 録画には映りません",
        zhHans: "拖动可移动 • 不会录制到视频中",
        zhHant: "拖曳可移動 • 不會錄製到影片中",
        hi: "स्थानांतरित करने के लिए ड्रैग करें • वीडियो में दिखाई नहीं देगा",
        ur: "منتقل کرنے کے لیے ڈریگ کریں • ویڈیو میں شامل نہیں ہوگا",
        ru: "Перетащите для перемещения • Не попадёт на видео",
        es: "Arrastra para mover • No aparece en el video",
        fr: "Faites glisser pour déplacer • Exclu de la vidéo",
        de: "Ziehen zum Verschieben • Nicht im Video sichtbar",
        pt: "Arraste para mover • Não aparece no vídeo");

    public static string OverlayAudioMeterTip => Get(
        en: "System audio level",
        ko: "시스템 오디오 음량",
        ja: "システム音量レベル",
        zhHans: "系统音频音量",
        zhHant: "系統音訊音量",
        hi: "सिस्टम ऑडियो स्तर",
        ur: "سسٹم آڈیو لیول",
        ru: "Уровень системного звука",
        es: "Nivel de audio del sistema",
        fr: "Niveau audio du système",
        de: "Systemaudio-Pegel",
        pt: "Nível de áudio do sistema");

    // --- Settings window ---
    public static string SettingsTitle => Get(
        en: "Setting",
        ko: "설정",
        ja: "設定",
        zhHans: "设置",
        zhHant: "設定",
        hi: "सेटिंग्स",
        ur: "ترتیبات",
        ru: "Настройки",
        es: "Configuración",
        fr: "Paramètres",
        de: "Einstellungen",
        pt: "Configurações");

    public static string SettingsLanguageHeader => Get(
        en: "Language",
        ko: "언어",
        ja: "言語",
        zhHans: "语言",
        zhHant: "語言",
        hi: "भाषा",
        ur: "زبان",
        ru: "Язык",
        es: "Idioma",
        fr: "Langue",
        de: "Sprache",
        pt: "Idioma");

    public static string SettingsLanguageDesc => Get(
        en: "Choose the display language for menus, buttons, and messages.",
        ko: "메뉴, 버튼 및 메시지에 사용할 표시 언어를 선택하세요.",
        ja: "メニュー、ボタン、メッセージの表示言語を選択してください。",
        zhHans: "选择菜单、按钮和消息的显示语言。",
        zhHant: "選擇選單、按鈕與訊息的顯示語言。",
        hi: "मेनू, बटन और संदेशों के लिए प्रदर्शन भाषा चुनें।",
        ur: "مینو، بٹن اور پیغامات کے لیے ڈسپلے زبان منتخب کریں۔",
        ru: "Выберите язык отображения меню, кнопок и сообщений.",
        es: "Elige el idioma de visualización para menús, botones y mensajes.",
        fr: "Choisissez la langue d’affichage des menus, boutons et messages.",
        de: "Wählen Sie die Anzeigesprache für Menüs, Schaltflächen und Meldungen.",
        pt: "Escolha o idioma de exibição para menus, botões e mensagens.");

    public static string SettingsShortcutHeader => Get(
        en: "Shortcut",
        ko: "단축키",
        ja: "ショートカット",
        zhHans: "快捷键",
        zhHant: "快速鍵",
        hi: "शॉर्टकट",
        ur: "شارٹ کٹ",
        ru: "Горячая клавиша",
        es: "Atajo de teclado",
        fr: "Raccourci",
        de: "Tastenkombination",
        pt: "Atalho");

    public static string SettingsPrintScreenLabel => Get(
        en: "Use the Print Screen key to capture screen",
        ko: "Print Screen 키로 화면 캡처 시작",
        ja: "Print Screen キーで画面キャプチャを開始",
        zhHans: "使用 Print Screen 键开始屏幕截图",
        zhHant: "使用 Print Screen 鍵開始擷取畫面",
        hi: "स्क्रीन कैप्चर करने के लिए Print Screen कुंजी का उपयोग करें",
        ur: "اسکرین کیپچر کے لیے Print Screen کلید استعمال کریں",
        ru: "Использовать клавишу Print Screen для снимка экрана",
        es: "Usar la tecla Impr Pant (Print Screen) para capturar pantalla",
        fr: "Utiliser la touche Impr. écran pour capturer l’écran",
        de: "Druck-Taste (Print Screen) für Bildschirmaufnahme verwenden",
        pt: "Usar a tecla Print Screen para capturar a tela");

    public static string SettingsPrintScreenDesc => Get(
        en: "When turned off, pressing Print Screen uses the default Windows Snipping Tool.",
        ko: "끄면 Print Screen 키를 눌렀을 때 Windows 기본 캡처 도구가 실행됩니다.",
        ja: "オフにすると、Print Screen キーは Windows 標準の Snipping Tool を起動します。",
        zhHans: "关闭后，按 Print Screen 键将恢复使用 Windows 默认截图工具。",
        zhHant: "關閉後，按 Print Screen 鍵將恢復使用 Windows 預設剪取工具。",
        hi: "बंद करने पर, Print Screen दबाने से डिफ़ॉल्ट Windows स्निपिंग टूल खुलेगा।",
        ur: "بند کرنے پر، Print Screen دبانے سے ونڈوز کا ڈیفالٹ اسنپنگ ٹول کھلے گا۔",
        ru: "Если отключено, клавиша Print Screen будет открывать стандартные «Ножницы» Windows.",
        es: "Si se desactiva, pulsar Impr Pant abrirá la herramienta Recortes de Windows.",
        fr: "Lorsque cette option est désactivée, la touche Impr. écran utilise l’outil Capture Windows.",
        de: "Wenn deaktiviert, öffnet die Druck-Taste das Windows-Snipping Tool.",
        pt: "Quando desativado, pressionar Print Screen usa a Ferramenta de Captura do Windows.");

    public static string SettingsClose => Get(
        en: "Close",
        ko: "닫기",
        ja: "閉じる",
        zhHans: "关闭",
        zhHant: "關閉",
        hi: "बंद करें",
        ur: "بند کریں",
        ru: "Закрыть",
        es: "Cerrar",
        fr: "Fermer",
        de: "Schließen",
        pt: "Fechar");

    private sealed class SettingsData
    {
        public string? Language { get; set; }
        public bool? UsePrintScreenKey { get; set; }
    }

    private static SettingsData? ReadSettingsData()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsPath));
        }
        catch { /* ignore corrupt settings */ }
        return null;
    }

    private static AppLanguage LoadInitialLanguage()
    {
        var data = ReadSettingsData();
        if (!string.IsNullOrWhiteSpace(data?.Language))
        {
            foreach (var item in SupportedLanguages)
            {
                if (string.Equals(item.Code, data.Language, StringComparison.OrdinalIgnoreCase))
                    return item.Language;
            }
        }
        return DetectSystemLanguage();
    }

    private static bool LoadInitialPrintScreenKey() =>
        ReadSettingsData()?.UsePrintScreenKey ?? true;

    public static AppLanguage DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        string name = culture.Name;
        string iso = culture.TwoLetterISOLanguageName.ToLowerInvariant();

        return iso switch
        {
            "ko" => AppLanguage.Korean,
            "ja" => AppLanguage.Japanese,
            "zh" => name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase)
                        ? AppLanguage.ChineseTraditional
                        : AppLanguage.ChineseSimplified,
            "hi" => AppLanguage.Hindi,
            "ur" => AppLanguage.Urdu,
            "ru" => AppLanguage.Russian,
            "es" => AppLanguage.Spanish,
            "fr" => AppLanguage.French,
            "de" => AppLanguage.German,
            "pt" => AppLanguage.Portuguese,
            _    => AppLanguage.English
        };
    }

    private static void SaveSettings()
    {
        try
        {
            string? dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string code = SupportedLanguages.FirstOrDefault(x => x.Language == Current).Code ?? "en";
            var json = JsonSerializer.Serialize(new SettingsData
            {
                Language = code,
                UsePrintScreenKey = UsePrintScreenKey
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best effort */ }
    }
}
