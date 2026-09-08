using System;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public abstract class BaseCustomRTCSignalClient
    {
        protected MonoBehaviour ctx;
        protected string url;
        protected string token;

        public BaseCustomRTCSignalClient(MonoBehaviour context, string url, string token)
        {
            ctx = context;
            this.url = url;
            this.token = token;
        }

        public abstract Task Connect();
        public abstract Task Disconnect();
        public abstract void On(string eventName, Action<JsonElement> action);
        public abstract Task Emit(string eventName, params object[] payload);
    }
}
