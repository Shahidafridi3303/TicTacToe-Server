using System.Collections.Generic;
using UnityEngine;
using System.IO;

static public class NetworkServerProcessing
{
    private static Dictionary<int, string> connectionToUsername = new Dictionary<int, string>();
    private static Dictionary<string, string> accounts = new Dictionary<string, string>();
    private static string accountFilePath = "accounts.txt"; // File to save accounts
    static GameLogic gameLogic; // Reference to GameLogic
    static NetworkServer networkServer;

    private static Dictionary<string, List<int>> gameRooms = new Dictionary<string, List<int>>(); // Room name to list of client IDs

    public const int CreateOrJoinGameRoom = 4; // New signifier for game room creation/joining
    public const int LeaveGameRoom = 5; // New signifier for leaving a game room
    public const int StartGame = 6; // New signifier for starting the game

    private static Dictionary<string, int[,]> gameBoards = new Dictionary<string, int[,]>(); // Room to 3x3 board
    private static Dictionary<string, int> currentTurn = new Dictionary<string, int>(); // Room to current player

    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
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
                SaveAccounts();
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
        else if (signifier == ClientToServerSignifiers.CreateOrJoinGameRoom)
        {
            string roomName = csv[1];

            // Check if the room exists; if not, create it
            if (!gameRooms.ContainsKey(roomName))
                gameRooms[roomName] = new List<int>();

            // Add the player to the room
            gameRooms[roomName].Add(clientConnectionID);

            // Notify the client that they joined/created the room
            SendMessageToClient($"{ServerToClientSignifiers.GameRoomCreatedOrJoined},{roomName},{gameRooms[roomName].Count}", clientConnectionID, pipeline);

            // Check if the room is now full (2 players)
            if (gameRooms[roomName].Count == 2)
            {
                // Initialize the game logic for this room
                InitializeGame(roomName);

                // Get player connection IDs
                int player1 = gameRooms[roomName][0];
                int player2 = gameRooms[roomName][1];

                // Notify the clients of their roles and turns
                SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},X,1", player1, TransportPipeline.ReliableAndInOrder); // Player 1 (X)
                SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},O,0", player2, TransportPipeline.ReliableAndInOrder); // Player 2 (O)

