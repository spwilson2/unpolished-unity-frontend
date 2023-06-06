using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

using UnityEngine;
using UnityEngine.XR;


public class GameManager : MonoBehaviour
{

    Server server = null;
    public GameObject prefabTile;
    Dictionary<int, GameObject> objects;

    // Start is called before the first frame update
    void Start()
    {
        server = new Server();
        server.Start();
        objects = new Dictionary<int, GameObject>();
    }


    // Update is called once per frame
    void Update()
    {

        Command data;
        while (server.ReadQueue(out data))
        {
            //Debug.Log($"{data.Cmd}, {data.pos_x}, {data.pos_y}");
            if (data.Cmd == "spawn")
            {
                var obj = Instantiate(prefabTile);
                obj.transform.position = new Vector3(data.pos_x, data.pos_y, 0);
                objects.Add(data.uid, obj);
            }
            else if (data.Cmd == "move")
            {
                GameObject obj;
                if (objects.TryGetValue(data.uid, out obj))
                {
                    obj.transform.position = new Vector3(data.pos_x, data.pos_y, 0);
                }

            } else if (data.Cmd == "end") {
                Debug.Log($"{objects.Count}");
            }
        }

    }

    //public struct Command {
    //    public string s;

    //    public Command(string s) {
    //        this.s = s;
    //    }
    //}

    [StructLayout(LayoutKind.Explicit)]
    public struct Command
    {
        [FieldOffset(0)]
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string Cmd;

        [FieldOffset(8)]
        public float pos_x;

        [FieldOffset(12)]
        public float pos_y;

        [FieldOffset(16)]
        public int uid;

        public static Command fromData(Byte[] bytes)
        {
            return ByteToType<Command>(new BinaryReader(new MemoryStream(bytes)));
        }
        // TODO
        static T ByteToType<T>(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(Marshal.SizeOf(typeof(T)));

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T theStructure = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();

            return theStructure;

        }
    }


    static int cmd_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Command));
    public class Server
    {
        private Thread backgroundThread;
        private TcpListener listener;
        private Queue<Command> queue;
        private SpinLock spinlock;
        private byte[] byteBuffer = new byte[1024 * 1024];
        private byte[] scratchBuffer = new byte[cmd_size];
        private int scratchSaved = 0;
        NetworkStream _stream;

        public void Start()
        {

            queue = new Queue<Command>();
            spinlock = new SpinLock();
            // Create a thread
            backgroundThread = new Thread(new ThreadStart(this.Run));


            backgroundThread.Start();
        }

        public bool ReadQueue(out Command result)
        {
            bool taken = false;
            spinlock.Enter(ref taken);
            var res = queue.TryDequeue(out result);
            spinlock.Exit();
            return res;
        }
        private void PushQueue(Command data)
        {
            bool taken = false;
            spinlock.Enter(ref taken);
            queue.Enqueue(data);
            spinlock.Exit();
        }
        private int ReadStream(NetworkStream stream) {
            int readLen = stream.Read(byteBuffer, 0, byteBuffer.Length);
            if (readLen <= 0) {
                //Debug.Log($"readLen, {readLen}");
                return readLen;
            }
            var bufStart = 0;

            // Try and reassemble
            if (scratchSaved >= 1 && readLen > 0) {
                (bufStart, _) = pushScratch(byteBuffer.AsSpan(0, readLen).ToArray());
            }

            // Iteratively attempt reassemble
            for (int i =bufStart; i + cmd_size < readLen; i += cmd_size)
            {
                pushMessageBytes(byteBuffer.AsSpan(i, cmd_size).ToArray());
            }

            // Stash any remaining
            var leftOver = (readLen - bufStart) % cmd_size;
            while (leftOver > 0) {
                (_, leftOver) = pushScratch(byteBuffer.AsSpan(readLen-leftOver, leftOver).ToArray());
            }

            return readLen;
        }

        (int, int) pushScratch(byte[] bytes) {
            var scratchSpace = cmd_size - scratchSaved;
            var toPush = 0;
            var remainder = 0;
            if (bytes.Length < scratchSpace) {
                toPush = bytes.Length;
            } else {
                remainder = bytes.Length - scratchSpace;
                toPush = scratchSpace;
            }
            for (int i = 0; i < toPush; i++)
            {
                scratchBuffer[scratchSaved + i] = bytes[i];
            }
            scratchSaved += toPush;
            if (scratchSaved == cmd_size)
            {
                // Fully reconstructed, dump it
                pushMessageBytes(scratchBuffer);
                scratchSaved = 0;
            }

            return (toPush, remainder);
        }
        private Command pushMessageBytes(byte[] bytes)
        {
            var cmd = Command.fromData(bytes);
            PushQueue(cmd);
            if (cmd.Cmd == "end") {
                throw new Exception("Done");
            }
                return cmd;
        }
        public void Run() {
            while (true) {
        scratchSaved = 0;


        _Run(); } }
        public void _Run()
        {
            try
            {
                listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 13001);
                listener.Start();
                var sz = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Command));


                Byte[] bytes = new Byte[sz];
                String data = null;
                while (true)
                {
                    Console.Write("Waiting for a connection... ");
                    var client = listener.AcceptTcpClient();
                    data = null;
                    NetworkStream stream = client.GetStream();
                    _stream = stream;
                    while (true)
                    {
                        while (ReadStream(stream) > 0)
                        {
                        }
                        // 
                    }
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException: {0}", e);
            }
            catch (Exception e) { }
            finally
            {
                if (_stream != null)
                {
                    _stream.Close();
                    _stream = null;
                }
                listener.Stop();
            }
        }
    }
}