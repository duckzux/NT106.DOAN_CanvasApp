using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CanvasApp.Common.Models
{
    public class Room
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public List<User> Users { get; set; }

        public Room()
        {
            Users = new List<User>();
        }
    }
}
