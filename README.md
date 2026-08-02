# Blox Fruits 自動打怪 Bot (C# WinForms + Python)

這是一個用於 Blox Fruits 的自動打怪 Bot 專案，結合了 C# WinForms 介面和 Python 決策邏輯。C# 端負責與遊戲視窗互動（尋找視窗句柄、截圖、發送按鍵），並透過 Named Pipes 與 Python 端通訊。Python 端接收截圖路徑，進行圖像分析（此部分需自行實作），並回傳 JSON 格式的決策，C# 端再根據決策執行相應的遊戲操作。

## 架構概述

* **C# WinForms 應用程式**：

  * 尋找 Roblox 遊戲視窗句柄 (HWND)。
  * 截取 Roblox 視窗畫面並儲存為圖片。
  * 透過 **Named Pipes** 與 Python 端進行通訊，發送截圖路徑並接收 JSON 決策。
  * 使用 Windows API 的 **PostMessage** 對 Roblox 視窗後台發送按鍵和滑鼠事件。
  * 提供「開始」、「停止」按鈕和日誌視窗 (RichTextBox) 用於使用者互動和狀態顯示。
  * 包含狀態機管理，根據 Python 決策切換 Bot 行為模式。
* **Python 腳本**：

  * 作為 Named Pipes 客戶端，接收 C# 端發送的截圖路徑。
  * **（待實作）** 根據截圖進行圖像分析，判斷遊戲狀態（例如：HP/能量比例、目標可見性、目標 ID）。
  * 根據分析結果，生成 JSON 格式的決策（`DecisionIntent`），並回傳給 C# 端。

## 功能需求

1. **自動尋找 Roblox 視窗句柄**：透過視窗標題尋找 Roblox 遊戲視窗。
2. **截取 Roblox 視窗畫面**：定期截取遊戲畫面並保存為 PNG 圖片。
3. **Named Pipe 通訊**：C# 作為伺服器，Python 作為客戶端，實現雙向通訊。
4. **JSON 決策處理**：C# 端解析 Python 回傳的 JSON 決策，Python 端生成 JSON 決策。
5. **後台按鍵發送**：根據決策，使用 `PostMessage` 向 Roblox 視窗發送按鍵和滑鼠事件。
6. **狀態機管理**：Bot 根據 Python 決策在 `GET\\\\\\\_QUEST` (接任務)、`TRANSFORM` (變身)、`COMBAT` (戰鬥)、`NAVIGATE` (巡邏) 之間切換。
7. **日誌顯示**：所有操作和狀態變化實時顯示在 WinForms 介面的日誌視窗中。
8. **一鍵啟動/緊急停止**：提供簡單的控制按鈕。

## Python 端回傳的 JSON 格式

```json
{
    "HpRatio": 0.85,
    "EnergyRatio": 0.75,
    "TargetVisible": true,
    "TargetId": "Target\\\\\\\_Monster",
    "DecisionIntent": "CombatMode"
}
```

`DecisionIntent` 包含三種：

* `"InteractWithNPC"`：接任務，接完後觸發 2→1 變身連招（按2開大佛，再按1切龍拳）。
* `"CombatMode"`：戰鬥模式，左鍵平A連點 + Z、X 技能隨機交替放。
* `"Navigate"`：巡邏移動，W前進 + Space跳躍防卡。

## 按鍵配置

