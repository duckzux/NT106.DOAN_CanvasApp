using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CanvasApp.Common.Models;

namespace CanvasApp.Client.Networking
{
    public class NetworkClient
    {
        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private bool _isConnected;

        // Sự kiện để báo cho UI (Form) biết khi có tin nhắn mới
        public event Action<Message> MessageReceived;

        // Sự kiện báo lỗi hoặc mất kết nối
        public event Action<string> OnDisconnected;

        public async Task<bool> ConnectAsync(string host, int port)
        {
            try
            {
                _tcpClient = new TcpClient();
                // Kết nối tới Server
                await _tcpClient.ConnectAsync(host, port);
                _isConnected = true;

                NetworkStream stream = _tcpClient.GetStream();
                // Khởi tạo luồng đọc/ghi với encoding UTF8
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                _reader = new StreamReader(stream, Encoding.UTF8);

                // Khởi chạy vòng lặp lắng nghe tin nhắn từ Server trong background
                Task.Run(() => ReceiveLoopAsync());

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client Connect Error] {ex.Message}");
                return false;
            }
        }

        // Vòng lặp liên tục đọc dữ liệu từ Server (Thường tách ra lớp ReceiverTask)
        private async Task ReceiveLoopAsync()
        {
            try
            {
                string line;
                while (_isConnected && (line = await _reader.ReadLineAsync()) != null)
                {
                    // Chuyển chuỗi JSON nhận được thành Object
                    Message msg = Message.FromJson(line);
                    if (msg != null)
                    {
                        // Kích hoạt sự kiện để Form bắt được tin nhắn này
                        MessageReceived?.Invoke(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Client Receive Error] {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        // Hàm gửi tin nhắn lên Server (Dùng trong SenderTask hoặc gọi trực tiếp)
        public async Task SendMessageAsync(Message message)
        {
            if (_isConnected && _writer != null)
            {
                try
                {
                    string json = message.ToJson();
                    // Viết chuỗi JSON kèm theo ký tự xuống dòng (quan trọng để ReadLineAsync() hoạt động)
                    await _writer.WriteLineAsync(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Client Send Error] {ex.Message}");
                    Disconnect();
                }
            }
        }

        public void Disconnect()
        {
            if (_isConnected)
            {
                _isConnected = false;
                _reader?.Close();
                _writer?.Close();
                _tcpClient?.Close();
                OnDisconnected?.Invoke("Disconnected from server.");
            }
        }
    }
}