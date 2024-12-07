using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using System.Collections.Generic;
using System;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;
    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;
    const ushort NetworkPort = 9001;
    const int MaxNumberOfClientConnections = 1000;
    Dictionary<int, NetworkConnection> idToConnectionLookup;
    Dictionary<NetworkConnection, int> connectionToIDLookup;
    void Start()
    {
        if (NetworkServerProcessing.GetNetworkServer() == null)
        {
            NetworkServerProcessing.SetNetworkServer(this);
            DontDestroyOnLoad(this.gameObject);

            InitializeServer(); // Initialize server logic
            ClearAllGameRoomData(); // Clear all game room data on server start
        }
        else
        {
            Debug.Log("Singleton-ish architecture violation detected, investigate where NetworkServer.cs Start() is being called. Are you creating a second instance of the NetworkServer game object or has the NetworkServer.cs been attached to more than one game object?");
            Destroy(this.gameObject);
        }
    }

    private void ClearAllGameRoomData()
    {
        NetworkServerProcessing.ClearAllGameRoomData(); // Call the static method in NetworkServerProcessing
        Debug.Log("All game room data has been cleared on server start.");
    }

    private void InitializeServer()
    {
        #region Connect
        idToConnectionLookup = new Dictionary<int, NetworkConnection>();
        connectionToIDLookup = new Dictionary<NetworkConnection, int>();

        networkDriver = NetworkDriver.Create();
        reliableAndInOrderPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliableNotInOrderedPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));
        NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        int error = networkDriver.Bind(endpoint);
        if (error != 0)
            Debug.Log("Failed to bind to port " + NetworkPort);
        else
            networkDriver.Listen();

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);
        #endregion
    }


    void OnDestroy()
    {
        Debug.Log("Disposing of network driver and connections...");
        foreach (var connection in networkConnections)
        {
            if (connection.IsCreated)
            {
                networkDriver.Disconnect(connection);
            }
        }

        networkConnections.Dispose();
        if (networkDriver.IsCreated)
        {
            networkDriver.Dispose();
        }
        Debug.Log("Network resources disposed successfully.");
    }


    void Update()
    {
        // Ensure the driver state is valid
        if (!networkDriver.IsCreated)
        {
            Debug.LogWarning("NetworkDriver is not created.");
            return;
        }

        networkDriver.ScheduleUpdate().Complete();

        // Check for unused or invalid connections
        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
            {
                Debug.Log($"Removing unused connection at index {i}.");
                if (connectionToIDLookup.ContainsKey(networkConnections[i]))
                {
                    int clientID = connectionToIDLookup[networkConnections[i]];
                    NetworkServerProcessing.DisconnectionEvent(clientID);
                    connectionToIDLookup.Remove(networkConnections[i]);
                    idToConnectionLookup.Remove(clientID);
                }
                networkConnections.RemoveAtSwapBack(i);
                i--;
            }
        }

        // Accept new incoming connections
        while (AcceptIncomingConnection()) { }

        // Process events for each connection
        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated) continue;

            NetworkEvent.Type eventType;
            DataStreamReader streamReader;
            NetworkPipeline pipeline;

            while ((eventType = networkDriver.PopEventForConnection(networkConnections[i], out streamReader, out pipeline)) != NetworkEvent.Type.Empty)
            {
                switch (eventType)
                {
                    case NetworkEvent.Type.Connect:
                        Debug.Log($"Client connected: {connectionToIDLookup[networkConnections[i]]}");
                        break;

                    case NetworkEvent.Type.Data:
                        if (streamReader.IsCreated)
                        {
                            int dataLength = streamReader.ReadInt();
                            using (NativeArray<byte> buffer = new NativeArray<byte>(dataLength, Allocator.Temp))
                            {
                                streamReader.ReadBytes(buffer); // Read into the NativeArray
                                byte[] receivedData = buffer.ToArray(); // Convert to byte[]
                                string message = Encoding.Unicode.GetString(receivedData);
                                NetworkServerProcessing.ReceivedMessageFromClient(
                                    message,
                                    connectionToIDLookup[networkConnections[i]],
                                    TransportPipeline.ReliableAndInOrder
                                );
                            }
                        }
                        break;

                    case NetworkEvent.Type.Disconnect:
                        Debug.Log($"Client disconnected: {connectionToIDLookup[networkConnections[i]]}");
                        int disconnectedClientID = connectionToIDLookup[networkConnections[i]];
                        NetworkServerProcessing.DisconnectionEvent(disconnectedClientID);

                        connectionToIDLookup.Remove(networkConnections[i]);
                        idToConnectionLookup.Remove(disconnectedClientID);
                        networkConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }
    }

    void HandleDataEvent(DataStreamReader streamReader, int connectionIndex, NetworkPipeline pipelineUsedToSendEvent)
    {
        try
        {
            int sizeOfDataBuffer = streamReader.ReadInt();
            NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
            streamReader.ReadBytes(buffer);
            byte[] byteBuffer = buffer.ToArray();
            string msg = Encoding.Unicode.GetString(byteBuffer);
            NetworkServerProcessing.ReceivedMessageFromClient(msg, connectionToIDLookup[networkConnections[connectionIndex]], TransportPipeline.ReliableAndInOrder);
            buffer.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing data event: {ex.Message}");
        }
    }

    void HandleDisconnectEvent(int connectionIndex)
    {
        try
        {
            NetworkConnection nc = networkConnections[connectionIndex];
            if (connectionToIDLookup.ContainsKey(nc))
            {
                int id = connectionToIDLookup[nc];
                NetworkServerProcessing.DisconnectionEvent(id);
                idToConnectionLookup.Remove(id);
                connectionToIDLookup.Remove(nc);
            }
            networkConnections[connectionIndex] = default(NetworkConnection);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error handling disconnect event: {ex.Message}");
        }
    }

    void CheckDriverState()
    {
        try
        {
            if (!networkDriver.IsCreated)
            {
                Debug.LogError("NetworkDriver is no longer valid. Reinitializing...");
                networkDriver.Dispose();
                networkDriver = NetworkDriver.Create();
                Debug.Log("NetworkDriver reinitialized successfully.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error checking NetworkDriver state: {ex.Message}");
        }
    }

    private bool AcceptIncomingConnection()
    {
        try
        {
            NetworkConnection connection = networkDriver.Accept();
            if (connection == default(NetworkConnection))
                return false;

            networkConnections.Add(connection);

            int id = 0;
            while (idToConnectionLookup.ContainsKey(id))
            {
                id++;
            }
            idToConnectionLookup.Add(id, connection);
            connectionToIDLookup.Add(connection, id);

            NetworkServerProcessing.ConnectionEvent(id);
            Debug.Log($"New connection accepted: {id}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error accepting incoming connection: {ex.Message}");
            return false;
        }
    }


    private bool PopNetworkEventAndCheckForData(NetworkConnection networkConnection, out NetworkEvent.Type networkEventType, out DataStreamReader streamReader, out NetworkPipeline pipelineUsedToSendEvent)
    {
        networkEventType = networkConnection.PopEvent(networkDriver, out streamReader, out pipelineUsedToSendEvent);

        if (networkEventType == NetworkEvent.Type.Empty)
            return false;
        return true;
    }

    public void SendMessageToClient(string msg, int connectionID, TransportPipeline pipeline)
    {
        NetworkPipeline networkPipeline = reliableAndInOrderPipeline;
        if(pipeline == TransportPipeline.FireAndForget)
            networkPipeline = nonReliableNotInOrderedPipeline;

        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);
        DataStreamWriter streamWriter;

        networkDriver.BeginSend(networkPipeline, idToConnectionLookup[connectionID], out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

}

public enum TransportPipeline
{
    NotIdentified,
    ReliableAndInOrder,
    FireAndForget
}
