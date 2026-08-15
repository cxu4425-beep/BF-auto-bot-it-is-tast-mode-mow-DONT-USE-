using System;
using System.Text;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

namespace BloxFruitsBot
{
    /// <summary>
    /// Named Pipe 伺服器：把截圖路徑送給 Python，並接收 Python 回傳的 JSON 決策。
    /// 通訊協定為「以 \n 分隔的 UTF-8 文字」，兩端都必須遵守。
    /// </summary>
    public class NamedPipeManager
    {
        private NamedPipeServerStream? _pipeServer;
        private readonly string _pipeName;
        private readonly Action<string> _logAction;
        private readonly object _sync = new object();

        // 使用者按下「停止」後就不該再自動重連
        private bool _shuttingDown;
        private bool _loopRunning;

        public event Action<PythonResponse>? OnPythonResponseReceived;

        public NamedPipeManager(string pipeName, Action<string> logAction)
        {
            _pipeName = pipeName;
            _logAction = logAction;
        }

        public void StartServer()
        {
            lock (_sync)
            {
                // 舊版在 SendMessage 的 catch 和 ListenForMessages 結尾各自呼叫
                // StopServer() + StartServer()，會疊出好幾條互相搶同一個 pipe 的迴圈。
                // 這裡改成單一常駐迴圈，並用旗標擋掉重複啟動。
                if (_loopRunning)
                {
                    return;
                }
                _loopRunning = true;
                _shuttingDown = false;
            }

            _logAction($"啟動 Named Pipe 伺服器: {_pipeName}");
            _ = Task.Run(ServerLoopAsync);
        }

        public void StopServer()
        {
            NamedPipeServerStream? server;
            lock (_sync)
            {
                _shuttingDown = true;
                server = _pipeServer;
                _pipeServer = null;
            }

            if (server != null)
            {
                try
                {
                    if (server.IsConnected)
                    {
                        server.Disconnect();
                    }
                }
                catch (Exception ex)
                {
                    _logAction($"中斷 Named Pipe 連線時發生錯誤: {ex.Message}");
                }

                // Dispose 會讓卡在 WaitForConnectionAsync / ReadAsync 的工作立刻拋例外結束
                server.Dispose();
            }
        }

        /// <summary>
        /// 常駐迴圈：等待連線 -> 收訊息 -> 斷線後自動重新等待，直到 StopServer() 被呼叫。
        /// </summary>
        private async Task ServerLoopAsync()
        {
            try
            {
                while (true)
                {
                    lock (_sync)
                    {
                        if (_shuttingDown) break;
                    }

                    NamedPipeServerStream server;
                    try
                    {
                        server = new NamedPipeServerStream(
                            _pipeName,
                            PipeDirection.InOut,
                            1, // Max number of server instances
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);
                    }
                    catch (Exception ex)
                    {
                        _logAction($"Named Pipe 伺服器建立失敗: {ex.Message}");
                        break;
                    }

                    lock (_sync)
                    {
                        if (_shuttingDown)
                        {
                            server.Dispose();
                            break;
                        }
                        _pipeServer = server;
                    }

                    try
                    {
                        _logAction("等待 Python 客戶端連接...");
                        await server.WaitForConnectionAsync();
                        _logAction("Python 客戶端已連接。");

                        await ListenForMessagesAsync(server);
                    }
                    catch (ObjectDisposedException)
                    {
                        // StopServer() 主動關閉，屬正常流程
                    }
                    catch (Exception ex)
                    {
                        _logAction($"Named Pipe 連線中斷: {ex.Message}");
                    }
                    finally
                    {
                        try
                        {
                            if (server.IsConnected)
                            {
                                server.Disconnect();
                            }
                        }
                        catch
                        {
                            // 已經斷了就算了
                        }

                        server.Dispose();

                        lock (_sync)
                        {
                            if (ReferenceEquals(_pipeServer, server))
                            {
                                _pipeServer = null;
                            }
                        }
                    }

                    lock (_sync)
                    {
                        if (_shuttingDown) break;
                    }

                    _logAction("Python 客戶端已斷開連接，重新等待連接...");
                    await Task.Delay(500);
                }
            }
            finally
            {
                lock (_sync)
                {
                    _loopRunning = false;
                }
                _logAction("Named Pipe 伺服器已停止。");
            }
        }

        public async Task SendMessage(string message)
        {
            NamedPipeServerStream? server;
            lock (_sync)
            {
                server = _pipeServer;
            }

            if (server == null || !server.IsConnected)
            {
                _logAction("Named Pipe 未連接，無法發送訊息。");
                return;
            }

            try
            {
                // Python 端的 receive_message() 是一路讀到 '\n' 才回傳。
                // 少了這個換行結尾，它會永遠卡在 read()，整個 Bot 就停在那裡不動了。
                if (!message.EndsWith("\n"))
                {
                    message += "\n";
                }

                byte[] buffer = Encoding.UTF8.GetBytes(message);
                await server.WriteAsync(buffer, 0, buffer.Length);
                await server.FlushAsync();
                _logAction($"發送訊息到 Python: {message.TrimEnd()}");
            }
            catch (Exception ex)
            {
                // 這裡不要自己 StopServer()+StartServer()，ServerLoopAsync 會偵測到斷線並自動重連
                _logAction($"發送訊息失敗: {ex.Message}");
            }
        }

        private async Task ListenForMessagesAsync(NamedPipeServerStream server)
        {
            byte[] buffer = new byte[4096];
            char[] charBuffer = new char[4096];

            // 用 Decoder 保留跨次讀取的半個 UTF-8 字元狀態，
            // 否則中文訊息剛好被切在位元組中間就會變成亂碼。
            Decoder decoder = Encoding.UTF8.GetDecoder();
            StringBuilder pending = new StringBuilder();

            while (server.IsConnected)
            {
                int bytesRead = await server.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    break; // 對端關閉
                }

                int charCount = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0);
                pending.Append(charBuffer, 0, charCount);

                // Python 端每筆決策都以 '\n' 結尾，必須照換行切開再解析。
                // 舊版直接把一次 ReadAsync 的內容當成一筆完整 JSON，
                // 只要訊息被切成兩半、或兩筆黏在同一次讀取裡，就會解析失敗。
                while (true)
                {
                    int newlineIndex = -1;
                    for (int i = 0; i < pending.Length; i++)
                    {
                        if (pending[i] == '\n')
                        {
                            newlineIndex = i;
                            break;
                        }
                    }

                    if (newlineIndex < 0)
                    {
                        break;
                    }

                    string line = pending.ToString(0, newlineIndex).Trim();
                    pending.Remove(0, newlineIndex + 1);

                    if (line.Length > 0)
                    {
                        HandleLine(line);
                    }
                }
            }
        }

        private void HandleLine(string line)
        {
            _logAction($"從 Python 接收到訊息: {line}");

            try
            {
                PythonResponse? response = JsonSerializer.Deserialize<PythonResponse>(line);
                if (response == null)
                {
                    _logAction("Python 回傳的 JSON 解析結果為 null，略過這筆。");
                    return;
                }

                OnPythonResponseReceived?.Invoke(response);
            }
            catch (JsonException ex)
            {
                _logAction($"解析 Python JSON 失敗: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logAction($"處理 Python 訊息時發生錯誤: {ex.Message}");
            }
        }
    }
}