                Debug.Log($"Game started in room {roomName} - Player 1: {player1} (X), Player 2: {player2} (O)");
            }
        }


        else if (signifier == ClientToServerSignifiers.LeaveGameRoom)
        {
            string roomName = csv[1];
            if (gameRooms.ContainsKey(roomName))
            {
                gameRooms[roomName].Remove(clientConnectionID);
                if (gameRooms[roomName].Count == 0)
                    gameRooms.Remove(roomName);
            }
        }
        else if (signifier == ClientToServerSignifiers.SendMessageToOpponent)
        {
            string roomName = csv[1];
            string message = csv[2];
            if (gameRooms.ContainsKey(roomName))
            {
                foreach (int clientID in gameRooms[roomName])
                {
                    if (clientID != clientConnectionID) // Send to opponent only
                    {
                        SendMessageToClient($"{ServerToClientSignifiers.OpponentMessage},{message}", clientID, pipeline);
                    }
                }
            }
        }
        else if (signifier == ClientToServerSignifiers.PlayerMove)
        {
            string roomName = csv[1];
            int x = int.Parse(csv[2]);
            int y = int.Parse(csv[3]);

            Debug.Log($"Received PlayerMove from Client {clientConnectionID}: Room {roomName}, x: {x}, y: {y}"); // Add this

            HandlePlayerMove(roomName, clientConnectionID, x, y);
        }

        else if (signifier == ClientToServerSignifiers.SendMessageToOpponent)
        {
            string roomName = csv[1];
            string message = csv[2];
            if (gameRooms.ContainsKey(roomName))
            {
                foreach (int clientID in gameRooms[roomName])
                {
                    if (clientID != clientConnectionID) // Send to opponent only
                    {
                        SendMessageToClient($"{ServerToClientSignifiers.OpponentMessage},{message}", clientID, pipeline);
                    }
                }
            }
        }

    }

    static public void SendMessageToClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        networkServer.SendMessageToClient(msg, clientConnectionID, pipeline);
    }

    static public void ConnectionEvent(int clientConnectionID)
    {
        LoadAccounts(); // Ensure accounts are loaded when a client connects
        List<string> formattedAccounts = new List<string>();
        foreach (var account in accounts)
        {
            formattedAccounts.Add($"{account.Key}:{account.Value}");
        }
        string accountList = string.Join(",", formattedAccounts); // Create a comma-separated list of username:password
        Debug.Log($"Sending Account List to Client {clientConnectionID}: {accountList}");
        SendMessageToClient($"{ServerToClientSignifiers.AccountList},{accountList}", clientConnectionID, TransportPipeline.ReliableAndInOrder);
    }


    static public void DisconnectionEvent(int clientConnectionID)
    {
        if (connectionToUsername.ContainsKey(clientConnectionID))
        {
            string disconnectedUsername = connectionToUsername[clientConnectionID];
            connectionToUsername.Remove(clientConnectionID);
            Debug.Log($"Client with ID {clientConnectionID} disconnected. Username: {disconnectedUsername}");
        }
        else
        {
            Debug.Log($"Client with ID {clientConnectionID} disconnected. No associated username.");
        }
    }

    static public void SetGameLogic(GameLogic GameLogic)
    {
        gameLogic = GameLogic;
    }

    private static void LoadAccounts()
    {
        if (!File.Exists(accountFilePath))
            return;

        using (StreamReader reader = new StreamReader(accountFilePath))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] accountData = line.Split(',');
                if (accountData.Length == 2)
                {
                    accounts[accountData[0]] = accountData[1]; // username, password
                }
            }
        }
        Debug.Log("Accounts loaded from file.");
    }

    private static void SaveAccounts()
    {
        using (StreamWriter writer = new StreamWriter(accountFilePath))
        {
            foreach (var account in accounts)
            {
                writer.WriteLine($"{account.Key},{account.Value}"); // username, password
            }
        }
        Debug.Log("Accounts saved to file.");
    }

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }

    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }

    // Initialize a new game board when two players join
    private static void InitializeGame(string roomName)
    {
        if (!gameBoards.ContainsKey(roomName))
        {
            gameBoards[roomName] = new int[3, 3]; // Empty board
            currentTurn[roomName] = gameRooms[roomName][0]; // Set the first player's turn
            Debug.Log($"Turn initialized for room '{roomName}'. Player 1's turn.");
        }
        else
        {
            Debug.LogWarning($"Game board for room '{roomName}' already exists. Reinitializing.");
            gameBoards[roomName] = new int[3, 3]; // Reset board
        }

        currentTurn[roomName] = gameRooms[roomName][0]; // Ensure Player 1 is the first to play
        Debug.Log($"Turn initialized for room '{roomName}'. Player 1's turn.");
    }

    private static void HandlePlayerMove(string roomName, int clientID, int x, int y)
    {
        if (!gameBoards.ContainsKey(roomName) || !currentTurn.ContainsKey(roomName))
        {
            Debug.LogWarning($"Room does not exist or is not properly initialized: {roomName}");
            return;
        }

        Debug.Log($"Processing PlayerMove for Room {roomName} by Client {clientID}: x={x}, y={y}"); // Add this

        int[,] board = gameBoards[roomName];
        int currentPlayer = currentTurn[roomName];

        // Log the current board state before the move
        Debug.Log($"Current board state for Room {roomName}:"); // Add this
        for (int i = 0; i < 3; i++)
        {
            Debug.Log($"{board[i, 0]} {board[i, 1]} {board[i, 2]}");
        }

        // Ensure it's the correct player's turn and the cell is empty
        if (clientID == currentPlayer && board[x, y] == 0)
        {
            int playerMark = (gameRooms[roomName].IndexOf(clientID) == 0) ? 1 : 2; // Player 1 = 1 (X), Player 2 = 2 (O)
            board[x, y] = playerMark;

            // Broadcast the move to all clients in the room
            foreach (int client in gameRooms[roomName])
            {
                Debug.Log($"Sending move update to Client {client}: Player {playerMark} at ({x}, {y})");
                SendMessageToClient($"{ServerToClientSignifiers.PlayerMove},{x},{y},{playerMark}", client, TransportPipeline.ReliableAndInOrder);
            }

            // Check for win/draw conditions
            if (CheckWinCondition(board, playerMark))
            {
                foreach (int client in gameRooms[roomName])
                {
                    Debug.Log($"Player {playerMark} wins. Sending GameResult to Client {client}");
                    SendMessageToClient($"{ServerToClientSignifiers.GameResult},{playerMark}", client, TransportPipeline.ReliableAndInOrder);
                }
                ResetGameRoom(roomName);
            }
            else if (CheckDrawCondition(board))
            {
                foreach (int client in gameRooms[roomName])
                {
                    Debug.Log($"Game Draw. Sending GameResult to Client {client}");
                    SendMessageToClient($"{ServerToClientSignifiers.GameResult},0", client, TransportPipeline.ReliableAndInOrder);
                }
                ResetGameRoom(roomName);
            }
            else
            {
                // Switch turn
                currentTurn[roomName] = gameRooms[roomName][1 - gameRooms[roomName].IndexOf(clientID)];
                foreach (int client in gameRooms[roomName])
                {
                    int isPlayerTurn = (client == currentTurn[roomName]) ? 1 : 0;
                    Debug.Log($"Notifying Client {client} of turn update: IsPlayerTurn = {isPlayerTurn}");
                    SendMessageToClient($"{ServerToClientSignifiers.TurnUpdate},{isPlayerTurn}", client, TransportPipeline.ReliableAndInOrder);
                }
            }
        }
        else
        {
            Debug.LogWarning($"Invalid move or not player's turn. Room: {roomName}, Client: {clientID}");
        }
    }


    private static bool CheckWinCondition(int[,] board, int player)
    {
        // Check rows, columns, and diagonals
        for (int i = 0; i < 3; i++)
        {
            if (board[i, 0] == player && board[i, 1] == player && board[i, 2] == player) return true;
            if (board[0, i] == player && board[1, i] == player && board[2, i] == player) return true;
        }
        if (board[0, 0] == player && board[1, 1] == player && board[2, 2] == player) return true;
        if (board[0, 2] == player && board[1, 1] == player && board[2, 0] == player) return true;

        return false;
    }

    private static bool CheckDrawCondition(int[,] board)
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (board[i, j] == 0) return false; // Empty cell
            }
        }
        return true;
    }

    private static void ResetGameRoom(string roomName)
    {
        if (gameBoards.ContainsKey(roomName))
        {
            gameBoards.Remove(roomName);
            Debug.Log($"Game board for room '{roomName}' removed.");
        }

        if (currentTurn.ContainsKey(roomName))
        {
            currentTurn.Remove(roomName);
            Debug.Log($"Turn information for room '{roomName}' removed.");
        }

        if (gameRooms.ContainsKey(roomName))
        {
            gameRooms[roomName].Clear();
            gameRooms.Remove(roomName);
            Debug.Log($"Room '{roomName}' removed.");
        }
        else
        {
            Debug.LogWarning($"Attempted to reset a non-existent room: {roomName}");
        }
    }

}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int DeleteAccount = 3; // New signifier for deleting accounts

    public const int CreateOrJoinGameRoom = 4;
    public const int LeaveGameRoom = 5;
    public const int SendMessageToOpponent = 6;
    public const int PlayerMove = 11; // Ensure this exists in ClientToServerSignifiers
}

public static class ServerToClientSignifiers
{
    public const int AccountCreated = 1;
    public const int AccountCreationFailed = 2;
    public const int LoginSuccessful = 3;
    public const int LoginFailed = 4;
    public const int AccountList = 5;
    public const int AccountDeleted = 6; // New signifier for successful deletion
    public const int AccountDeletionFailed = 7; // New signifier for failed deletion

    public const int GameRoomCreatedOrJoined = 8;
    public const int StartGame = 9;
    public const int OpponentMessage = 10;

    public const int PlayerMove = 11; // Sent when a player makes a move
    public const int GameResult = 12; // Sent when the game ends
    public const int TurnUpdate = 13; // New signifier for turn updates
}
