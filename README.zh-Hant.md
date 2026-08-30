# JavMetaLite

<img src="JavMetaLite.App/Resources/Brand/JavMetaLite-64.png" width="64" alt="JavMetaLite 圖示">

[简体中文](README.zh-Hans.md) · **繁體中文** · [English](README.md) · [日本語](README.ja.md)

一次只整理一部影片的輕量 Windows metadata 編輯器。選擇或拖入影片、搜尋資料、逐項檢查及修改，再於儲存前預覽所有檔案變更。JavMetaLite 不會掃描媒體庫，也不會在使用者確認前寫入或移動影片。

## 主要功能

- 只處理目前選擇的一部影片，不提供批次刮削或媒體庫掃描。
- LibreDMM 提供日文資料，R18.dev 提供英文資料，JAVLibrary 可作為手動網頁備用來源。
- 多來源搜尋後可為每個欄位選擇資料來源，也可繼續手動修改。
- 讀取並安全更新本機 NFO、poster 和 fanart，同時保留無法辨識的 XML。
- 產生 Jellyfin 相容的 NFO、poster、fanart，以及可選的 `extrafanart/`。
- 可讓影片留在原位、在原地建立番號資料夾，或整理到自訂目標根目錄。
- 跨磁碟或 UNC 目標使用安全複製與 SHA-256 校驗，失敗時回復原狀。
- 儲存前預設顯示實際變更預覽；目標影片衝突時一律阻止執行。
- 可攜、自包含的 Windows x64 單一執行檔，無需安裝 .NET Runtime。

## 快速開始

1. 從 [GitHub Releases](https://github.com/Noredge/JavMetaLite/releases) 下載 `JavMetaLite-v1.0.0-win-x64-portable.zip`。
2. 對照同一個 Release 內的 `SHA256SUMS.txt` 校驗壓縮檔，然後解壓縮。
3. 執行 `JavMetaLite.exe`，選擇或拖入一個影片。
4. 檢查番號並搜尋資料，選擇合適的文字與封套來源。
5. 修改所需欄位，選擇輸出內容和目標位置。
6. 檢查儲存前變更預覽，確認後執行。

首次執行未簽署的程式時，Windows 可能顯示 SmartScreen 提示。請只從本儲存庫的正式 Release 下載，並核對 SHA-256。

## 輸出範例

```text
目標根目錄/
  IPX-123/
    IPX-123.mp4
    IPX-123.nfo
    IPX-123-poster.jpg
    IPX-123-fanart.jpg
    extrafanart/       # 可選
      fanart1.jpg
      fanart2.jpg
```

## 資料來源

| 來源 | 主要用途 | 說明 |
| --- | --- | --- |
| LibreDMM | 日文資料、完整封套、樣張 | 建議的日文來源 |
| R18.dev | 英文資料、完整封套、Gallery | 英文輸出與輔助來源 |
| JAVLibrary | 手動網頁匯入 | 網站要求驗證或自動來源失敗時使用 |

來源網站可能變更或暫時無法使用。多來源搜尋會限制每個來源的等待時間；失敗時可切換來源或手動填寫，不需要反覆重新啟動程式。

## 安全設計

- 預設不移動影片，也不直接覆蓋 metadata。
- 預覽視窗顯示即將新增、更新、移動或保持不變的檔案。
- 影片目標已存在時不會覆蓋另一個影片。
- 跨磁碟區傳輸會在刪除來源前校驗檔案大小與 SHA-256。
- 提交失敗時會還原已覆蓋的 metadata，並盡可能保持原影片位置不變。
- 搜尋時會將辨識出的番號傳送給使用者選擇的資料來源。選擇影片和讀取本機 NFO 不會自動連線寫入。
- 手動匯入 JAVLibrary 時只會讀取目前影片頁面；內建 WebView2 瀏覽器可能保留網站驗證所需的 Cookie。

任何檔案整理工具都不能取代備份。請先備份重要影片，第一次使用自訂目標位置時也請使用測試副本。

## 系統需求與限制

- Windows 10/11 x64。
- 首次執行會跟隨 Windows 的簡體中文、繁體中文、英文或日文顯示語言；其他系統語言會回退到英文，之後記住使用者選擇。
- 內建瀏覽器需要 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已經安裝。
- 支援選擇 MP4、MKV、AVI、WMV 影片；不會寫入容器內部 metadata。
- 不掃描媒體庫、不批次處理，也不會自動搬移未知字幕或伴隨檔案。
- 暫不產生 `actors/`；演員圖片透過 NFO 內的遠端 `thumb` 提供。
- 實際網路共用的速度、權限和可用性取決於 Windows 與目標伺服器。
- 請遵守資料來源網站的使用條款，並僅以合理頻率查詢。
- 資料來源可能包含成人內容。請僅在當地法律允許且符合使用者年齡的情況下使用。

執行記錄位於 `%LOCALAPPDATA%\JavMetaLite\Logs`，預設保留最近 14 天。使用者偏好位於 `%LOCALAPPDATA%\JavMetaLite\settings.json`。

## 開發與測試

需要 .NET 10 SDK：

```powershell
dotnet build .\JavMetaLite.App\JavMetaLite.App.csproj
.\scripts\Test-Automated.ps1
```

產生乾淨的 Windows x64 可攜套件與 SHA-256：

```powershell
.\scripts\New-ReleasePackage.ps1
```

自動化測試層級請參閱 [TESTING.md](TESTING.md)，版本歷史請參閱 [CHANGELOG.md](CHANGELOG.md)。

## 授權條款

JavMetaLite 採用 [MIT License](LICENSE)，版權所有 © 2026 Noredge。第三方元件使用各自的授權條款，詳見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

JavMetaLite 與其讀取的資料來源網站沒有隸屬或合作關係。本專案的 MIT License 不代表對來源網站資料的再授權。
