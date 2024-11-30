using System.Collections;
using System.Collections.Generic;
using UnityEngine;

static public class NetworkServerProcessing
{

    #region Send and Receive Data Functions
    private static Dictionary<int, string> connectionToUsername = new Dictionary<int, string>();
    private static Dictionary<string, string> accounts = new Dictionary<string, string>();

    public static void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string username = csv[1];
            string password = csv[2];
            if (!accounts.ContainsKey(username))
            {
                accounts[username] = password;
                SendMessageToClient($"{ServerToClientSignifiers.AccountCreated}", clientConnectionID, pipeline);
            }
            else
            {
                SendMessageToClient($"{ServerToClientSignifiers.AccountCreationFailed}", clientConnectionID, pipeline);
            }
        }
        else if (signifier == ClientToServerSignifiers.Login)
        {
            string username = csv[1];
            string password = csv[2];
            if (accounts.ContainsKey(username) && accounts[username] == password)
            {
                connectionToUsername[clientConnectionID] = username;
                SendMessageToClient($"{ServerToClientSignifiers.LoginSuccessful}", clientConnectionID, pipeline);
            }
            else
            {
                SendMessageToClient($"{ServerToClientSignifiers.LoginFailed}", clientConnectionID, pipeline);
            }
        }
    }


    static public void SendMessageToClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        networkServer.SendMessageToClient(msg, clientConnectionID, pipeline);
    }

    #endregion

    #region Connection Events

    static public void ConnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client connection, ID == " + clientConnectionID);
    }
    static public void DisconnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client disconnection, ID == " + clientConnectionID);
    }

    #endregion

    #region Setup
    static NetworkServer networkServer;
    static GameLogic gameLogic;

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }
    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }
    static public void SetGameLogic(GameLogic GameLogic)
    {
        gameLogic = GameLogic;
    }

    #endregion
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1; // For account creation requests
    public const int Login = 2;        // For login requests
}

public static class ServerToClientSignifiers
{
    public const int AccountCreated = 1;           // Response for successful account creation
    public const int AccountCreationFailed = 2;    // Response for failed account creation
    public const int LoginSuccessful = 3;          // Response for successful login
    public const int LoginFailed = 4;              // Response for failed login
}

