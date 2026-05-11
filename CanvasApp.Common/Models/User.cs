using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CanvasApp.Common.Models
{
    public class User
    {
        public string ConnectionId { get; set; } // ID của Socket Session
        public string Username { get; set; }     // Tên hiển thị
        public string Color { get; set; } = "#6c5ce7"; // Màu avatar mặc định (Tím)
        public string Role { get; set; } = RoomRole.Member.ToString(); // Vai trò
    }
}
