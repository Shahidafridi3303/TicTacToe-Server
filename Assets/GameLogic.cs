using System;
using System.Collections.Generic;
using UnityEngine;

public class GameLogic : MonoBehaviour
{
    private static Dictionary<string, List<int>> gameRooms = new();
    private static Dictionary<string, List<int>> observers = new();
    private static Dictionary<string, int[,]> gameBoards = new();
    private static Dictionary<string, int> currentTurn = new();

    public static Dictionary<string, List<int>> GetGameRooms() => gameRooms;
    public static Dictionary<string, List<int>> GetObservers() => observers;

    public static void CreateOrJoinGameRoom(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        string roomName = csv[1];

        if (!gameRooms.ContainsKey(roomName))
        {
            gameRooms[roomName] = new List<int>();
            observers[roomName] = new List<int>();
            gameBoards[roomName] = new int[3, 3]; // Initialize an empty board
        }

        if (gameRooms[roomName].Count < 2)
        {
            gameRooms[roomName].Add(clientConnectionID);
            NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameRoomCreatedOrJoined},{roomName},{gameRooms[roomName].Count}", clientConnectionID, pipeline);

            if (gameRooms[roomName].Count == 2)
            {
                InitializeGame(roomName);

                int player1 = gameRooms[roomName][0];
                int player2 = gameRooms[roomName][1];

                NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},X,1", player1, TransportPipeline.ReliableAndInOrder);
                NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.StartGame},{roomName},O,0", player2, TransportPipeline.ReliableAndInOrder);
            }
        }
        else
        {
            // Add the client as an observer
            observers[roomName].Add(clientConnectionID);

            // Notify the observer that they've joined
            NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.ObserverJoined},{roomName}", clientConnectionID, pipeline);

            // Send the current board state to the observer
            if (gameBoards.ContainsKey(roomName))
            {
                int[,] board = gameBoards[roomName];
                for (int x = 0; x < 3; x++)
                {
                    for (int y = 0; y < 3; y++)
                    {
                        if (board[x, y] != 0) // Only send cells that are already occupied
                        {
                            int playerMark = board[x, y];
                            NetworkServerProcessing.SendMessageToClient(
                                $"{ServerToClientSignifiers.PlayerMove},{x},{y},{playerMark}",
                                clientConnectionID,
                                TransportPipeline.ReliableAndInOrder
                            );
                        }
                    }
                }
            }
        }
    }

    public static void LeaveGameRoom(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        string roomName = csv[1];

        if (gameRooms.ContainsKey(roomName))
        {
            if (gameRooms[roomName].Contains(clientConnectionID))
            {
                // A player is leaving, destroy the room and notify everyone
                Debug.Log($"Player {clientConnectionID} left room {roomName}. Destroying room.");

                // Notify all clients in the room to go back to GameRoomPanel
                foreach (int clientID in gameRooms[roomName])
                {
                    NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameRoomDestroyed}", clientID, TransportPipeline.ReliableAndInOrder);
                }

                // Notify observers as well
                if (observers.ContainsKey(roomName))
                {
                    foreach (int observerID in observers[roomName])
                    {
                        NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameRoomDestroyed}", observerID, TransportPipeline.ReliableAndInOrder);
                    }
                }

                // Remove the room and its data
                gameRooms.Remove(roomName);
                observers.Remove(roomName);
                gameBoards.Remove(roomName);
            }
            else if (observers.ContainsKey(roomName) && observers[roomName].Contains(clientConnectionID))
            {
                // An observer is leaving, just remove them
                observers[roomName].Remove(clientConnectionID);
                Debug.Log($"Observer {clientConnectionID} left room {roomName}. No action required.");
            }
        }
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
            gameBoards[roomName] = new int[3, 3]; // Reset board
        }

        currentTurn[roomName] = gameRooms[roomName][0]; // Ensure Player 1 is the first to play
        Debug.Log($"Turn initialized for room '{roomName}'. Player 1's turn.");
    }

    public static void ObserverJoined(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        string roomName = csv[1];
        Debug.Log($"Joined room {roomName} as an observer.");

        // Send current board state to the observer
        if (gameBoards.ContainsKey(roomName))
        {
            int[,] board = gameBoards[roomName];
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    if (board[x, y] != 0)
                    {
                        int playerMark = board[x, y];
                        NetworkServerProcessing.SendMessageToClient(
                            $"{ServerToClientSignifiers.PlayerMove},{x},{y},{playerMark}",
                            clientConnectionID,
                            TransportPipeline.ReliableAndInOrder
                        );
                    }
                }
            }
        }

        // Notify observer UI
        NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.ObserverJoined},{roomName}", clientConnectionID, pipeline);

    }

    public static void PlayerMove(string[] csv, int clientConnectionID, TransportPipeline pipeline)
    {
        string roomName = csv[1];
        int x = int.Parse(csv[2]);
        int y = int.Parse(csv[3]);

        Debug.Log($"Received PlayerMove from Client {clientConnectionID}: Room {roomName}, x: {x}, y: {y}"); // Add this

        if (!gameBoards.ContainsKey(roomName) || !currentTurn.ContainsKey(roomName))
        {
            Debug.LogWarning($"Room does not exist or is not properly initialized: {roomName}");
            return;
        }

        Debug.Log($"Processing PlayerMove for Room {roomName} by Client {clientConnectionID}: x={x}, y={y}");

        int[,] board = gameBoards[roomName];
        int currentPlayer = currentTurn[roomName];

        if (clientConnectionID == currentPlayer && board[x, y] == 0)
        {
            int playerMark = (gameRooms[roomName].IndexOf(clientConnectionID) == 0) ? 1 : 2;
            board[x, y] = playerMark;

            foreach (int client in gameRooms[roomName])
            {
                NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.PlayerMove},{x},{y},{playerMark}", client, TransportPipeline.ReliableAndInOrder);
            }

            if (observers.ContainsKey(roomName))
            {
                foreach (int observer in observers[roomName])
                {
                    NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.PlayerMove},{x},{y},{playerMark}", observer, TransportPipeline.ReliableAndInOrder);
                }
            }

            if (CheckWinCondition(board, playerMark))
            {
                foreach (int client in gameRooms[roomName])
                {
                    NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameResult},{playerMark}", client, TransportPipeline.ReliableAndInOrder);
                }

                NotifyRoomDestroyed(roomName);
                ResetGameRoom(roomName);
            }
            else if (CheckDrawCondition(board))
            {
                foreach (int client in gameRooms[roomName])
                {
                    NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameResult},0", client, TransportPipeline.ReliableAndInOrder);
                }

                NotifyRoomDestroyed(roomName);
                ResetGameRoom(roomName);
            }
            else
            {
                currentTurn[roomName] = gameRooms[roomName][1 - gameRooms[roomName].IndexOf(clientConnectionID)];
                foreach (int client in gameRooms[roomName])
                {
                    int isPlayerTurn = (client == currentTurn[roomName]) ? 1 : 0;
                    NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.TurnUpdate},{isPlayerTurn}", client, TransportPipeline.ReliableAndInOrder);
                }
            }
        }
        else
        {
            Debug.LogWarning($"Invalid move or not player's turn. Room: {roomName}, Client: {clientConnectionID}");
        }
    }

    private static void NotifyRoomDestroyed(string roomName)
    {
        if (gameRooms.ContainsKey(roomName))
        {
            foreach (int client in gameRooms[roomName])
            {
                NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameRoomDestroyed}", client, TransportPipeline.ReliableAndInOrder);
            }
        }

        if (observers.ContainsKey(roomName))
        {
            foreach (int observer in observers[roomName])
            {
                NetworkServerProcessing.SendMessageToClient($"{ServerToClientSignifiers.GameRoomDestroyed}", observer, TransportPipeline.ReliableAndInOrder);
            }
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

    public static void ClearRoomData(string roomName)
    {
        gameRooms.Remove(roomName);
        observers.Remove(roomName);
        gameBoards.Remove(roomName);
        currentTurn.Remove(roomName);
    }

    public static void ClearAllRooms()
    {
        gameRooms.Clear();
        observers.Clear();
        gameBoards.Clear();
        currentTurn.Clear();
    }
}
