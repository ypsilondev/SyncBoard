using System;

namespace SyncBoard
{
    class Network
    {
        public static String URL { get; private set;  } = "http://yjulian.xyz:5000/";

        public static void SetServer(string host)
        {
            URL = "http://" + host + ":5000/";
        }
    }
}
