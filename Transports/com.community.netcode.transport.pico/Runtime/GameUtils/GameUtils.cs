using System;
using UnityEngine;

using Pico.Platform;
using Pico.Platform.Models;
using static Netcode.Transports.Pico.PicoTransport;

namespace Netcode.Transports.Pico
{
    public class GameUtils : MonoBehaviour
    {
        public static bool IsInt(string strNumber)
        {
            try
            {
                var result = Convert.ToInt32(strNumber);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDouble(string strNumber)
        {
            try
            {
                var result = Convert.ToDouble(strNumber);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetUserLogData(User obj)
        {
            string log =
                $"      DisplayName: {obj.DisplayName}, ID: {obj.ID}, Gender: {obj.Gender} \n " +
                $"      ImageURL: {obj.ImageUrl} \n" +
                $"      InviteToken: {obj.InviteToken}, PresenceStatus: {obj.PresenceStatus}";
            return log;
        }

        public static string GetUserListLogData(UserList obj)
        {
            string log = $" Count: {obj.Count}\n";
            var list = obj.GetEnumerator();
            while (list.MoveNext())
            {
                var item = list.Current;
                log += $"   {GetUserLogData(item)}\n";
            }

            return log;
        }

        public static string GetRoomLogData(Room room)
        {
            string ownerOpenID = "invalid";
            if (room.OwnerOptional != null)
            {
                ownerOpenID = room.OwnerOptional.ID;
            }
            string str = $"RoomID: {room.RoomId}, Type: {room.RoomType}, Owner: {ownerOpenID}\n";
            str += $"   PicoRoom Description: {room.Description}\n";
            str += $"   IsMembershipLocked: {room.IsMembershipLocked}, JoinPolicy: {room.RoomJoinPolicy}, " +
                   $"Joinability: {room.RoomJoinability}, MaxUsers: {room.MaxUsers}\n";
            if (room.UsersOptional == null)
            {
                str += "    UserList is invalid\n";
            }
            else
            {
                str += $"   Users:\n    {GetUserListLogData(room.UsersOptional)}\n";
            }
            return str;
        }

        public static string GetMatchmakingAdminSnapshotCandidateLogData(MatchmakingAdminSnapshotCandidate obj)
        {
            return $"MatchmakingAdminSnapshotCandidate[\nCanMatch: {obj.CanMatch}, MyTotalScore: {obj.MyTotalScore}, TheirCurrentThreshold: {obj.TheirCurrentThreshold}\n]MatchmakingAdminSnapshotCandidate";
        }

        public static string GetMatchmakingAdminSnapshotCandidateListLogData(MatchmakingAdminSnapshotCandidateList obj)
        {
            string log = $"MatchmakingAdminSnapshotCandidateList[\nCount: {obj.Count}";
            var list = obj.GetEnumerator();
            while (list.MoveNext())
            {
                var item = list.Current;
                log += $"{GetMatchmakingAdminSnapshotCandidateLogData(item)}\n";
            }

            return log + "]MatchmakingAdminSnapshotCandidateList";
        }

        public static string GetMatchmakingAdminSnapshotLogData(MatchmakingAdminSnapshot obj)
        {
            return $"MatchmakingAdminSnapshot[\nCandidates: {GetMatchmakingAdminSnapshotCandidateListLogData(obj.CandidateList)}\nMyCurrentThreshold: {obj.MyCurrentThreshold}\n]MatchmakingAdminSnapshot";
        }

        public static string GetMatchmakingEnqueueResultLogData(MatchmakingEnqueueResult obj)
        {
            string log = "MatchmakingEnqueueResult[\n";
            if (obj.AdminSnapshotOptional == null)
            {
                log += "AdminSnapshotOptional: null\n";
            }
            else
            {
                log += $"AdminSnapshotOptional: {GetMatchmakingAdminSnapshotLogData(obj.AdminSnapshotOptional)}\n";
            }

            log += $"AverageWait: {obj.AverageWait}, MatchesInLastHourCount: {obj.MatchesInLastHourCount}, MaxExpectedWait: {obj.MaxExpectedWait}, " +
                   $"Pool: {obj.Pool}, RecentMatchPercentage: {obj.RecentMatchPercentage}";
            return log + "\n]MatchmakingEnqueueResult";
        }

        public static RoomOptions GetRoomOptions(string roomID, string maxUserResults, string turnOffUpdates, string dataKeys, string dataValuses, string excludeRecentlyMet)
        {
            RoomOptions options = new RoomOptions();
            if (!string.IsNullOrEmpty(roomID))
            {
                options.SetRoomId(Convert.ToUInt64(roomID));
            }
            if (!string.IsNullOrEmpty(maxUserResults))
            {
                options.SetMaxUserResults(Convert.ToUInt32(maxUserResults));
            }
            if (!string.IsNullOrEmpty(turnOffUpdates))
            {
                options.SetTurnOffUpdates(Convert.ToBoolean(turnOffUpdates));
            }
            if (!string.IsNullOrEmpty(turnOffUpdates))
            {
                options.SetExcludeRecentlyMet(Convert.ToBoolean(excludeRecentlyMet));
            }
            if (!string.IsNullOrEmpty(dataKeys) && !string.IsNullOrEmpty(dataValuses))
            {
                string[] keys = dataKeys.Split(';');
                string[] values = dataValuses.Split(';');
                if (keys.Length != values.Length)
                {
                    PicoTransportLog(LogLevel.Warn, "dataKeys.Length != dataValuses.Length");
                }
                else
                {
                    for (int i = 0; i < keys.Length; i++)
                    {
                        options.SetDataStore(keys[i], values[i]);
                    }
                }
            }

            return options;
        }        
    }
}

