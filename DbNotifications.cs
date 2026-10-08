using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace ProcurePay
{
    // Notifications: reading and read/unread state. (Db.Notify / Db.NotifyRole, which create them, are in Db.cs.)
    public static partial class Db
    {
        // Unread notifications for a user. Never throws: returns -1 if the database cannot be read,
        // so the sidebar badge keeps its old number instead of showing a wrong one.
        public static int UnreadCount(string userName)
        {
            try { return (int)Scalar("SELECT COUNT(*) FROM Notifications WHERE UserName=@u AND IsRead=0", "@u", userName); }
            catch { return -1; }
        }

        public static int NotificationTotal(string userName)
        {
            return (int)Scalar("SELECT COUNT(*) FROM Notifications WHERE UserName=@u", "@u", userName);
        }

        // Newest first. The list is capped at 300 rows; NotificationTotal still counts them all.
        public static List<NotificationRow> NotificationList(string userName, bool unreadOnly)
        {
            var list = new List<NotificationRow>();
            string sql = "SELECT TOP 300 Id, EntityCode, Title, Message, Module, RecordId, IsRead, CreatedAt FROM Notifications WHERE UserName=@u"
                       + (unreadOnly ? " AND IsRead=0" : "") + " ORDER BY CreatedAt DESC, Id DESC";
            using (var c = Open())
            using (var r = Cmd(c, sql, null, "@u", userName).ExecuteReader())
                while (r.Read())
                    list.Add(new NotificationRow
                    {
                        Id = r.GetInt64(0),
                        EntityCode = r.IsDBNull(1) ? "" : r.GetString(1),
                        Title = r.GetString(2),
                        Message = r.IsDBNull(3) ? "" : r.GetString(3),
                        Module = r.IsDBNull(4) ? "" : r.GetString(4),
                        RecordId = r.IsDBNull(5) ? "" : r.GetString(5),
                        IsRead = r.GetBoolean(6),
                        CreatedAt = r.GetDateTime(7)
                    });
            return list;
        }

        // A user can only change their own notifications
        public static void NotificationSetRead(string userName, long id, bool read)
        {
            using (var c = Open())
                Cmd(c, "UPDATE Notifications SET IsRead=@r, ReadAt=CASE WHEN @r=1 THEN SYSDATETIME() ELSE NULL END WHERE Id=@i AND UserName=@u",
                    null, "@r", read, "@i", id, "@u", userName).ExecuteNonQuery();
        }

        public static void NotificationMarkAllRead(string userName)
        {
            using (var c = Open())
                Cmd(c, "UPDATE Notifications SET IsRead=1, ReadAt=SYSDATETIME() WHERE UserName=@u AND IsRead=0", null, "@u", userName).ExecuteNonQuery();
        }
    }
}