* `2`：開啟大佛型態
* `1`：切換龍拳技能
* `Left Click`：一般攻擊/平A
* `Z`、`X`：龍拳爆發技能（隨機交替）
* `Q`：衝刺/位移 (目前 C# 端未實作，可根據需求在 `BotCore.cs` 中添加)
* `W`、`Space`：移動與跳躍

## 專案結構

```
BloxFruitsBot/
├── BloxFruitsBot.csproj
├── BloxFruitsBot.sln
├── Program.cs
├── MainForm.cs
├── MainForm.Designer.cs
├── Win32.cs
├── PythonResponse.cs
├── NamedPipeManager.cs
├── ScreenshotManager.cs
├── BotCore.cs
└── python\\\\\\\_client.py
└── README.md
```

## 設定與執行

### 1\. C# WinForms 應用程式

**環境要求**：

* Windows 作業系統
* .NET 8.0 SDK (或更高版本)
* Visual Studio (推薦，用於開發和編譯)

**編譯與執行**：

1. 使用 Visual Studio 開啟 `BloxFruitsBot.sln` 解決方案。
2. 建置 (Build) 專案以生成可執行文件。
3. 執行 `BloxFruitsBot.exe`。
4. 在 WinForms 介面中，點擊「開始」按鈕。

**注意事項**：

* 請確保 Roblox 遊戲視窗的標題為「Roblox」。如果不同，請修改 `MainForm.cs` 中的 `windowTitle` 變數。
* 截圖會保存到應用程式執行目錄下的 `Screenshots` 資料夾中。

### 2\. Python 客戶端

**環境要求**：

* Python 3.x

**執行**：

1. 開啟命令提示字元或終端機。
2. 導航到 `BloxFruitsBot` 資料夾。
3. 執行 Python 腳本：

&#x20;   `bash python python\\\\\\\_client.py `

**注意事項**：

* `python\\\\\\\_client.py` 是一個模擬腳本，它會隨機生成決策並發送給 C# 端。您需要根據實際需求，在 `python\\\\\\\_client.py` 中實作圖像識別和 AI 決策邏輯。
* Python 腳本必須在 C# 應用程式啟動並開始監聽 Named Pipe 後才能成功連接。

## 一鍵啟動器 (launcher.bat)

專案提供 `launcher.bat` 一鍵啟動腳本，自動完成以下步驟：

1. 檢查 Python 環境
2. 檢查並啟動 Ollama (本地 AI，若已安裝)
3. 編譯並啟動 C# WinForms 應用程式
4. 等待 C# 應用程式初始化後自動啟動 Python AI 客戶端

**使用方法**：雙擊 `launcher.bat` 或在命令提示字元中執行，然後在彈出的 C# 視窗點擊「開始」。

**環境要求**：Windows、.NET 8.0 SDK、Python 3.x、Ollama (可選，用於本地 AI 決策)。

## 核心組件說明

* **`Win32.cs`**：定義了所有必要的 Win32 API 函數和常數，用於尋找視窗、發送消息、截圖等。
* **`PythonResponse.cs`**：定義了 Python 端回傳 JSON 數據的 C# 物件模型。
* **`NamedPipeManager.cs`**：實作了 Named Pipe 伺服器，負責與 Python 客戶端建立連接、發送截圖路徑和接收 JSON 決策。
* **`ScreenshotManager.cs`**：提供了截取指定視窗畫面的功能，並將截圖保存為 PNG 文件。
* **`BotCore.cs`**：包含了 Bot 的核心邏輯，包括狀態機管理、根據 Python 決策執行按鍵和滑鼠操作。
* **`MainForm.cs`** / **`MainForm.Designer.cs`**：WinForms 應用程式的主介面，包含「開始」、「停止」按鈕和日誌顯示區域，並協調各組件的運作。
* **`Program.cs`**：應用程式的入口點。

## 擴展與改進

* **Python AI 決策**：將 `python\\\\\\\_client.py` 中的模擬決策替換為實際的圖像識別和 AI 邏輯，例如使用 OpenCV 識別遊戲元素、血條、能量條、目標等。
* **更精細的按鍵控制**：在 `BotCore.cs` 中添加更多遊戲按鍵的支援，例如 `Q` 衝刺。
* **錯誤處理與恢復**：增強錯誤處理機制，例如當遊戲崩潰或 Bot 卡住時的自動恢復邏輯。
* **配置化**：將遊戲視窗標題、按鍵延遲、截圖頻率等參數配置化，方便調整。
* **多目標支援**：擴展 Bot 以支援多個目標的選擇和攻擊策略。

希望這個專案能為您提供一個良好的起點！

