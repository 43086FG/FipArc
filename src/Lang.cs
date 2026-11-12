// Lang.cs —— 多语言支持
//
// 设计原则：
//   1. 语言用「代码」标识（en / ja / ko / es / ru / zh-TW / zh-CN），
//      界面上永远显示该语言自己的名字（English、日本語、한국어…），
//      这样用户即使看不懂当前界面也能找到自己的语言。
//   2. 文案集中在这一个文件里，key -> 7 种语言的数组。
//      加一种语言只需在 Langs 里加一列 + 每个 key 补一项。
//   3. 找不到的 key 返回 key 本身，方便发现漏翻。
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace FipArc
{
    public static class Lang
    {
        public const string REG_KEY = @"Software\FipArc";
        const string REG_VALUE = "Language";

        /// <summary>支持的语言代码（顺序必须和 Names、各文案数组一致）</summary>
        public static readonly string[] Codes = { "en", "ja", "ko", "es", "ru", "zh-TW", "zh-CN" };

        /// <summary>语言自己的名字 —— 界面上就显示这些</summary>
        public static readonly string[] Names = { "English", "日本語", "한국어", "Español", "Русский", "繁體中文", "简体中文" };

        const int EN = 0, JA = 1, KO = 2, ES = 3, RU = 4, TW = 5, CN = 6;

        static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>(StringComparer.Ordinal);
        static int cur = CN;

        static void A(string key, string en, string ja, string ko, string es, string ru, string tw, string cn)
        {
            T[key] = new string[] { en, ja, ko, es, ru, tw, cn };
        }

        static Lang()
        {
            // ---------------- 通用 ----------------
            A("app.name",       "FipArc", "FipArc", "FipArc", "FipArc", "FipArc", "FipArc", "FipArc");
            A("app.desc",       "Archiver & Media Player",
                                "圧縮・メディアプレイヤー",
                                "압축 및 미디어 플레이어",
                                "Compresor y reproductor",
                                "Архиватор и плеер",
                                "壓縮與媒體播放器",
                                "压缩与媒体播放器");
            A("ok",     "OK", "OK", "확인", "Aceptar", "ОК", "確定", "确定");
            A("cancel", "Cancel", "キャンセル", "취소", "Cancelar", "Отмена", "取消", "取消");
            A("yes",    "Yes", "はい", "예", "Sí", "Да", "是", "是");
            A("no",     "No", "いいえ", "아니요", "No", "Нет", "否", "否");
            A("close",  "Close", "閉じる", "닫기", "Cerrar", "Закрыть", "關閉", "关闭");
            A("browse", "Browse…", "参照…", "찾아보기…", "Examinar…", "Обзор…", "瀏覽…", "浏览…");

            // ---------------- 主窗口：菜单 ----------------
            A("menu.file",    "&File", "ファイル(&F)", "파일(&F)", "&Archivo", "&Файл", "檔案(&F)", "文件(&F)");
            A("menu.pack",    "&Compress", "圧縮(&C)", "압축(&C)", "&Comprimir", "&Сжать", "壓縮(&C)", "压缩(&C)");
            A("menu.extract", "E&xtract", "解凍(&X)", "압축 풀기(&X)", "E&xtraer", "&Извлечь", "解壓縮(&X)", "解压(&X)");
            A("menu.tools",   "&Tools", "ツール(&T)", "도구(&T)", "&Herramientas", "&Инструменты", "工具(&T)", "工具(&T)");
            A("menu.help",    "&Help", "ヘルプ(&H)", "도움말(&H)", "A&yuda", "&Справка", "說明(&H)", "帮助(&H)");
            A("menu.open",    "Open Archive…", "書庫を開く…", "압축 파일 열기…", "Abrir archivo…", "Открыть архив…", "開啟壓縮檔…", "打开压缩包…");
            A("menu.exit",    "Exit", "終了", "종료", "Salir", "Выход", "結束", "退出");
            A("menu.about",   "About", "このソフトについて", "정보", "Acerca de", "О программе", "關於", "关于");
            A("menu.language","Language", "言語", "언어", "Idioma", "Язык", "語言", "语言");

            A("app.title",  "FipArc Archiver", "FipArc 圧縮ツール", "FipArc 압축 도구", "FipArc Compresor", "FipArc Архиватор", "FipArc 壓縮工具", "FipArc 压缩工具");

            // ---------------- 主窗口：菜单项 ----------------
            A("file.open",   "Open archive…", "書庫を開く…", "압축 파일 열기…", "Abrir archivo…", "Открыть архив…", "開啟壓縮檔…", "打开压缩包…");
            A("pack.fip",    "Compress to FIP (native)…", "FIP に圧縮（独自形式）…", "FIP로 압축(전용 형식)…", "Comprimir a FIP (nativo)…", "Сжать в FIP (свой формат)…", "壓縮為 FIP（專有格式）…", "压缩为 FIP（本软件专有）…");
            A("extract.to",  "Extract to…", "展開先…", "압축 풀 위치…", "Extraer en…", "Извлечь в…", "解壓縮至…", "解压到…");
            A("ctx.desk",    "Extract selected to Desktop", "選択項目をデスクトップに展開", "선택 항목을 바탕 화면에 풀기", "Extraer selección al escritorio", "Извлечь выбранное на рабочий стол", "解壓縮選取項至桌面", "解压选中项到桌面");
            A("ctx.to",      "Extract selected to…", "選択項目を展開…", "선택 항목 압축 풀기…", "Extraer selección en…", "Извлечь выбранное в…", "解壓縮選取項至…", "解压选中项到…");
            A("st.items",    "{0} items", "{0} 項目", "{0}개 항목", "{0} elementos", "{0} элементов", "{0} 個項目", "{0} 项");
            A("log.opened",  "Opened {0} ({1}) - {2} entries ({3} folders / {4} files), {5} unpacked",
                           "{0}（{1}）を開きました。{2} 項目（{3} フォルダー / {4} ファイル）、展開後 {5}",
                           "{0}({1}) 열림 - {2}개 항목({3}개 폴더 / {4}개 파일), 압축 해제 시 {5}",
                           "Abierto {0} ({1}) - {2} entradas ({3} carpetas / {4} archivos), {5} sin comprimir",
                           "Открыт {0} ({1}) - записей {2} ({3} папок / {4} файлов), распаковано {5}",
                           "已開啟 {0}（{1}）- 共 {2} 項（{3} 個資料夾 / {4} 個檔案），解壓後 {5}",
                           "已打开 {0}（{1}）- 共 {2} 项（{3} 个文件夹 / {4} 个文件），解压后 {5}");
            // ---------------- 主窗口：下拉菜单项 ----------------
            A("file.exit",    "Exit", "終了", "종료", "Salir", "Выход", "結束", "退出");
            A("pack.zip",     "Compress to ZIP…", "ZIP に圧縮…", "ZIP로 압축…", "Comprimir a ZIP…", "Сжать в ZIP…", "壓縮為 ZIP…", "压缩为 ZIP…");
            A("pack.tar",     "Compress to TAR…", "TAR に圧縮…", "TAR로 압축…", "Comprimir a TAR…", "Сжать в TAR…", "壓縮為 TAR…", "压缩为 TAR…");
            A("pack.gz",      "Compress to GZ (single file)…", "GZ に圧縮（単一ファイル）…", "GZ로 압축(단일 파일)…", "Comprimir a GZ (un archivo)…", "Сжать в GZ (один файл)…", "壓縮為 GZ（單一檔案）…", "压缩为 GZ（单文件）…");
            A("pack.tgz",     "Compress to TAR.GZ…", "TAR.GZ に圧縮…", "TAR.GZ로 압축…", "Comprimir a TAR.GZ…", "Сжать в TAR.GZ…", "壓縮為 TAR.GZ…", "压缩为 TAR.GZ…");
            A("pack.add",     "Add files to current archive…", "書庫にファイルを追加…", "압축 파일에 추가…", "Agregar archivos al archivo…", "Добавить файлы в архив…", "新增檔案至壓縮檔…", "添加文件到当前压缩包…");
            A("extract.here", "Extract here", "ここに展開", "여기에 풀기", "Extraer aquí", "Извлечь сюда", "解壓縮到此處", "解压到当前文件夹");
            A("extract.view", "Browse archive contents", "書庫の中身を見る", "압축 파일 내용 보기", "Examinar contenido", "Просмотр содержимого", "瀏覽壓縮檔內容", "查看压缩包内容");
            A("tools.repair2",   "Register / repair context menu", "右クリックメニューを登録 / 修復", "상황별 메뉴 등록 / 복구", "Registrar / reparar menú", "Зарегистрировать / восстановить меню", "登錄 / 修復右鍵選單", "注册 / 修复右键菜单");
            A("tools.refresh2",  "Refresh file icons", "ファイルアイコンを更新", "파일 아이콘 새로 고침", "Actualizar iconos", "Обновить значки файлов", "重新整理檔案圖示", "刷新文件图标");
            A("tools.defaults2", "Set as default app…", "既定のアプリに設定…", "기본 앱으로 설정…", "Establecer como app predeterminada…", "Сделать приложением по умолчанию…", "設為預設應用程式…", "设为默认应用…");
            A("tools.cache2",    "Clear cache…", "キャッシュを削除…", "캐시 지우기…", "Borrar caché…", "Очистить кэш…", "清除快取…", "清除缓存…");
            A("tools.settings",  "Settings", "設定", "설정", "Configuración", "Настройки", "設定", "设置");
            A("tools.uninstall2","Uninstall…", "アンインストール…", "제거…", "Desinstalar…", "Удалить…", "解除安裝…", "卸载…");
            A("help.about2",     "About", "このソフトについて", "정보", "Acerca de", "О программе", "關於", "关于");

            // ---------------- 设置窗口 ----------------
            A("set.title",   "Settings", "設定", "설정", "Configuración", "Настройки", "設定", "设置");
            A("set.language","Display language", "表示言語", "표시 언어", "Idioma de la interfaz", "Язык интерфейса", "介面語言", "界面语言");
            A("set.restart", "The application will restart to apply the new language.",
                             "新しい言語を適用するため再起動します。",
                             "새 언어를 적용하기 위해 다시 시작합니다.",
                             "La aplicación se reiniciará para aplicar el idioma.",
                             "Приложение перезапустится, чтобы применить язык.",
                             "將重新啟動以套用新語言。",
                             "将重启程序以应用新语言。");
            A("set.close",   "Close", "閉じる", "닫기", "Cerrar", "Закрыть", "關閉", "关闭");
            // ---------------- 主窗口：工具栏 ----------------
            A("tb.open",    "Open", "開く", "열기", "Abrir", "Открыть", "開啟", "打开");
            A("tb.pack",    "Compress", "圧縮", "압축", "Comprimir", "Сжать", "壓縮", "压缩");
            A("tb.extract", "Extract", "解凍", "압축 풀기", "Extraer", "Извлечь", "解壓縮", "解压");

            // ---------------- 主窗口：列表列名 ----------------
            A("col.name",   "Name", "名前", "이름", "Nombre", "Имя", "名稱", "名称");
            A("col.size",   "Size", "サイズ", "크기", "Tamaño", "Размер", "大小", "大小");
            A("col.packed", "Packed", "圧縮後", "압축 후", "Comprimido", "Сжатый", "壓縮後", "压缩后");
            A("col.type",   "Type", "種類", "종류", "Tipo", "Тип", "類型", "类型");

            // ---------------- 主窗口：状态与日志 ----------------
            A("st.ready",  "Ready", "準備完了", "준비됨", "Listo", "Готово", "就緒", "就绪");
            A("st.working","Working…", "処理中…", "처리 중…", "Procesando…", "Обработка…", "處理中…", "处理中…");
            A("log.welcome","FipArc started.", "FipArc を起動しました。", "FipArc가 시작되었습니다.", "FipArc iniciado.", "FipArc запущен.", "FipArc 已啟動。", "FipArc 已启动。");
            A("log.hint",  "Drop files or folders here to compress them; drop an archive to browse it.",
                           "ファイルやフォルダをここにドロップすると圧縮、書庫をドロップすると中身を表示します。",
                           "파일이나 폴더를 여기에 놓으면 압축하고, 압축 파일을 놓으면 내용을 보여줍니다.",
                           "Suelta archivos o carpetas aquí para comprimirlos; suelta un archivo comprimido para verlo.",
                           "Перетащите файлы или папки сюда для сжатия, архив — для просмотра.",
                           "將檔案或資料夾拖到此處即可壓縮；拖入壓縮檔即可瀏覽內容。",
                           "把文件或文件夹拖进窗口即可压缩；把压缩包拖进来即可查看内容。");
            A("folder",    "Folder", "フォルダー", "폴더", "Carpeta", "Папка", "資料夾", "文件夹");
            A("root",      "(root)", "(ルート)", "(루트)", "(raíz)", "(корень)", "(根目錄)", "(根目录)");

            // ---------------- 工具菜单 ----------------
            A("tools.repair",  "Repair context menu", "右クリックメニューを修復", "상황별 메뉴 복구", "Reparar menú contextual", "Восстановить меню", "修復右鍵選單", "修复右键菜单");
            A("tools.refresh", "Refresh file icons", "ファイルアイコンを更新", "파일 아이콘 새로 고침", "Actualizar iconos", "Обновить значки", "重新整理檔案圖示", "刷新文件图标");
            A("tools.cache",   "Clear cache…", "キャッシュを削除…", "캐시 지우기…", "Borrar caché…", "Очистить кэш…", "清除快取…", "清除缓存…");
            A("tools.defaults","Set as default app…", "既定のアプリに設定…", "기본 앱으로 설정…", "Establecer como app predeterminada…", "Сделать приложением по умолчанию…", "設為預設應用程式…", "设为默认应用…");
            A("tools.lav",     "Install built-in codecs (LAV)", "内蔵コーデックを導入 (LAV)", "내장 코덱 설치 (LAV)", "Instalar códecs (LAV)", "Установить кодеки (LAV)", "安裝內建解碼器 (LAV)", "安装内置解码器 (LAV)");
            A("tools.uninstall","Uninstall…", "アンインストール…", "제거…", "Desinstalar…", "Удалить…", "解除安裝…", "卸载…");

            // ---------------- 安装程序 ----------------
            A("setup.title",   "FipArc Setup", "FipArc セットアップ", "FipArc 설치", "Instalador de FipArc", "Установка FipArc", "FipArc 安裝程式", "FipArc 安装程序");
            A("setup.lang",    "Installer language", "インストーラーの言語", "설치 언어", "Idioma del instalador", "Язык установщика", "安裝程式語言", "安装程序语言");
            A("setup.dest",    "Install location", "インストール先", "설치 위치", "Ubicación de instalación", "Папка установки", "安裝位置", "安装位置");
            A("setup.desktop", "Create desktop shortcut", "デスクトップにショートカットを作成", "바탕 화면에 바로 가기 만들기", "Crear acceso directo en el escritorio", "Создать ярлык на рабочем столе", "建立桌面捷徑", "创建桌面快捷方式");
            A("setup.install", "Install", "インストール", "설치", "Instalar", "Установить", "安裝", "安装");
            A("setup.done",    "Installation complete.", "インストールが完了しました。", "설치가 완료되었습니다.", "Instalación completada.", "Установка завершена.", "安裝完成。", "安装完成。");
            A("setup.later",   "You can change the language later in Tools → Language.",
                               "言語は後で「ツール → 言語」から変更できます。",
                               "언어는 나중에 도구 → 언어에서 변경할 수 있습니다.",
                               "Puedes cambiar el idioma luego en Herramientas → Idioma.",
                               "Язык можно изменить позже: Инструменты → Язык.",
                               "語言可稍後在「工具 → 語言」變更。",
                               "语言可以稍后在「工具 → 语言」里修改。");
            A("setup.loc",      "Choose install location", "インストール先の選択", "설치 위치 선택", "Elegir ubicación", "Выберите папку установки", "選擇安裝位置", "选择安装位置");
            A("setup.desc",     "Installs FipArc: archiver, image viewer and text editor.\r\nOnly the .fip association is claimed; other file types keep their system defaults.",
                                "FipArc（圧縮・画像表示・テキスト編集）をインストールします。`r`n関連付けは .fip のみ。他のファイル形式はシステムの既定のままです。",
                                "FipArc(압축·이미지 보기·텍스트 편집)를 설치합니다.`r`n.fip 연결만 가져가며, 다른 형식은 시스템 기본값을 유지합니다.",
                                "Instala FipArc: compresor, visor de imágenes y editor de texto.`r`nSolo se asocia .fip; el resto mantiene sus valores del sistema.",
                                "Устанавливает FipArc: архиватор, просмотр изображений и редактор текста.`r`nСвязывается только .fip, остальные типы остаются системными.",
                                "安裝 FipArc：壓縮、看圖、文字編輯。`r`n只接管 .fip 關聯，其它檔案類型維持系統預設。",
                                "安装 FipArc：压缩、看图、文本编辑。`r`n只接管 .fip 关联，其它文件类型保持系统默认。");
            A("setup.path",     "Install path:", "インストール先：", "설치 경로:", "Ruta de instalación:", "Путь установки:", "安裝路徑：", "安装路径：");
            A("setup.browse2",  "Browse…", "参照…", "찾아보기…", "Examinar…", "Обзор…", "瀏覽…", "浏览…");
            A("setup.desktop2", "Create desktop shortcut", "デスクトップにショートカットを作成", "바탕 화면에 바로 가기 만들기", "Crear acceso directo en el escritorio", "Создать ярлык на рабочем столе", "建立桌面捷徑", "创建桌面快捷方式");
            A("setup.install2", "Install", "インストール", "설치", "Instalar", "Установить", "安裝", "安装");
            A("setup.cancel2",  "Cancel", "キャンセル", "취소", "Cancelar", "Отмена", "取消", "取消");            A("setup.installing","Installing…", "インストール中…", "설치 중…", "Instalando…", "Установка…", "正在安裝…", "正在安装…");
            A("setup.writing",  "Writing program files…", "プログラムを書き込み中…", "프로그램 파일 쓰는 중…", "Escribiendo archivos…", "Запись файлов программы…", "正在寫入程式檔案…", "正在写入程序文件…");
            A("setup.registry", "Registering context menu…", "右クリックメニューを登録中…", "상황별 메뉴 등록 중…", "Registrando menú…", "Регистрация меню…", "正在登錄右鍵選單…", "正在注册右键菜单…");
            A("setup.icons",    "Updating file icons…", "ファイルアイコンを更新中…", "파일 아이콘 갱신 중…", "Actualizando iconos…", "Обновление значков…", "正在更新檔案圖示…", "正在更新文件图标…");
            A("setup.shortcut", "Creating desktop shortcut…", "デスクトップにショートカットを作成中…", "바탕 화면 바로 가기 만드는 중…", "Creando acceso directo…", "Создание ярлыка…", "正在建立桌面捷徑…", "正在创建桌面快捷方式…");
            A("setup.finished", "Installed!", "インストール完了！", "설치 완료!", "¡Instalado!", "Установлено!", "安裝完成！", "安装完成！");
            A("setup.finished2","Installation complete. Right-click files to compress or extract.",
                                "インストールが完了しました。ファイルを右クリックして圧縮・展開できます。",
                                "설치가 완료되었습니다. 파일을 마우스 오른쪽 버튼으로 압축하거나 풀 수 있습니다.",
                                "Instalación completada. Haz clic derecho para comprimir o extraer.",
                                "Установка завершена. Щёлкните файл правой кнопкой для сжатия или распаковки.",
                                "安裝完成。可對檔案按右鍵壓縮或解壓縮。",
                                "安装完成。可直接右键压缩或解压文件。");
            A("setup.askrun",   "Launch FipArc now?", "FipArc を起動しますか？", "FipArc를 지금 실행할까요?", "¿Iniciar FipArc ahora?", "Запустить FipArc сейчас?", "要立即啟動 FipArc 嗎？", "是否立即启动 FipArc？");
            A("setup.where",    "Installed to", "インストール先", "설치 위치", "Instalado en", "Установлено в", "安裝於", "安装位置");
            A("setup.finish",   "Finish", "完了", "완료", "Finalizar", "Готово", "完成", "完成");
            A("setup.done.title","Installation complete", "インストール完了", "설치 완료", "Instalación completada", "Установка завершена", "安裝完成", "安装完成");            A("setup.uninstall","Uninstall FipArc", "FipArc をアンインストール", "FipArc 제거", "Desinstalar FipArc", "Удалить FipArc", "解除安裝 FipArc", "卸载 FipArc");
        }

        /// <summary>当前语言索引</summary>
        public static int Current
        {
            get { return cur; }
            set
            {
                if (value < 0 || value >= Codes.Length) value = EN;
                cur = value;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(REG_KEY))
                        if (k != null) k.SetValue(REG_VALUE, Codes[value]);
                }
                catch { }
            }
        }

        /// <summary>从注册表读回上次选择的语言；装过一次之后保持一致</summary>
        public static void Load()
        {
            // 注册表里没有（首次运行 / 静默安装 / 刚卸载重装）就按系统语言来，
            // 不要硬编码成某一种语言。
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(REG_KEY))
                {
                    string code = (k == null) ? null : (k.GetValue(REG_VALUE) as string);
                    if (!string.IsNullOrEmpty(code))
                    {
                        for (int i = 0; i < Codes.Length; i++)
                            if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase)) { cur = i; return; }
                    }
                }
            }
            catch { }
            cur = DetectSystem();
        }

        /// <summary>按当前语言取文案；找不到就返回 key 本身，方便发现漏翻</summary>
        public static string S(string key)
        {
            string[] v;
            if (T.TryGetValue(key, out v) && cur >= 0 && cur < v.Length) return v[cur];
            return key;
        }

        /// <summary>按语言代码查索引（后台/安装程序用）</summary>
        public static int IndexOf(string code)
        {
            for (int i = 0; i < Codes.Length; i++)
                if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase)) return i;
            return EN;
        }

        /// <summary>根据系统区域猜一个默认语言（仅首次安装时用）</summary>
        public static int DetectSystem()
        {

            try
            {
                string n = System.Globalization.CultureInfo.CurrentUICulture.Name;   // zh-CN / ja-JP / ...
                if (n.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    return (n.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("HK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0) ? TW : CN;
                if (n.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return JA;
                if (n.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return KO;
                if (n.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return ES;
                if (n.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return RU;
            }
            catch { }
            return EN;
        }
    }
}
