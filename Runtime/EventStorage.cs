using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Truesoft.Analytics
{
    public class EventStorage : MonoBehaviour
    {
        private Queue<string> memoryQueue = new Queue<string>();
        private const string PersistentFile = "CollectEventQueue.json";
        private const int MaxStoredEvents = 500;

        private bool isSending = false;

        public void Enqueue(Dictionary<string, object> data)
        {
            string json = JsonUtility.ToJson(new JsonWrapper(data));
            memoryQueue.Enqueue(json);
            SaveToDisk();
            TrySend();
        }

        private void SaveToDisk()
        {
            var storedList = new List<string>(memoryQueue);
            if (storedList.Count > MaxStoredEvents)
                storedList.RemoveRange(0, storedList.Count - MaxStoredEvents);

            string path = Path.Combine(Application.persistentDataPath, PersistentFile);
            File.WriteAllText(path, JsonUtility.ToJson(new JsonListWrapper(storedList)));
        }

        public void LoadFromDisk()
        {
            string path = Path.Combine(Application.persistentDataPath, PersistentFile);
            if (File.Exists(path))
            {
                var wrapper = JsonUtility.FromJson<JsonListWrapper>(File.ReadAllText(path));
                foreach (var item in wrapper.items)
                    memoryQueue.Enqueue(item);
            }
        }

        public void TrySend()
        {
            if (!isSending)
                Task.Run(() => SendLoop());
        }

        private async Task SendLoop()
        {
            isSending = true;
            while (memoryQueue.Count > 0)
            {
                string json = memoryQueue.Peek();
                bool success = await SendToServer(json);
                if (success)
                {
                    memoryQueue.Dequeue();
                    SaveToDisk();
                }
                else
                {
                    await Task.Delay(2000);
                }
            }

            isSending = false;
        }

        private async Task<bool> SendToServer(string json)
        {
            using (UnityWebRequest www = UnityWebRequest.Put("<API_URL>", json))
            {
                www.method = "POST";
                www.SetRequestHeader("Content-Type", "application/json");
                await www.SendWebRequest();

                return www.result == UnityWebRequest.Result.Success;
            }
        }

        // 내부 JSON 직렬화용
        [Serializable]
        private class JsonWrapper
        {
            public Dictionary<string, object> wrapper;

            public JsonWrapper(Dictionary<string, object> d)
            {
                wrapper = d;
            }
        }

        [Serializable]
        private class JsonListWrapper
        {
            public List<string> items;

            public JsonListWrapper(List<string> l)
            {
                items = l;
            }
        }
    }
}
