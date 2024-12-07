using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Collections.Generic;
using System.Text;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;
    private NetworkPipeline reliablePipeline;
    private NetworkPipeline nonReliablePipeline;
    private const ushort NetworkPort = 9001;
    private const int MaxConnections = 1000;

    private Dictionary<int, NetworkConnection> idToConnectionMap = new Dictionary<int, NetworkConnection>();
    private Dictionary<NetworkConnection, int> connectionToIDMap = new Dictionary<NetworkConnection, int>();

    private void Start()
    {
        if (NetworkServerProcessing.GetNetworkServer() == null)
        {
            NetworkServerProcessing.SetNetworkServer(this);
            DontDestroyOnLoad(this.gameObject);
            InitializeServer();
        }
        else
        {
            Debug.LogWarning("Duplicate NetworkServer detected. Destroying duplicate.");
            Destroy(this.gameObject);
        }
    }

    private void InitializeServer()
    {
        networkDriver = NetworkDriver.Create();
        reliablePipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliablePipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));

        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        if (networkDriver.Bind(endpoint) != 0)
        {
            Debug.LogError($"Failed to bind to port {NetworkPort}");
            return;
        }

        networkDriver.Listen();
        networkConnections = new NativeList<NetworkConnection>(MaxConnections, Allocator.Persistent);
        Debug.Log($"Server started on port {NetworkPort}");
    }

    private void OnDestroy()
    {
        Debug.Log("Shutting down server and disposing resources.");

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

        Debug.Log("Network resources disposed.");
    }

    private void Update()
    {
        if (!networkDriver.IsCreated)
        {
            Debug.LogWarning("NetworkDriver is not valid.");
            return;
        }

        networkDriver.ScheduleUpdate().Complete();

        AcceptIncomingConnections();
        ProcessConnections();
    }

    private void AcceptIncomingConnections()
    {
        while (true)
        {
            var connection = networkDriver.Accept();
            if (!connection.IsCreated) break;

            networkConnections.Add(connection);
            int clientID = AssignClientID(connection);
            NetworkServerProcessing.ConnectionEvent(clientID);
            Debug.Log($"Accepted new connection with ID {clientID}");
        }
    }

    private int AssignClientID(NetworkConnection connection)
    {
        int clientID = 0;
        while (idToConnectionMap.ContainsKey(clientID)) clientID++;

        idToConnectionMap[clientID] = connection;
        connectionToIDMap[connection] = clientID;

        return clientID;
    }

    private void ProcessConnections()
    {
        for (int i = 0; i < networkConnections.Length; i++)
        {
            var connection = networkConnections[i];
            if (!connection.IsCreated)
            {
                HandleDisconnection(connection);
                networkConnections.RemoveAtSwapBack(i);
                i--; // Adjust the index due to removal
                continue;
            }

            ProcessConnectionEvents(connection);
        }
    }

    private void ProcessConnectionEvents(NetworkConnection connection)
    {
        while (networkDriver.PopEventForConnection(connection, out var streamReader, out var pipeline) != NetworkEvent.Type.Empty)
        {
            switch (networkDriver.PopEventForConnection(connection, out streamReader, out pipeline))
            {
                case NetworkEvent.Type.Data:
                    ProcessDataEvent(streamReader, connection, pipeline);
                    break;
                case NetworkEvent.Type.Disconnect:
                    HandleDisconnection(connection);
                    break;
            }
        }
    }

    private void ProcessDataEvent(DataStreamReader streamReader, NetworkConnection connection, NetworkPipeline pipeline)
    {
        int clientID = connectionToIDMap[connection];

        var buffer = new NativeArray<byte>(streamReader.Length, Allocator.Temp);
        streamReader.ReadBytes(buffer);
        var message = Encoding.Unicode.GetString(buffer.ToArray());
        buffer.Dispose();

        NetworkServerProcessing.ReceivedMessageFromClient(message, clientID, pipeline == reliablePipeline ? TransportPipeline.ReliableAndInOrder : TransportPipeline.FireAndForget);
    }

    private void HandleDisconnection(NetworkConnection connection)
    {
        if (connectionToIDMap.TryGetValue(connection, out int clientID))
        {
            NetworkServerProcessing.DisconnectionEvent(clientID);
            idToConnectionMap.Remove(clientID);
            connectionToIDMap.Remove(connection);
        }

        Debug.Log($"Client {connection} disconnected.");
    }

    public void SendMessageToClient(string msg, int connectionID, TransportPipeline pipeline)
    {
        if (!idToConnectionMap.TryGetValue(connectionID, out var connection))
        {
            Debug.LogWarning($"Connection ID {connectionID} not found.");
            return;
        }

        var messageBytes = Encoding.Unicode.GetBytes(msg);

        using (var buffer = new NativeArray<byte>(messageBytes, Allocator.Temp))
        {
            networkDriver.BeginSend(pipeline == TransportPipeline.ReliableAndInOrder ? reliablePipeline : nonReliablePipeline, connection, out var writer);
            writer.WriteInt(buffer.Length);
            writer.WriteBytes(buffer);
            networkDriver.EndSend(writer);
        }
    }
}

public enum TransportPipeline
{
    ReliableAndInOrder,
    FireAndForget
}
