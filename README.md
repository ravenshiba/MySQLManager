<<<<<<< HEAD
<div align="center">

# ⚡ MySQL Manager

**功能強大的 MySQL 桌面管理工具，以 C# WPF (.NET 8) 開發**

功能完整的 MySQL 桌面管理工具，內建 AI 輔助、多語系支援、深色模式

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?logo=windows)](https://github.com/dotnet/wpf)
[![MySQL](https://img.shields.io/badge/MySQL-5.7%20%7C%208.0%20%7C%209.x-4479A1?logo=mysql&logoColor=white)](https://www.mysql.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

[功能特色](#-功能特色) • [截圖](#-截圖) • [安裝方式](#-安裝方式) • [快速開始](#-快速開始) • [快捷鍵](#-快捷鍵)

</div>

---

## ✨ 功能特色

### 核心功能
| 功能 | 說明 |
|------|------|
| 🔌 多連線管理 | 支援多個 MySQL 連線、SSH Tunnel、SSL/TLS 加密 |
| 📝 SQL 編輯器 | 語法高亮、智慧自動補全、FK-aware JOIN ON 建議、程式碼折疊 |
| 🔍 Find/Replace | Ctrl+F 搜尋、Ctrl+H 取代、大小寫/全字比對 |
| 📊 結果集操作 | 行內編輯、Excel 式欄位篩選、欄寬記憶、BLOB/JSON 預覽 |
| 🗺 ERD 關聯圖 | Force-Directed 自動排列、PNG 匯出 |
| 👤 使用者管理 | GRANT/REVOKE 視覺化介面 |
| 💾 備份還原 | mysqldump 整合、排程備份 |
| 🔀 Schema 比對 | 自動產生同步 SQL |

### 進階特色功能
| 功能 | 說明 |
|------|------|
| 🤖 AI SQL 助理 | 自然語言→SQL 生成、SQL 解釋（支援 OpenAI / Claude API）|
| 🤖 AI 索引建議 | 分析 EXPLAIN，自動建議 CREATE INDEX |
| 🔀 跨連線查詢 | 同時對兩個 MySQL 伺服器執行查詢並並排比對結果 |
| 📸 結果集快照 | 保留多個結果集快照並排比較 |
| 🎨 5 種主題色彩 | 藍/綠/紫/橘/紅，搭配深色/淺色模式 |
| 🌍 三語系支援 | 繁體中文 / English / 日本語，即時切換無需重啟 |
| 🔽 欄位標頭篩選 | Excel 式多選下拉篩選，可組合多欄 |
| 📐 欄寬記憶 | 自動記住每個資料表的欄位寬度 |
| ⌨ 快捷鍵面板 | Ctrl+/ 開啟完整快捷鍵說明 |
| 🔒 Lock 死鎖偵測 | 支援 MySQL 5.7 / 8.0+，5 秒自動更新 |

---

## 📸 截圖

> 主介面（繁體中文 + 深色模式）

---

## 🛠 安裝方式

### 方式一：直接執行（推薦）

從 [Releases](../../releases) 下載最新版 `MySQLManager.exe`，直接雙擊執行，**無需安裝 .NET Runtime**。

### 方式二：從原始碼建置

**環境需求**
- Windows 10 / 11（64 位元）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 或更新版本

```bash
# Clone 專案
git clone https://github.com/ravenshiba/MySQLManager.git
cd MySQLManager

# 還原套件並建置
dotnet restore
dotnet build -c Release

# 執行
dotnet run
```

**打包成單一執行檔**
```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish
```

---

## 🚀 快速開始

1. 啟動程式後點擊工具列「**⚡ 連線**」
2. 填入 MySQL 主機、帳號、密碼
3. 點擊「**測試連線**」確認連線正常
4. 點擊「**確定**」完成連線
5. 在左側樹狀列表展開資料庫
6. 點擊「**+ 新查詢**」開始撰寫 SQL

---

## ⌨ 快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| `F5` / `Ctrl+Enter` | 執行 SQL |
| `Ctrl+O` | 開啟 SQL 檔案 |
| `Ctrl+Shift+O` | 另存 SQL 檔案 |
| `Ctrl+F` | 搜尋 |
| `Ctrl+H` | 取代 |
| `Ctrl+Space` | 觸發自動補全 |
| `Ctrl+Shift+F` | 格式化 SQL |
| `Ctrl+T` | 新增查詢分頁 |
| `Ctrl+W` | 關閉分頁 |
| `Ctrl+/` | 快捷鍵說明面板 |

完整快捷鍵列表請按 `Ctrl+/` 在程式內查看。

---

## 🏗 專案架構

```
MySQLManager/
├── Models/          # 資料模型
├── ViewModels/      # MVVM ViewModel 層
├── Views/           # WPF XAML 視窗（40+ 個）
├── Services/        # 業務邏輯服務（21 個）
├── Helpers/         # 轉換器、輔助工具
└── Resources/
    ├── Styles/      # AppStyles.xaml、DarkTheme.xaml、Accent*.xaml
    ├── Localization/# Strings.zh-TW.xaml、Strings.en.xaml、Strings.ja.xaml
    ├── Highlighting/# MySQL.xshd 語法高亮定義
    └── Icons/       # 應用程式圖示
```

**技術堆疊**

| 套件 | 版本 | 用途 |
|------|------|------|
| MySqlConnector | 2.3.7 | MySQL 驅動（非同步） |
| CommunityToolkit.Mvvm | 8.2.2 | MVVM 框架 |
| AvalonEdit | 6.3.0 | SQL 語法高亮編輯器 |
| Newtonsoft.Json | 13.0.3 | JSON 序列化 |
| SSH.NET | 2024.2.0 | SSH Tunnel 支援 |

---

## ⚙ 設定檔位置

所有設定儲存於本機，**不含任何敏感資訊**：

```
%AppData%\MySQLManager\
├── settings.json       # 連線設定（密碼加密儲存）
├── theme.json          # 主題偏好
├── query_history.json  # 查詢歷史
├── snippets.json       # SQL Snippet 庫
├── format_options.json # SQL 格式化設定
└── column_widths.json  # 欄寬記憶
```

---

## 🤝 貢獻

歡迎 Issue 和 Pull Request！

1. Fork 本專案
2. 建立功能分支 `git checkout -b feature/AmazingFeature`
3. Commit 你的修改 `git commit -m 'Add AmazingFeature'`
4. Push 到分支 `git push origin feature/AmazingFeature`
5. 開啟 Pull Request

---

## 📄 授權

本專案採用 [MIT License](LICENSE) 授權。

---

<div align="center">

**MySQL Manager** — 開源的 MySQL 桌面管理工具

⭐ 如果這個專案對你有幫助，歡迎給個 Star！

</div>
=======
# MySQLManager
This is a MySQL desktop management tool developed with C#. For now, it focuses on MySQL, but in the future, support for other databases will be gradually added along with more comprehensive features.
>>>>>>> a3d0a911548bec6515f6b844a9b9c885fcb8aba5
