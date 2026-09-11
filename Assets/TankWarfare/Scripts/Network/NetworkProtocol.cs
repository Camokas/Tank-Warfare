using System;
using TankWarfare.Core;

namespace TankWarfare.Network
{
    [Serializable]
    public sealed class NetworkMessage
    {
        public string type;
        public string room;
        public string name;
        public string phase;
        public string matchId;
        public string error;
        public int playerId;
        public int tankType;
        public int seed;
        public int sequence;
        public int winner = -1;
        public int scoreA;
        public int scoreB;
        public int round;
        public float move;
        public float turn;
        public bool fire;
        public PlayerSnapshot[] players;
        public BulletSnapshot[] bullets;
        public WallSnapshot[] walls;
        public MatchPlayerStatistics[] statistics;
    }

    [Serializable]
    public sealed class PlayerSnapshot
    {
        public int id;
        public string name;
        public int tankType;
        public float x;
        public float z;
        public float yaw;
        public float health;
        public float maxHealth;
        public bool alive;
    }

    [Serializable]
    public sealed class BulletSnapshot
    {
        public int id;
        public int owner;
        public float x;
        public float z;
        public float yaw;
        public float speed;
    }

    [Serializable]
    public sealed class WallSnapshot
    {
        public int id;
        public float x;
        public float z;
        public float health;
        public bool destructible;
    }
}
