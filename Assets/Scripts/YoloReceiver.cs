using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using System.Threading;
using System.IO;
using System.Globalization;

public class YoloReceiver : MonoBehaviour
{
    TcpListener server;
    TcpClient client;
    NetworkStream stream;

    // === PERBAIKAN: thread-safe properties ===
    private readonly object detectionLock = new object();
    private bool _yoloDetected = false;
    private float _targetX = 0.5f;
    private float _targetBboxArea = 0f;

    public float targetBboxArea
    {
        get { lock (detectionLock) { return _targetBboxArea; } }
        private set { lock (detectionLock) { _targetBboxArea = value; } }
    }

    public bool yoloDetected
    {
        get { lock (detectionLock) { return _yoloDetected; } }
        private set { lock (detectionLock) { _yoloDetected = value; } }
    }

    public float targetX
    {
        get { lock (detectionLock) { return _targetX; } }
        private set { lock (detectionLock) { _targetX = value; } }
    }

    Thread receiveThread;

    public RenderTexture droneCamera;
    private Texture2D frameTexture;

    TcpListener frameServer;
    TcpClient frameClient;
    NetworkStream frameStream;
    Thread frameSendThread;

    private byte[] latestFrameBytes;
    private readonly object frameLock = new object();
    private bool hasNewFrame = false;

    void Start()
    {
        frameTexture = new Texture2D(
            droneCamera.width,
            droneCamera.height,
            TextureFormat.RGB24, false
        );

        receiveThread = new Thread(StartDetectionServer);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        frameSendThread = new Thread(StartFrameServer);
        frameSendThread.IsBackground = true;
        frameSendThread.Start();
    }

    void LateUpdate()
    {
        if (droneCamera == null) return;

        RenderTexture.active = droneCamera;
        frameTexture.ReadPixels(
            new Rect(0, 0, droneCamera.width, droneCamera.height), 0, 0
        );
        frameTexture.Apply();
        RenderTexture.active = null;

        byte[] jpgBytes = frameTexture.EncodeToJPG(75);

        lock (frameLock)
        {
            latestFrameBytes = jpgBytes;
            hasNewFrame = true;
        }
    }

    void StartDetectionServer()
    {
        server = new TcpListener(IPAddress.Any, 5005);
        server.Start();
        Debug.Log("[YoloReceiver] Waiting for YOLO connection on port 5005...");

        client = server.AcceptTcpClient();
        stream = client.GetStream();
        Debug.Log("[YoloReceiver] YOLO connected!");

        using (var reader = new StreamReader(stream, Encoding.ASCII))
        {
            while (true)
            {
                try
                {
                    string line = reader.ReadLine();
                    if (line == null) break;

                    line = line.Trim();

                    if (line.StartsWith("PERSON"))
                    {
                        string[] parts = line.Split(' ');
                        float parsedX = 0.5f;
                        float parsedArea = 0f;

                        if (parts.Length > 1)
                        {
                            float.TryParse(parts[1],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out parsedX);
                        }
                        if (parts.Length > 2)
                        {
                            float.TryParse(parts[2],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out parsedArea);
                        }

                        lock (detectionLock)
                        {
                            _yoloDetected = true;
                            _targetX = parsedX;
                            _targetBboxArea = parsedArea;
                        }
                    }
                    else
                    {
                        yoloDetected = false;
                    }
                }
                catch { break; }
            }
        }

        Debug.Log("[YoloReceiver] Detection connection closed.");
    }

    void StartFrameServer()
    {
        frameServer = new TcpListener(IPAddress.Any, 5006);
        frameServer.Start();
        Debug.Log("[YoloReceiver] Waiting for frame connection on port 5006...");

        frameClient = frameServer.AcceptTcpClient();
        frameStream = frameClient.GetStream();
        Debug.Log("[YoloReceiver] Frame receiver connected!");

        while (true)
        {
            try
            {
                byte[] currentFrame = null;
                bool send = false;

                lock (frameLock)
                {
                    if (hasNewFrame && latestFrameBytes != null)
                    {
                        currentFrame = latestFrameBytes;
                        hasNewFrame = false;
                        send = true;
                    }
                }

                if (send && currentFrame != null)
                {
                    byte[] sizeBytes = System.BitConverter.GetBytes(currentFrame.Length);
                    frameStream.Write(sizeBytes, 0, 4);
                    frameStream.Write(currentFrame, 0, currentFrame.Length);
                    frameStream.Flush();
                }

                Thread.Sleep(33);
            }
            catch { break; }
        }
    }

    void OnDestroy()
    {
        receiveThread?.Abort();
        frameSendThread?.Abort();
        server?.Stop();
        frameServer?.Stop();
        client?.Close();
        frameClient?.Close();
    }
}