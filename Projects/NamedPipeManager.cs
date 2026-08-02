using System; 
using System.IO; 
using System.IO.Pipes; 
using System.Text; 
using System.Threading.Tasks; 
using System.Text.Json; 
 
namespace BloxFruitsBot 
{ 
    public class NamedPipeManager 
    { 
        private NamedPipeServerStream _pipeServer; 
        private string _pipeName; 
        private Action<string> _logAction; 
 
        public event Action<PythonResponse> OnPythonResponseReceived; 
 
        public NamedPipeManager(string pipeName, Action<string> logAction) 
        { 
            _pipeName = pipeName; 
            _logAction = logAction; 
        } 
 
        public void StartServer() 
        { 
            _logAction($"啟動 Named Pipe 伺服器: {_pipeName}"); 
            Task.Run(async () => 
            { 
                try 
                { 
                    _pipeServer = new NamedPipeServerStream( 
                        _pipeName, 
                        PipeDirection.InOut, 
                        1, // Max number of server instances 
                        PipeTransmissionMode.Byte, 
                        PipeOptions.Asynchronous); 
 
                    _logAction("等待 Python 客戶端連接..."); 
                    await _pipeServer.WaitForConnectionAsync(); 
                    _logAction("Python 客戶端已連接。"); 
 
                    // Start listening for messages 
                    _ = Task.Run(async () => await ListenForMessages()); 
                } 
                catch (Exception ex) 
                { 
                    _logAction($"Named Pipe 伺服器啟動失敗: {ex.Message}"); 
                } 
            }); 
        } 
 
        public void StopServer() 
        { 
            if (_pipeServer != null && _pipeServer.IsConnected) 
            { 
                _pipeServer.Disconnect(); 
                _logAction("Python 客戶端已斷開連接。"); 
            } 
            _pipeServer?.Dispose(); 
            _logAction("Named Pipe 伺服器已停止。"); 
        } 
 
        public async Task SendMessage(string message) 
        { 
            if (_pipeServer != null && _pipeServer.IsConnected) 
            { 
                try 
                { 
                    byte[] buffer = Encoding.UTF8.GetBytes(message); 
                    await _pipeServer.WriteAsync(buffer, 0, buffer.Length); 
                    await _pipeServer.FlushAsync(); 
                    _pipeServer.WaitForPipeDrain(); 
                    _logAction($"發送訊息到 Python: {message}"); 
                } 
                catch (Exception ex) 
                { 
                    _logAction($"發送訊息失敗: {ex.Message}"); 
                    StopServer(); // Attempt to stop and restart if connection is lost 
                    StartServer(); 
                } 
            } 
            else 
            { 
                _logAction("Named Pipe 未連接，無法發送訊息。"); 
            } 
        } 
 
        private async Task ListenForMessages() 
        { 
            byte[] buffer = new byte[4096]; 
            while (_pipeServer != null && _pipeServer.IsConnected) 
            { 
                try 
                { 
                    int bytesRead = await _pipeServer.ReadAsync(buffer, 0, buffer.Length); 
                    if (bytesRead > 0) 
                    { 
                        string jsonString = Encoding.UTF8.GetString(buffer, 0, bytesRead); 
                        _logAction($"從 Python 接收到訊息: {jsonString}"); 
                        PythonResponse response = JsonSerializer.Deserialize<PythonResponse>(jsonString); 
                        OnPythonResponseReceived?.Invoke(response); 
                    } 
                } 
                catch (IOException ex) 
                { 
                    _logAction($"Named Pipe 讀取錯誤: {ex.Message}"); 
                    break; // Exit loop on disconnect 
                } 
                catch (Exception ex) 
                { 
                    _logAction($"處理 Python 訊息時發生錯誤: {ex.Message}"); 
                } 
            } 
            _logAction("Python 客戶端斷開連接或伺服器停止。"); 
            StopServer(); 
            StartServer(); // Attempt to restart server after disconnect 
        } 
    } 
}
